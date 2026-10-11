using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Tests;

internal static class TestData
{
    public static ColumnInfo Col(string name, string type, string baseType, bool pk = false, bool nullable = false, bool identity = false, long? max = null) =>
        new() { Name = name, DataType = type, BaseType = baseType, IsPrimaryKey = pk, IsNullable = nullable, IsIdentity = identity, MaxLength = max };

    /// <summary>A small shop schema with a few deliberate flaws.</summary>
    public static DatabaseSchema FlawedShop()
    {
        var schema = new DatabaseSchema { DatabaseName = "shop", Engine = DbEngine.PostgreSql, ServerVersion = "16.1" };

        var customers = new TableInfo { Name = "customers", RowCount = 1000, DefaultSchema = "public" };
        customers.Columns.Add(Col("id", "integer", "int4", pk: true, identity: true));
        customers.Columns.Add(Col("email", "varchar(255)", "varchar", max: 255));
        customers.Columns.Add(Col("password", "varchar(40)", "varchar", max: 40));

        var orders = new TableInfo { Name = "orders", RowCount = 500_000, DefaultSchema = "public" };
        orders.Columns.Add(Col("id", "integer", "int4", pk: true, identity: true));
        orders.Columns.Add(Col("customer_id", "bigint", "int8"));          // type mismatch with customers.id
        orders.Columns.Add(Col("total", "double precision", "float8"));    // money as float
        orders.Columns.Add(Col("order_date", "varchar(20)", "varchar", max: 20));

        var log = new TableInfo { Name = "audit_log", RowCount = 10, DefaultSchema = "public" }; // no primary key
        log.Columns.Add(Col("message", "text", "text", nullable: true, max: -1));

        schema.Tables.AddRange(new[] { customers, orders, log });

        var fk = new ForeignKeyInfo { Name = "fk_orders_customer", FromTable = "orders", ToTable = "customers" };
        fk.FromColumns.Add("customer_id");
        fk.ToColumns.Add("id");
        schema.ForeignKeys.Add(fk);

        var pk = new IndexInfo { Table = "customers", Name = "customers_pkey", IsPrimary = true, IsUnique = true };
        pk.Columns.Add("id");
        var pk2 = new IndexInfo { Table = "orders", Name = "orders_pkey", IsPrimary = true, IsUnique = true };
        pk2.Columns.Add("id");
        var a = new IndexInfo { Table = "orders", Name = "ix_a" };
        a.Columns.Add("total");
        var b = new IndexInfo { Table = "orders", Name = "ix_b" };
        b.Columns.Add("total");
        schema.Indexes.AddRange(new[] { pk, pk2, a, b });

        return schema;
    }
}
