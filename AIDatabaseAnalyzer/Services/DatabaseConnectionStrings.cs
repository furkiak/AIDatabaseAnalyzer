using System.Globalization;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace AIDatabaseAnalyzer.Services;

public static class DatabaseConnectionStrings
{
    public static string Build(
        DbEngine engine, string serverAddress, string? port, string? username,
        string? password, bool allowUntrustedCertificate = false)
    {
        var server = serverAddress?.Trim();
        if (string.IsNullOrWhiteSpace(server))
            throw new ArgumentException("Enter a database server address.", nameof(serverAddress));
        var parsedPort = ParsePort(port);
        username = username?.Trim() ?? "";
        password ??= "";

        return engine switch
        {
            DbEngine.SqlServer => SqlServer(server, parsedPort, username, password, allowUntrustedCertificate),
            DbEngine.PostgreSql => PostgreSql(server, parsedPort, username, password, allowUntrustedCertificate),
            DbEngine.MySql => MySql(server, parsedPort, username, password, allowUntrustedCertificate),
            _ => throw new ArgumentOutOfRangeException(nameof(engine))
        };
    }

    public static string WithDatabase(DbEngine engine, string baseConnectionString, string database)
    {
        if (string.IsNullOrWhiteSpace(database))
            throw new ArgumentException("Choose a database.", nameof(database));
        return engine switch
        {
            DbEngine.SqlServer => new SqlConnectionStringBuilder(baseConnectionString)
                { InitialCatalog = database }.ConnectionString,
            DbEngine.PostgreSql => new NpgsqlConnectionStringBuilder(baseConnectionString)
                { Database = database }.ConnectionString,
            DbEngine.MySql => new MySqlConnectionStringBuilder(baseConnectionString)
                { Database = database }.ConnectionString,
            _ => throw new ArgumentOutOfRangeException(nameof(engine))
        };
    }

    private static uint? ParsePort(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!uint.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
            throw new ArgumentException("Port must be a number from 1 to 65535.", nameof(text));
        return port;
    }

    private static string SqlServer(
        string server, uint? port, string username, string password, bool allowUntrustedCertificate)
    {
        if (port.HasValue)
        {
            if (server.Contains('\\') || server.Contains(','))
                throw new ArgumentException("Use either a SQL Server instance or an explicit port, not both.", nameof(server));
            if (server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) server = server[4..];
            server = $"tcp:{server},{port.Value.ToString(CultureInfo.InvariantCulture)}";
        }
        if (username.Length == 0 && password.Length != 0)
            throw new ArgumentException("Enter a user name with the SQL Server password.", nameof(username));
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = allowUntrustedCertificate,
            IntegratedSecurity = username.Length == 0,
            PersistSecurityInfo = false,
            ConnectTimeout = 15
        };
        if (username.Length != 0)
        {
            builder.UserID = username;
            builder.Password = password;
        }
        return builder.ConnectionString;
    }

    private static string PostgreSql(
        string server, uint? port, string username, string password, bool allowUntrustedCertificate)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = server,
            Username = username,
            Password = password,
            SslMode = allowUntrustedCertificate ? SslMode.Require : SslMode.VerifyFull,
            Timeout = 15
        };
        if (port.HasValue) builder.Port = checked((int)port.Value);
        return builder.ConnectionString;
    }

    private static string MySql(
        string server, uint? port, string username, string password, bool allowUntrustedCertificate)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = server,
            UserID = username,
            Password = password,
            SslMode = allowUntrustedCertificate ? MySqlSslMode.Required : MySqlSslMode.VerifyFull,
            ConnectionTimeout = 15
        };
        if (port.HasValue) builder.Port = port.Value;
        return builder.ConnectionString;
    }
}
