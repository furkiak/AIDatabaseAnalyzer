namespace AIDatabaseAnalyzer.Models;

/// <summary>Database engines the analyzer can read metadata from. The numeric values are stored: never reorder.</summary>
public enum DbEngine
{
    SqlServer = 0,
    PostgreSql = 1,
    MySql = 2,
    Sqlite = 3,
    Oracle = 4
}

public static class DbEngineExtensions
{
    public static IReadOnlyList<DbEngine> All { get; } = new[] { DbEngine.SqlServer, DbEngine.PostgreSql, DbEngine.MySql, DbEngine.Oracle, DbEngine.Sqlite };

    public static string DisplayName(this DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => "SQL Server",
        DbEngine.PostgreSql => "PostgreSQL",
        DbEngine.MySql => "MySQL / MariaDB",
        DbEngine.Sqlite => "SQLite",
        DbEngine.Oracle => "Oracle",
        _ => engine.ToString()
    };

    /// <summary>Services that speak the same protocol and can be analysed with the same engine.</summary>
    public static string CompatibleServices(this DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => "Also Azure SQL Database and Azure SQL Managed Instance.",
        DbEngine.PostgreSql => "Also Amazon RDS / Aurora, Azure, Google Cloud SQL, Supabase, Neon, CockroachDB, YugabyteDB and TimescaleDB.",
        DbEngine.MySql => "Also MariaDB, Amazon Aurora MySQL, PlanetScale, TiDB and SingleStore.",
        DbEngine.Oracle => "Oracle Database 12c and later, including Autonomous Database (TLS without wallet). Enter the service name below.",
        DbEngine.Sqlite => "A local database file: read in read-only mode, nothing is changed.",
        _ => ""
    };

    public static int DefaultPort(this DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => 1433,
        DbEngine.PostgreSql => 5432,
        DbEngine.MySql => 3306,
        DbEngine.Oracle => 1521,
        _ => 0
    };

    /// <summary>File based engines have no server, port or credentials.</summary>
    public static bool IsFileBased(this DbEngine engine) => engine == DbEngine.Sqlite;

    /// <summary>Schema that is considered "default" for the engine (hidden in qualified names).</summary>
    public static string? DefaultSchema(this DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => "dbo",
        DbEngine.PostgreSql => "public",
        _ => null
    };
}
