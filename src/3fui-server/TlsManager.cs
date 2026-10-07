using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace linux3fui.Server;

/// <summary>
/// 声明式 HTTPS：Kestrel 恒常监听 HTTPS 端口，每次 TLS 握手通过 ServerCertificateSelector
/// 从数据目录动态读取证书——上传证书后立即生效，无需重启容器；删除证书后立即停用。
/// 同时支持 TLS_CERT_PATH / TLS_KEY_PATH 环境变量（指向证书文件，同样热加载）。
/// </summary>
public sealed class TlsManager
{
    public const string CertFileName = "tls.crt";
    public const string KeyFileName = "tls.key";
    public const string StateFileName = "tls.json";

    private readonly ServerConfig _config;
    private readonly ILogger<TlsManager> _logger;
    private readonly object _lock = new();
    private X509Certificate2? _certificate;
    private DateTime _loadedAtUtc;
    private string? _certSourcePath;
    private string? _keySourcePath;

    public TlsManager(ServerConfig config, ILogger<TlsManager> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string CertDir => Path.Combine(_config.DataDir, "certs");

    /// <summary>证书是否已配置（文件存在且可加载）。</summary>
    public bool Active
    {
        get
        {
            lock (_lock) return _certificate is not null;
        }
    }

    public X509Certificate2? Certificate
    {
        get { lock (_lock) return _certificate; }
    }

    public string? CertPath
    {
        get { lock (_lock) return _certSourcePath; }
    }

    public string? KeyPath
    {
        get { lock (_lock) return _keySourcePath; }
    }

    /// <summary>确定证书来源：环境变量优先，其次数据目录中已上传的证书。</summary>
    private (string? cert, string? key) ResolvePaths()
    {
        if (_config.TlsCertPath is not null && _config.TlsKeyPath is not null)
            return (_config.TlsCertPath, _config.TlsKeyPath);
        var localCert = Path.Combine(CertDir, CertFileName);
        var localKey = Path.Combine(CertDir, KeyFileName);
        if (File.Exists(localCert) && File.Exists(localKey) && File.Exists(Path.Combine(CertDir, StateFileName)))
            return (localCert, localKey);
        return (null, null);
    }

    /// <summary>启动时预加载一次，失败仅告警（选择器会在每次握手时重试）。</summary>
    public void LoadExisting()
    {
        var (cert, key) = ResolvePaths();
        if (cert is null || key is null) return;
        try
        {
            Reload(cert, key);
            _logger.LogInformation("HTTPS 已启用（端口 {Port}），证书 {Cert}", _config.HttpsPort, cert);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("加载证书失败，HTTPS 暂不可用：{Message}", ex.Message);
        }
    }

    /// <summary>Kestrel 握手选择器：每次 TLS 握手调用，证书变化后下一握手即生效。</summary>
    public X509Certificate2? SelectCertificate()
    {
        try
        {
            lock (_lock)
            {
                var (cert, key) = ResolvePaths();
                if (cert is null || key is null)
                {
                    if (_certificate is not null)
                    {
                        _certificate.Dispose();
                        _certificate = null;
                    }
                    return null;
                }
                var writeTime = GetNewestWriteTimeUtc(cert, key);
                if (_certificate is not null && cert == _certSourcePath && key == _keySourcePath && writeTime <= _loadedAtUtc)
                    return _certificate;
                Reload(cert, key);
                return _certificate;
            }
        }
        catch
        {
            return null;
        }
    }

    private void Reload(string certPath, string keyPath)
    {
        var certificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        _certificate?.Dispose();
        _certificate = certificate;
        _certSourcePath = certPath;
        _keySourcePath = keyPath;
        _loadedAtUtc = DateTime.UtcNow;
    }

    private static DateTime GetNewestWriteTimeUtc(string certPath, string keyPath)
    {
        var certTime = File.GetLastWriteTimeUtc(certPath);
        var keyTime = File.GetLastWriteTimeUtc(keyPath);
        return certTime > keyTime ? certTime : keyTime;
    }

    /// <summary>校验并持久化上传的证书/私钥。成功后返回 null，失败返回错误消息。</summary>
    public string? Install(string certPem, string keyPem)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509Certificate2.CreateFromPem(certPem, keyPem);
        }
        catch (Exception ex)
        {
            return "证书或私钥无效：" + ex.Message;
        }
        using (certificate)
        {
            var now = DateTime.UtcNow;
            if (certificate.NotAfter.ToUniversalTime() < now) return "证书已过期（NotAfter 早于当前时间）";
            if (certificate.NotBefore.ToUniversalTime() > now.AddMinutes(5)) return "证书尚未生效（NotBefore 晚于当前时间）";
        }

        Directory.CreateDirectory(CertDir);
        var certPath = Path.Combine(CertDir, CertFileName);
        var keyPath = Path.Combine(CertDir, KeyFileName);
        File.WriteAllText(certPath, certPem);
        File.WriteAllText(keyPath, keyPem);
        File.WriteAllText(Path.Combine(CertDir, StateFileName), "{}");
        // 私钥仅属主可读（证书是公开的，无需收紧）
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // 立即生效：下一次 TLS 握手将由选择器读取新证书。
        try
        {
            lock (_lock) Reload(certPath, keyPath);
        }
        catch (Exception ex)
        {
            return "证书有效但加载失败：" + ex.Message;
        }
        _logger.LogInformation("HTTPS 证书已更新：{Subject}", _certificate?.Subject);
        return null;
    }

    public void Uninstall()
    {
        lock (_lock)
        {
            _certificate?.Dispose();
            _certificate = null;
            _certSourcePath = null;
            _keySourcePath = null;
        }
        foreach (var file in new[] { CertFileName, KeyFileName, StateFileName })
        {
            var path = Path.Combine(CertDir, file);
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
        _logger.LogInformation("HTTPS 证书已删除");
    }

    /// <summary>配置 Kestrel HTTPS 端口：恒常监听，证书由选择器按需提供。</summary>
    public void ConfigureHttpsPort(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listen)
    {
        var https = new HttpsConnectionAdapterOptions
        {
            ServerCertificateSelector = (_, _) => SelectCertificate(),
        };
        listen.UseHttps(https);
    }
}
