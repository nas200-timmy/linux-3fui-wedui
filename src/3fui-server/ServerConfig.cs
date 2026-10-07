namespace linux3fui.Server;

/// <summary>环境变量驱动的服务端配置。</summary>
public sealed class ServerConfig
{
    public required string DataDir { get; init; }
    public required int HttpPort { get; init; }
    public required int HttpsPort { get; init; }
    public string? MediaRoot { get; init; }
    public string? TlsCertPath { get; init; }
    public string? TlsKeyPath { get; init; }

    public static ServerConfig FromEnvironment()
    {
        var dataDir = Env("DATA_DIR") ?? Path.Combine(AppContext.BaseDirectory, "data");
        var mediaRoot = Env("MEDIA_ROOT");
        if (mediaRoot is null && Directory.Exists("/media")) mediaRoot = "/media";
        return new ServerConfig
        {
            DataDir = dataDir,
            HttpPort = ParsePort(Env("HTTP_PORT"), 8080),
            HttpsPort = ParsePort(Env("HTTPS_PORT"), 8443),
            MediaRoot = mediaRoot,
            TlsCertPath = Env("TLS_CERT_PATH"),
            TlsKeyPath = Env("TLS_KEY_PATH"),
        };
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int ParsePort(string? raw, int fallback) =>
        int.TryParse(raw, out var port) && port is >= 1 and <= 65535 ? port : fallback;
}
