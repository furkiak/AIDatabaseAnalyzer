namespace AIDatabaseAnalyzer.Models;

/// <summary>Everything needed to open a connection to a server. Passwords never end up in a connection string literal.</summary>
public sealed class ConnectionSettings
{
    public DbEngine Engine { get; set; } = DbEngine.SqlServer;
    public string Server { get; set; } = "";
    public int? Port { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Oracle only: the service name of the pluggable database (for example FREEPDB1 or ORCLPDB1).</summary>
    public string ServiceName { get; set; } = "";

    /// <summary>SQL Server only: use Windows (integrated) authentication.</summary>
    public bool UseIntegratedSecurity { get; set; }

    /// <summary>Accept untrusted certificates. Convenient for local dev servers, unsafe for production.</summary>
    public bool TrustServerCertificate { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>A saved connection. The password is stored separately in secure storage.</summary>
public sealed class SavedConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int Engine { get; set; }
    public string Server { get; set; } = "";
    public int? Port { get; set; }
    public string Username { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public bool UseIntegratedSecurity { get; set; }
    public bool TrustServerCertificate { get; set; } = true;
    public bool HasSavedPassword { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => (DbEngine)Engine == DbEngine.Sqlite
        ? $"SQLite · {Path.GetFileName(Server)}"
        : $"{((DbEngine)Engine).DisplayName()} · {Server}";
}
