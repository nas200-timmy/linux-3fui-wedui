using System.Security.Cryptography;
using System.Text.Json;

namespace linux3fui.Server;

/// <summary>
/// 可选登录认证：配置存 数据目录/Auth.json（与 Settings.json 分离），密码 PBKDF2-SHA256 哈希存放。
/// 防爆破：每 3 分钟窗口内最多允许 15 次密码错误，窗口到期自动重置。
/// </summary>
public sealed class AuthService
{
    private const int HashIterations = 100_000;
    private const int MaxFailuresPerWindow = 15;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(3);

    private readonly string _configPath;
    private readonly object _lock = new();

    private bool _enabled;
    private string _username = "";
    private string _passwordHash = "";
    private string _salt = "";

    // token -> 过期时间（内存会话，重启即失效）
    private readonly Dictionary<string, DateTime> _sessions = new();

    // 防爆破计数（全局窗口，与 IP 无关：服务通常只有一两个管理员）
    private DateTime _windowStart = DateTime.UtcNow;
    private int _failures;

    public AuthService(string dataDir)
    {
        _configPath = Path.Combine(dataDir, "Auth.json");
        Load();
    }

    public bool Enabled { get { lock (_lock) return _enabled; } }

    private void Load()
    {
        try
        {
            if (!File.Exists(_configPath)) return;
            var doc = JsonDocument.Parse(File.ReadAllText(_configPath)).RootElement;
            lock (_lock)
            {
                _enabled = doc.TryGetProperty("enabled", out var e) && e.GetBoolean();
                _username = doc.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
                _passwordHash = doc.TryGetProperty("passwordHash", out var h) ? h.GetString() ?? "" : "";
                _salt = doc.TryGetProperty("salt", out var s) ? s.GetString() ?? "" : "";
            }
        }
        catch
        {
            // 配置损坏视为未启用，避免把管理员锁在门外
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(new { enabled = _enabled, username = _username, passwordHash = _passwordHash, salt = _salt });
            File.WriteAllText(_configPath, json);
            // 凭据文件仅属主可读（Settings.json 同理收紧；Windows 上该 API 无意义故跳过）
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_configPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public object Status(bool authenticated) => new { enabled = Enabled, authenticated };

    /// <summary>启用/停用或修改凭据。停用不需要旧密码（调用方已要求已认证）。</summary>
    public string? Configure(bool enable, string? username, string? password)
    {
        lock (_lock)
        {
            if (!enable)
            {
                _enabled = false;
                Save();
                return null;
            }
            if (string.IsNullOrWhiteSpace(username)) return "用户名不能为空";
            if (string.IsNullOrWhiteSpace(password) && (string.IsNullOrEmpty(_passwordHash) || username != _username))
                return "首次启用或修改用户名时必须设置密码";
            _username = username.Trim();
            if (!string.IsNullOrWhiteSpace(password))
            {
                if (password.Length < 6) return "密码至少 6 位";
                _salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                _passwordHash = Hash(password, _salt);
                // 改密码吊销全部旧会话（旧 token 不应继续有效）
                _sessions.Clear();
            }
            _enabled = true;
            Save();
            return null;
        }
    }

    /// <summary>返回 (是否成功, 剩余秒数[被锁定时])</summary>
    public (bool ok, int lockedSeconds) TryLogin(string? username, string? password)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (now - _windowStart >= Window)
            {
                _windowStart = now;
                _failures = 0;
            }
            if (_failures >= MaxFailuresPerWindow)
                return (false, (int)Math.Ceiling((Window - (now - _windowStart)).TotalSeconds));

            var ok = _enabled && !string.IsNullOrEmpty(_passwordHash)
                     && username == _username
                     && !string.IsNullOrEmpty(password)
                     && Hash(password, _salt) == _passwordHash;
            if (!ok) _failures++;
            else _failures = 0;
            return (ok, 0);
        }
    }

    public string CreateSession()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_lock)
        {
            _sessions[token] = DateTime.UtcNow.AddDays(7);
            foreach (var key in _sessions.Where(pair => pair.Value < DateTime.UtcNow).Select(pair => pair.Key).ToList())
                _sessions.Remove(key);
        }
        return token;
    }

    public bool Validate(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (_lock)
            return _sessions.TryGetValue(token, out var expiry) && expiry > DateTime.UtcNow;
    }

    public void Revoke(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        lock (_lock) _sessions.Remove(token);
    }

    private static string Hash(string password, string saltHex)
    {
        var bytes = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromHexString(saltHex), HashIterations, HashAlgorithmName.SHA256, 32);
        return Convert.ToHexString(bytes);
    }
}
