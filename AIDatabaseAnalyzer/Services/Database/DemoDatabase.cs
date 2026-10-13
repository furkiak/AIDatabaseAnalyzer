using Microsoft.Data.Sqlite;

namespace AIDatabaseAnalyzer.Services.Database;

/// <summary>
/// A sample shop database, generated on demand, so the app can be tried without a database server.
/// Its design has typical real-world flaws (plain-text passwords, money stored as REAL, a missing
/// foreign key, unindexed foreign keys, a duplicate index, tables without a key...) for the
/// insights, the diagram and the AI analysis to find. All data is fictitious.
/// </summary>
public static class DemoDatabase
{
    public const string FileName = "demo-shop.sqlite";

    /// <summary>Bumped whenever <see cref="Script"/> changes, so an outdated copy is rebuilt.</summary>
    public const int Version = 1;

    /// <summary>Creates (or refreshes) the demo database in <paramref name="folder"/> and returns its path.</summary>
    public static async Task<string> EnsureAsync(string folder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, FileName);
        if (File.Exists(path) && await ReadVersionAsync(path, ct).ConfigureAwait(false) == Version) return path;

        string temp = path + ".tmp";
        File.Delete(temp);
        await using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ConnectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = Script + $"\nPRAGMA user_version = {Version};";
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        File.Move(temp, path, overwrite: true);
        return path;
    }

    private static async Task<long> ReadVersionAsync(string path, CancellationToken ct)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            await using var conn = new SqliteConnection(builder.ConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version";
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }
        catch (SqliteException)
        {
            return -1; // damaged or not a database: rebuild it
        }
    }

    /// <summary>The schema and the generated rows.</summary>
    public const string Script = """
        CREATE TABLE customers (
          id          INTEGER PRIMARY KEY AUTOINCREMENT,
          email       VARCHAR(255) NOT NULL UNIQUE,
          password    VARCHAR(64) NOT NULL,
          first_name  VARCHAR(60),
          last_name   VARCHAR(60),
          phone       VARCHAR(30),
          birth_date  TEXT,
          national_id VARCHAR(20),
          created_at  TEXT DEFAULT CURRENT_TIMESTAMP,
          CONSTRAINT ck_customers_email CHECK (email LIKE '%@%')
        );

        CREATE TABLE addresses (
          id          INTEGER PRIMARY KEY,
          customer_id INTEGER NOT NULL REFERENCES customers(id),
          line1       VARCHAR(200) NOT NULL,
          city        VARCHAR(80) NOT NULL,
          postal_code VARCHAR(12),
          country     CHAR(2) NOT NULL DEFAULT 'TR',
          is_default  INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE categories (
          id        INTEGER PRIMARY KEY,
          parent_id INTEGER REFERENCES categories(id),
          name      VARCHAR(80) NOT NULL
        );

        CREATE TABLE products (
          id          INTEGER PRIMARY KEY,
          sku         VARCHAR(20) NOT NULL UNIQUE,
          name        VARCHAR(120) NOT NULL,
          category_id INTEGER REFERENCES categories(id),
          price       REAL NOT NULL CHECK (price >= 0),
          stock       INTEGER NOT NULL DEFAULT 0,
          is_active   INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE orders (
          id                  INTEGER PRIMARY KEY,
          customer_id         INTEGER NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
          shipping_address_id INTEGER REFERENCES addresses(id),
          status              VARCHAR(20) NOT NULL DEFAULT 'pending',
          total               REAL,
          order_date          TEXT NOT NULL,
          CONSTRAINT ck_orders_status CHECK (status IN ('pending', 'paid', 'shipped', 'delivered', 'cancelled'))
        );

        CREATE TABLE order_items (
          order_id   INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
          line_no    INTEGER NOT NULL,
          product_id INTEGER NOT NULL REFERENCES products(id),
          quantity   INTEGER NOT NULL CHECK (quantity > 0),
          unit_price NUMERIC(10, 2) NOT NULL,
          PRIMARY KEY (order_id, line_no)
        );

        CREATE TABLE payments (
          id          INTEGER PRIMARY KEY,
          order_id    INTEGER NOT NULL REFERENCES orders(id),
          amount      REAL NOT NULL,
          method      VARCHAR(20) NOT NULL,
          card_number VARCHAR(19),
          paid_at     TEXT NOT NULL
        );

        -- order_id has no REFERENCES clause: a probable missing foreign key
        CREATE TABLE shipments (
          id          INTEGER PRIMARY KEY,
          order_id    INTEGER NOT NULL,
          carrier     VARCHAR(40) NOT NULL,
          tracking_no VARCHAR(40),
          shipped_at  TEXT
        );

        CREATE TABLE reviews (
          id          INTEGER PRIMARY KEY,
          product_id  INTEGER NOT NULL REFERENCES products(id),
          customer_id INTEGER NOT NULL REFERENCES customers(id),
          rating      INTEGER NOT NULL CHECK (rating BETWEEN 1 AND 5),
          body        TEXT,
          created_at  TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE wishlists (
          customer_id INTEGER NOT NULL REFERENCES customers(id),
          product_id  INTEGER NOT NULL REFERENCES products(id),
          PRIMARY KEY (customer_id, product_id)
        );

        -- no primary key, and not related to anything
        CREATE TABLE coupons (code VARCHAR(20), discount REAL, valid_until TEXT);
        CREATE TABLE audit_log (event VARCHAR(40), detail TEXT, created_at TEXT DEFAULT CURRENT_TIMESTAMP);

        CREATE INDEX ix_orders_status ON orders(status);
        CREATE INDEX ix_orders_status_2 ON orders(status);
        CREATE INDEX ix_products_category ON products(category_id);
        CREATE INDEX ix_orders_open ON orders(order_date) WHERE status IN ('pending', 'paid');

        CREATE VIEW v_order_totals AS
          SELECT o.id AS order_id, o.customer_id, SUM(i.quantity * i.unit_price) AS items_total
          FROM orders o JOIN order_items i ON i.order_id = o.id
          GROUP BY o.id, o.customer_id;

        CREATE TRIGGER trg_orders_cancelled AFTER UPDATE OF status ON orders
          WHEN NEW.status = 'cancelled'
        BEGIN
          INSERT INTO audit_log(event, detail) VALUES ('order_cancelled', 'order ' || NEW.id);
        END;

        -- ---------------------------------------------------------------- data (fictitious)

        INSERT INTO categories(id, parent_id, name) VALUES
          (1, NULL, 'Electronics'), (2, 1, 'Phones'), (3, 1, 'Laptops'), (4, 1, 'Audio'),
          (5, NULL, 'Home'), (6, 5, 'Kitchen'), (7, 5, 'Lighting'), (8, NULL, 'Books');

        WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 80)
        INSERT INTO customers(email, password, first_name, last_name, phone, birth_date, national_id)
        SELECT
          'customer' || i || '@example.com',
          'Welcome' || (i * 37 % 1000),
          CASE i % 8 WHEN 0 THEN 'Ayşe' WHEN 1 THEN 'Mehmet' WHEN 2 THEN 'Elif' WHEN 3 THEN 'Can'
                     WHEN 4 THEN 'Zeynep' WHEN 5 THEN 'Emre' WHEN 6 THEN 'Selin' ELSE 'Burak' END,
          CASE i % 6 WHEN 0 THEN 'Yılmaz' WHEN 1 THEN 'Kaya' WHEN 2 THEN 'Demir' WHEN 3 THEN 'Şahin'
                     WHEN 4 THEN 'Çelik' ELSE 'Arslan' END,
          '+90 555 ' || printf('%03d %04d', i * 13 % 1000, i * 791 % 10000),
          date('1975-01-01', '+' || (i * 211 % 12000) || ' days'),
          printf('%011d', 10000000000 + i * 7919)
        FROM n;

        INSERT INTO addresses(id, customer_id, line1, city, postal_code, is_default)
        SELECT id, id, 'Example Street ' || id, CASE id % 4 WHEN 0 THEN 'İstanbul' WHEN 1 THEN 'Ankara' WHEN 2 THEN 'İzmir' ELSE 'Bursa' END,
               printf('%05d', 34000 + id), 1
        FROM customers;

        WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 48)
        INSERT INTO products(id, sku, name, category_id, price, stock)
        SELECT i, printf('SKU-%04d', i),
               CASE (i % 7) + 2 WHEN 2 THEN 'Phone' WHEN 3 THEN 'Laptop' WHEN 4 THEN 'Headphones' WHEN 6 THEN 'Coffee maker'
                                WHEN 7 THEN 'Desk lamp' WHEN 8 THEN 'Novel' ELSE 'Accessory' END || ' ' || i,
               CASE (i % 7) + 2 WHEN 5 THEN 1 ELSE (i % 7) + 2 END,
               round(9.99 + (i * 37 % 400) * 2.5, 2),
               i * 17 % 120
        FROM n;

        WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 320)
        INSERT INTO orders(id, customer_id, shipping_address_id, status, order_date)
        SELECT i, (i * 7 % 80) + 1, (i * 7 % 80) + 1,
               CASE i % 9 WHEN 0 THEN 'cancelled' WHEN 1 THEN 'pending' WHEN 2 THEN 'paid' WHEN 3 THEN 'shipped' ELSE 'delivered' END,
               date('2026-01-01', '+' || (i * 13 % 280) || ' days')
        FROM n;

        WITH RECURSIVE line(l) AS (SELECT 1 UNION ALL SELECT l + 1 FROM line WHERE l < 3)
        INSERT INTO order_items(order_id, line_no, product_id, quantity, unit_price)
        SELECT o.id, line.l, ((o.id * 11 + line.l * 5) % 48) + 1, (o.id + line.l) % 3 + 1, p.price
        FROM orders o
        JOIN line ON line.l <= (o.id % 3) + 1
        JOIN products p ON p.id = ((o.id * 11 + line.l * 5) % 48) + 1;

        UPDATE orders SET total = (SELECT round(SUM(quantity * unit_price), 2) FROM order_items WHERE order_id = orders.id);

        INSERT INTO payments(order_id, amount, method, card_number, paid_at)
        SELECT id, total, CASE id % 3 WHEN 0 THEN 'transfer' ELSE 'card' END,
               CASE id % 3 WHEN 0 THEN NULL ELSE '4111 1111 1111 ' || printf('%04d', id * 37 % 10000) END,
               datetime(order_date, '+2 hours')
        FROM orders WHERE status IN ('paid', 'shipped', 'delivered');

        INSERT INTO shipments(order_id, carrier, tracking_no, shipped_at)
        SELECT id, CASE id % 2 WHEN 0 THEN 'Aras' ELSE 'Yurtiçi' END, printf('TR%010d', id * 104729), date(order_date, '+1 day')
        FROM orders WHERE status IN ('shipped', 'delivered');

        WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 150)
        INSERT INTO reviews(product_id, customer_id, rating, body)
        SELECT (i * 5 % 48) + 1, (i * 3 % 80) + 1, (i % 5) + 1,
               CASE i % 4 WHEN 0 THEN 'Great value.' WHEN 1 THEN 'Arrived late.' WHEN 2 THEN NULL ELSE 'Would buy again.' END
        FROM n;

        INSERT INTO coupons(code, discount, valid_until) VALUES ('WELCOME10', 0.10, '2026-12-31'), ('SPRING15', 0.15, '2026-05-31');

        UPDATE orders SET status = 'cancelled' WHERE id IN (5, 14);
        """;
}
