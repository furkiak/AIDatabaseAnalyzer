using AIDatabaseAnalyzer.Services;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace AIDatabaseAnalyzer.ConnectionTests;

[Collection("SQL Server fixtures")]
public class DatabaseConnectionStringsTests
{
    [Fact]
    public void SqlServerPreservesDelimitersAndUsesValidatedTls()
    {
        const string password = "s3cret;Encrypt=False;TrustServerCertificate=True;";
        var connection = DatabaseConnectionStrings.Build(
            DbEngine.SqlServer, "sql.example.test", "14330", "reader", password);
        var parsed = new SqlConnectionStringBuilder(connection);
        Assert.Equal("tcp:sql.example.test,14330", parsed.DataSource);
        Assert.Equal(password, parsed.Password);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, parsed.Encrypt);
        Assert.False(parsed.TrustServerCertificate);

        var withDatabase = new SqlConnectionStringBuilder(
            DatabaseConnectionStrings.WithDatabase(DbEngine.SqlServer, connection, "sample;Database=master"));
        Assert.Equal("sample;Database=master", withDatabase.InitialCatalog);
        Assert.Equal(password, withDatabase.Password);
    }

    [Fact]
    public void SqlServerSupportsIntegratedLoginAndRejectsAmbiguousOrInvalidPort()
    {
        var integrated = new SqlConnectionStringBuilder(
            DatabaseConnectionStrings.Build(DbEngine.SqlServer, @"localhost\SQLEXPRESS", "", "", ""));
        Assert.True(integrated.IntegratedSecurity);
        Assert.Throws<ArgumentException>(() => DatabaseConnectionStrings.Build(
            DbEngine.SqlServer, @"localhost\SQLEXPRESS", "1433", "reader", "pw"));
        Assert.Throws<ArgumentException>(() => DatabaseConnectionStrings.Build(
            DbEngine.SqlServer, "host", "65536", "reader", "pw"));
        Assert.Throws<ArgumentException>(() => DatabaseConnectionStrings.Build(
            DbEngine.SqlServer, "host", "-1", "reader", "pw"));
    }

    [Fact]
    public void PostgreSqlAndMySqlRequireCertificateValidationByDefault()
    {
        const string password = "part1;Password=changed";
        var pg = new NpgsqlConnectionStringBuilder(DatabaseConnectionStrings.Build(
            DbEngine.PostgreSql, "pg.example.test", "5433", "reader", password));
        Assert.Equal(SslMode.VerifyFull, pg.SslMode);
        Assert.Equal(password, pg.Password);
        Assert.Equal(5433, pg.Port);
        Assert.Equal("db;Host=other", new NpgsqlConnectionStringBuilder(
            DatabaseConnectionStrings.WithDatabase(DbEngine.PostgreSql, pg.ConnectionString, "db;Host=other")).Database);

        var mysql = new MySqlConnectionStringBuilder(DatabaseConnectionStrings.Build(
            DbEngine.MySql, "my.example.test", "3307", "reader", password));
        Assert.Equal(MySqlSslMode.VerifyFull, mysql.SslMode);
        Assert.Equal(password, mysql.Password);
        Assert.Equal((uint)3307, mysql.Port);
        Assert.Equal("db;Server=other", new MySqlConnectionStringBuilder(
            DatabaseConnectionStrings.WithDatabase(DbEngine.MySql, mysql.ConnectionString, "db;Server=other")).Database);
    }

    [Fact]
    public void LocalCertificateExceptionStillRequiresEncryption()
    {
        var sql = new SqlConnectionStringBuilder(DatabaseConnectionStrings.Build(
            DbEngine.SqlServer, "localhost", null, "reader", "pw", allowUntrustedCertificate: true));
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, sql.Encrypt);
        Assert.True(sql.TrustServerCertificate);
        var pg = new NpgsqlConnectionStringBuilder(DatabaseConnectionStrings.Build(
            DbEngine.PostgreSql, "localhost", null, "reader", "pw", allowUntrustedCertificate: true));
        Assert.Equal(SslMode.Require, pg.SslMode);
        var mysql = new MySqlConnectionStringBuilder(DatabaseConnectionStrings.Build(
            DbEngine.MySql, "localhost", null, "reader", "pw", allowUntrustedCertificate: true));
        Assert.Equal(MySqlSslMode.Required, mysql.SslMode);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task LocalUntrustedSqlServerCertificateIsRejectedUnlessExplicitlyAllowed()
    {
        var server = Environment.GetEnvironmentVariable("AIDB_TEST_SQLSERVER")
            ?? throw new InvalidOperationException("Set AIDB_TEST_SQLSERVER to an isolated SQL Server test instance.");
        var strict = DatabaseConnectionStrings.Build(DbEngine.SqlServer, server, null, "", "");
        await using (var connection = new SqlConnection(strict))
            await Assert.ThrowsAsync<SqlException>(() => connection.OpenAsync());

        var localException = DatabaseConnectionStrings.Build(
            DbEngine.SqlServer, server, null, "", "", allowUntrustedCertificate: true);
        await using var accepted = new SqlConnection(localException);
        await accepted.OpenAsync();
        Assert.Equal(System.Data.ConnectionState.Open, accepted.State);
    }
}
