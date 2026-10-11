namespace AIDatabaseAnalyzer.Models;

/// <summary>Database engines the analyzer can read metadata from.</summary>
public enum DbEngine
{
    SqlServer = 0,
    PostgreSql = 1,
    MySql = 2
}

public static class DbEngineExtensions
{
    public static string DisplayName(this DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => "SQL Server",
        DbEngine.PostgreSql => "PostgreSQL",
        DbEngine.MySql => "MySQL / MariaDB",
        _ => engine.ToString()
    };

    public static int DefaultPort(this DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => 1433,
        DbEngine.PostgreSql => 5432,
        DbEngine.MySql => 3306,
        _ => 0
    };

    /// <summary>Schema that is considered "default" for the engine (hidden in qualified names).</summary>
    public static string? DefaultSchema(this DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => "dbo",
        DbEngine.PostgreSql => "public",
        _ => null
    };
}
