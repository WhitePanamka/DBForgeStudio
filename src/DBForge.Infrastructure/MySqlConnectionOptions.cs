namespace DBForge.Infrastructure;

public sealed class SqlConnectionOptions
{
    public string Server { get; set; } = "localhost";
    public int Port { get; set; } = 1433;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Database { get; set; }
    public bool UseWindowsAuth { get; set; } = true;

    public string BuildConnectionString(bool includeDatabase = true)
    {
        var targetServer = string.IsNullOrWhiteSpace(Server) ? "localhost" : Server;
        var db = (includeDatabase && !string.IsNullOrWhiteSpace(Database)) ? Database : "master";

        if (UseWindowsAuth || string.IsNullOrWhiteSpace(User))
        {
            return $"Server={targetServer};Database={db};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5;";
        }

        return $"Server={targetServer};Database={db};User Id={User};Password={Password};TrustServerCertificate=True;Connect Timeout=5;";
    }
}