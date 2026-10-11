using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Database;

/// <summary>
/// Engine specific catalog queries. Every query returns a fixed, engine independent column layout
/// (documented on each member) so <see cref="DbSchemaService"/> can read all engines with the same code.
/// </summary>
public static class SqlCatalog
{
    private static readonly string[] ExcludedBinaryTypes =
    {
        "image", "binary", "varbinary", "timestamp", "rowversion", "geometry", "geography", "hierarchyid", "sql_variant",
        "bytea", "blob", "tinyblob", "mediumblob", "longblob", "bit varying", "point", "linestring", "polygon", "multipoint",
        "multilinestring", "multipolygon", "geometrycollection",
        "raw", "long raw", "long", "bfile", "sdo_geometry", "ordsys.ordimage"
    };

    /// <summary>Oracle: every catalog query is scoped to the schema chosen with ALTER SESSION SET CURRENT_SCHEMA.</summary>
    private const string OracleSchema = "SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')";

    private const string OracleSource =
        "(SELECT LISTAGG(s.text, '' ON OVERFLOW TRUNCATE '…' WITHOUT COUNT) WITHIN GROUP (ORDER BY s.line) " +
        "FROM all_source s WHERE s.owner = o.owner AND s.name = o.object_name AND s.type = o.object_type)";

    /// <summary>SQLite: user tables only (internal sqlite_* tables are hidden).</summary>
    private const string SqliteTables = "m.type = 'table' AND m.name NOT LIKE 'sqlite\\_%' ESCAPE '\\'";

    /// <summary>True when sample queries should skip this column (large or unreadable binary payloads).</summary>
    public static bool IsBinaryLike(string baseType) =>
        ExcludedBinaryTypes.Contains(baseType, StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- identifiers

    public static string Quote(DbEngine engine, string identifier) => engine switch
    {
        DbEngine.SqlServer => "[" + identifier.Replace("]", "]]") + "]",
        DbEngine.MySql => "`" + identifier.Replace("`", "``") + "`",
        _ => "\"" + identifier.Replace("\"", "\"\"") + "\"" // PostgreSQL, Oracle, SQLite
    };

    public static string QuoteTable(DbEngine engine, TableInfo table) =>
        string.IsNullOrEmpty(table.Schema)
            ? Quote(engine, table.Name)
            : Quote(engine, table.Schema) + "." + Quote(engine, table.Name);

    // ---------------------------------------------------------------- databases

    /// <summary>Columns: name.</summary>
    public static string Databases(DbEngine engine) => engine switch
    {
        DbEngine.SqlServer =>
            "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name",
        DbEngine.PostgreSql =>
            "SELECT datname FROM pg_database WHERE datistemplate = false AND datallowconn = true ORDER BY datname",
        // Oracle: a "database" is a schema (owner) that holds tables, without the ones Oracle maintains itself.
        DbEngine.Oracle =>
            "SELECT u.username FROM all_users u WHERE u.oracle_maintained = 'N' " +
            "AND EXISTS (SELECT 1 FROM all_tables t WHERE t.owner = u.username) ORDER BY u.username",
        DbEngine.Sqlite => "SELECT 'main'",
        _ =>
            "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA " +
            "WHERE SCHEMA_NAME NOT IN ('information_schema','mysql','performance_schema','sys') ORDER BY SCHEMA_NAME"
    };

    // ---------------------------------------------------------------- tables

    /// <summary>Columns: schema, table, estimated_rows.</summary>
    public static string Tables(DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => """
            SELECT s.name, t.name, CAST(ISNULL(SUM(p.rows), 0) AS bigint)
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            WHERE t.is_ms_shipped = 0
            GROUP BY s.name, t.name
            ORDER BY s.name, t.name
            """,
        DbEngine.PostgreSql => """
            SELECT n.nspname, c.relname, CASE WHEN c.reltuples < 0 THEN NULL ELSE c.reltuples::bigint END
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p')
              AND NOT c.relispartition
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname NOT LIKE 'pg\_toast%' AND n.nspname NOT LIKE 'pg\_temp%'
              AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype = 'e')
            ORDER BY n.nspname, c.relname
            """,
        DbEngine.Oracle => $"""
            SELECT NULL, t.table_name, t.num_rows
            FROM all_tables t
            WHERE t.owner = {OracleSchema} AND t.dropped = 'NO' AND t.nested = 'NO' AND t.secondary = 'N'
              AND NOT EXISTS (SELECT 1 FROM all_mviews mv WHERE mv.owner = t.owner AND mv.mview_name = t.table_name)
            ORDER BY t.table_name
            """,
        // Row counts are filled in separately (SQLite keeps no statistics by default).
        DbEngine.Sqlite => $"""
            SELECT NULL, m.name, NULL
            FROM sqlite_master m
            WHERE {SqliteTables}
            ORDER BY m.name
            """,
        _ => """
            SELECT NULL, TABLE_NAME, TABLE_ROWS
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME
            """
    };

    // ---------------------------------------------------------------- columns

    /// <summary>Columns: schema, table, column, base_type, full_type, max_len, is_nullable, is_identity, default.</summary>
    public static string Columns(DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => """
            SELECT s.name, t.name, c.name, ty.name,
                   CASE
                     WHEN ty.name IN ('varchar', 'char', 'varbinary', 'binary')
                       THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length AS varchar(10)) END + ')'
                     WHEN ty.name IN ('nvarchar', 'nchar')
                       THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                     WHEN ty.name IN ('decimal', 'numeric')
                       THEN ty.name + '(' + CAST(c.precision AS varchar(5)) + ',' + CAST(c.scale AS varchar(5)) + ')'
                     WHEN ty.name IN ('datetime2', 'datetimeoffset', 'time')
                       THEN ty.name + '(' + CAST(c.scale AS varchar(5)) + ')'
                     ELSE ty.name
                   END,
                   CASE
                     WHEN ty.name IN ('text', 'ntext', 'image', 'xml') THEN -1
                     WHEN ty.name IN ('varchar', 'char', 'varbinary', 'binary') THEN c.max_length
                     WHEN ty.name IN ('nvarchar', 'nchar') THEN CASE WHEN c.max_length = -1 THEN -1 ELSE c.max_length / 2 END
                     ELSE NULL
                   END,
                   c.is_nullable, c.is_identity, dc.definition
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, c.column_id
            """,
        DbEngine.PostgreSql => """
            SELECT n.nspname, c.relname, a.attname, t.typname, format_type(a.atttypid, a.atttypmod),
                   CASE
                     WHEN t.typname IN ('varchar', 'bpchar') AND a.atttypmod > 0 THEN a.atttypmod - 4
                     WHEN t.typname IN ('text', 'bytea', 'json', 'jsonb', 'xml') THEN -1
                     ELSE NULL
                   END,
                   NOT a.attnotnull,
                   (a.attidentity IN ('a', 'd') OR COALESCE(pg_get_expr(d.adbin, d.adrelid) LIKE 'nextval(%', false)),
                   pg_get_expr(d.adbin, d.adrelid)
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_type t ON t.oid = a.atttypid
            LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE a.attnum > 0 AND NOT a.attisdropped
              AND c.relkind IN ('r', 'p')
              AND NOT c.relispartition
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname NOT LIKE 'pg\_toast%' AND n.nspname NOT LIKE 'pg\_temp%'
            ORDER BY n.nspname, c.relname, a.attnum
            """,
        DbEngine.Oracle => $"""
            SELECT NULL, c.table_name, c.column_name, c.data_type,
                   CASE
                     WHEN c.data_type IN ('VARCHAR2', 'NVARCHAR2', 'CHAR', 'NCHAR')
                       THEN c.data_type || '(' || c.char_length || CASE WHEN c.char_used = 'C' AND c.data_type IN ('VARCHAR2', 'CHAR') THEN ' CHAR' END || ')'
                     WHEN c.data_type = 'RAW' THEN 'RAW(' || c.data_length || ')'
                     WHEN c.data_type = 'NUMBER' AND c.data_precision IS NOT NULL
                       THEN 'NUMBER(' || c.data_precision || ',' || c.data_scale || ')'
                     WHEN c.data_type = 'NUMBER' AND c.data_scale = 0 THEN 'INTEGER'
                     WHEN c.data_type = 'FLOAT' THEN 'FLOAT(' || c.data_precision || ')'
                     ELSE c.data_type
                   END,
                   CASE
                     WHEN c.data_type IN ('VARCHAR2', 'NVARCHAR2', 'CHAR', 'NCHAR') THEN c.char_length
                     WHEN c.data_type = 'RAW' THEN c.data_length
                     WHEN c.data_type IN ('CLOB', 'NCLOB', 'BLOB', 'LONG', 'LONG RAW', 'XMLTYPE') THEN -1
                   END,
                   CASE WHEN c.nullable = 'Y' THEN 1 ELSE 0 END,
                   CASE WHEN c.identity_column = 'YES' THEN 1 ELSE 0 END,
                   c.data_default
            FROM all_tab_columns c
            JOIN all_tables t ON t.owner = c.owner AND t.table_name = c.table_name
            WHERE c.owner = {OracleSchema} AND t.dropped = 'NO'
            ORDER BY c.table_name, c.column_id
            """,
        // The rowid alias (INTEGER PRIMARY KEY on its own) is SQLite's identity column and can never be NULL.
        DbEngine.Sqlite => $"""
            SELECT NULL, m.name, p.name,
                   lower(trim(CASE WHEN instr(p.type, '(') > 0 THEN substr(p.type, 1, instr(p.type, '(') - 1) ELSE p.type END)),
                   lower(p.type),
                   CASE WHEN instr(p.type, '(') > 0 AND (upper(p.type) LIKE '%CHAR%' OR upper(p.type) LIKE '%TEXT%' OR upper(p.type) LIKE '%CLOB%')
                        THEN CAST(substr(p.type, instr(p.type, '(') + 1) AS INTEGER) END,
                   CASE WHEN p."notnull" = 0 AND NOT (p.pk = 1 AND upper(p.type) = 'INTEGER'
                        AND (SELECT COUNT(*) FROM pragma_table_info(m.name) k WHERE k.pk > 0) = 1) THEN 1 ELSE 0 END,
                   CASE WHEN p.pk = 1 AND upper(p.type) = 'INTEGER'
                        AND (SELECT COUNT(*) FROM pragma_table_info(m.name) k WHERE k.pk > 0) = 1 THEN 1 ELSE 0 END,
                   p.dflt_value
            FROM sqlite_master m
            JOIN pragma_table_info(m.name) p
            WHERE {SqliteTables}
            ORDER BY m.name, p.cid
            """,
        _ => """
            SELECT NULL, c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.COLUMN_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
                   (c.IS_NULLABLE = 'YES'), (c.EXTRA LIKE '%auto_increment%'), c.COLUMN_DEFAULT
            FROM information_schema.COLUMNS c
            JOIN information_schema.TABLES t
              ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_TYPE = 'BASE TABLE'
            WHERE c.TABLE_SCHEMA = DATABASE()
            ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION
            """
    };

    // ---------------------------------------------------------------- indexes

    /// <summary>
    /// Columns: schema, table, index_name, index_type, is_unique, is_primary, column_name, is_included, ordinal, filter.
    /// One row per index column; rows of the same index are contiguous.
    /// </summary>
    public static string Indexes(DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => """
            SELECT s.name, t.name, i.name, i.type_desc, i.is_unique, i.is_primary_key, c.name,
                   ic.is_included_column, ic.index_column_id, i.filter_definition
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.type > 0 AND i.is_hypothetical = 0 AND t.is_ms_shipped = 0
            ORDER BY s.name, t.name, i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id
            """,
        DbEngine.PostgreSql => """
            SELECT n.nspname, t.relname, i.relname, am.amname, ix.indisunique, ix.indisprimary,
                   COALESCE(a.attname, '(expression)'), (k.ord > ix.indnkeyatts), k.ord::int,
                   pg_get_expr(ix.indpred, ix.indrelid)
            FROM pg_index ix
            JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_am am ON am.oid = i.relam
            CROSS JOIN LATERAL unnest(ix.indkey::int2[]) WITH ORDINALITY AS k(attnum, ord)
            LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum AND k.attnum > 0
            WHERE t.relkind IN ('r', 'p')
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname NOT LIKE 'pg\_toast%' AND n.nspname NOT LIKE 'pg\_temp%'
            ORDER BY n.nspname, t.relname, i.relname, k.ord
            """,
        DbEngine.Oracle => $"""
            SELECT NULL, i.table_name, i.index_name, i.index_type,
                   CASE WHEN i.uniqueness = 'UNIQUE' THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS (SELECT 1 FROM all_constraints k
                                     WHERE k.owner = i.table_owner AND k.table_name = i.table_name AND k.constraint_type = 'P'
                                       AND k.index_owner = i.owner AND k.index_name = i.index_name) THEN 1 ELSE 0 END,
                   CASE WHEN ic.column_name LIKE 'SYS\_NC%$' ESCAPE '\' THEN '(expression)' ELSE ic.column_name END,
                   0, ic.column_position, NULL
            FROM all_indexes i
            JOIN all_ind_columns ic ON ic.index_owner = i.owner AND ic.index_name = i.index_name
            WHERE i.table_owner = {OracleSchema} AND i.index_type <> 'LOB' AND i.table_name NOT LIKE 'BIN$%'
            ORDER BY i.table_name, i.index_name, ic.column_position
            """,
        // A rowid-alias primary key has no index of its own; it is reported as a pseudo index so keys are detected.
        DbEngine.Sqlite => $"""
            SELECT NULL, m.name, il.name,
                   CASE il.origin WHEN 'pk' THEN 'PRIMARY KEY' WHEN 'u' THEN 'UNIQUE CONSTRAINT' ELSE 'BTREE' END,
                   il."unique", CASE WHEN il.origin = 'pk' THEN 1 ELSE 0 END,
                   COALESCE(ii.name, '(expression)'), 0, ii.seqno,
                   CASE WHEN il.partial = 1 THEN (SELECT trim(substr(x.sql, instr(upper(x.sql), ' WHERE ') + 7))
                                                  FROM sqlite_master x WHERE x.type = 'index' AND x.name = il.name) END
            FROM sqlite_master m
            JOIN pragma_index_list(m.name) il
            JOIN pragma_index_info(il.name) ii
            WHERE {SqliteTables}
            UNION ALL
            SELECT NULL, m.name, 'PRIMARY KEY', 'ROWID', 1, 1, p.name, 0, p.pk - 1, NULL
            FROM sqlite_master m
            JOIN pragma_table_info(m.name) p
            WHERE {SqliteTables} AND p.pk > 0
              AND NOT EXISTS (SELECT 1 FROM pragma_index_list(m.name) x WHERE x.origin = 'pk')
            ORDER BY 2, 3, 9
            """,
        _ => """
            SELECT NULL, TABLE_NAME, INDEX_NAME, INDEX_TYPE, (NON_UNIQUE = 0), (INDEX_NAME = 'PRIMARY'),
                   COALESCE(COLUMN_NAME, '(expression)'), 0, SEQ_IN_INDEX, NULL
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
            ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX
            """
    };

    // ---------------------------------------------------------------- foreign keys

    /// <summary>
    /// Columns: name, from_schema, from_table, from_column, to_schema, to_table, to_column, on_delete, on_update, ordinal.
    /// One row per column pair, grouped by (from_schema, name).
    /// </summary>
    public static string ForeignKeys(DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => """
            SELECT fk.name, sp.name, tp.name, cp.name, sr.name, tr.name, cr.name,
                   fk.delete_referential_action_desc, fk.update_referential_action_desc, fkc.constraint_column_id
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
            JOIN sys.schemas sp ON sp.schema_id = tp.schema_id
            JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
            JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
            JOIN sys.schemas sr ON sr.schema_id = tr.schema_id
            JOIN sys.columns cr ON cr.object_id = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id
            ORDER BY sp.name, fk.name, fkc.constraint_column_id
            """,
        DbEngine.PostgreSql => """
            SELECT c.conname, n1.nspname, t1.relname, a1.attname, n2.nspname, t2.relname, a2.attname,
                   CASE c.confdeltype WHEN 'c' THEN 'CASCADE' WHEN 'n' THEN 'SET NULL' WHEN 'd' THEN 'SET DEFAULT'
                                      WHEN 'r' THEN 'RESTRICT' ELSE 'NO ACTION' END,
                   CASE c.confupdtype WHEN 'c' THEN 'CASCADE' WHEN 'n' THEN 'SET NULL' WHEN 'd' THEN 'SET DEFAULT'
                                      WHEN 'r' THEN 'RESTRICT' ELSE 'NO ACTION' END,
                   k.ord::int
            FROM pg_constraint c
            JOIN pg_class t1 ON t1.oid = c.conrelid
            JOIN pg_namespace n1 ON n1.oid = t1.relnamespace
            JOIN pg_class t2 ON t2.oid = c.confrelid
            JOIN pg_namespace n2 ON n2.oid = t2.relnamespace
            CROSS JOIN LATERAL unnest(c.conkey, c.confkey) WITH ORDINALITY AS k(from_att, to_att, ord)
            JOIN pg_attribute a1 ON a1.attrelid = c.conrelid AND a1.attnum = k.from_att
            JOIN pg_attribute a2 ON a2.attrelid = c.confrelid AND a2.attnum = k.to_att
            WHERE c.contype = 'f' AND n1.nspname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY n1.nspname, c.conname, k.ord
            """,
        DbEngine.Oracle => $"""
            SELECT c.constraint_name, NULL, c.table_name, cc.column_name,
                   CASE WHEN r.owner = c.owner THEN NULL ELSE r.owner END, r.table_name, rc.column_name,
                   c.delete_rule, 'NO ACTION', cc.position
            FROM all_constraints c
            JOIN all_cons_columns cc ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
            JOIN all_constraints r ON r.owner = c.r_owner AND r.constraint_name = c.r_constraint_name
            JOIN all_cons_columns rc ON rc.owner = r.owner AND rc.constraint_name = r.constraint_name AND rc.position = cc.position
            WHERE c.constraint_type = 'R' AND c.owner = {OracleSchema} AND c.table_name NOT LIKE 'BIN$%'
            ORDER BY c.table_name, c.constraint_name, cc.position
            """,
        // A reference without a column list points at the parent's primary key.
        DbEngine.Sqlite => $"""
            SELECT 'fk_' || m.name || '_' || fk.id, NULL, m.name, fk."from", NULL, fk."table",
                   COALESCE(fk."to", (SELECT p.name FROM pragma_table_info(fk."table") p WHERE p.pk = fk.seq + 1)),
                   fk.on_delete, fk.on_update, fk.seq
            FROM sqlite_master m
            JOIN pragma_foreign_key_list(m.name) fk
            WHERE {SqliteTables}
            ORDER BY m.name, fk.id, fk.seq
            """,
        _ => """
            SELECT k.CONSTRAINT_NAME, NULL, k.TABLE_NAME, k.COLUMN_NAME, NULL, k.REFERENCED_TABLE_NAME,
                   k.REFERENCED_COLUMN_NAME, rc.DELETE_RULE, rc.UPDATE_RULE, k.ORDINAL_POSITION
            FROM information_schema.KEY_COLUMN_USAGE k
            JOIN information_schema.REFERENTIAL_CONSTRAINTS rc
              ON rc.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA AND rc.CONSTRAINT_NAME = k.CONSTRAINT_NAME AND rc.TABLE_NAME = k.TABLE_NAME
            WHERE k.TABLE_SCHEMA = DATABASE() AND k.REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY k.TABLE_NAME, k.CONSTRAINT_NAME, k.ORDINAL_POSITION
            """
    };

    // ---------------------------------------------------------------- triggers

    /// <summary>Columns: name, schema, table, events, timing, is_disabled, definition.</summary>
    public static string Triggers(DbEngine engine, bool definitions, int maxDefinition) => engine switch
    {
        DbEngine.SqlServer => $"""
            SELECT tr.name, s.name, t.name,
                   STUFF((SELECT ', ' + te.type_desc FROM sys.trigger_events te WHERE te.object_id = tr.object_id
                          FOR XML PATH('')), 1, 2, ''),
                   CASE WHEN tr.is_instead_of_trigger = 1 THEN 'INSTEAD OF' ELSE 'AFTER' END,
                   tr.is_disabled,
                   {(definitions ? $"LEFT(OBJECT_DEFINITION(tr.object_id), {maxDefinition})" : "NULL")}
            FROM sys.triggers tr
            JOIN sys.tables t ON t.object_id = tr.parent_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE tr.is_ms_shipped = 0
            ORDER BY s.name, t.name, tr.name
            """,
        DbEngine.PostgreSql => $"""
            SELECT t.tgname, n.nspname, c.relname,
                   concat_ws(' OR ',
                     CASE WHEN (t.tgtype & 4) <> 0 THEN 'INSERT' END,
                     CASE WHEN (t.tgtype & 8) <> 0 THEN 'DELETE' END,
                     CASE WHEN (t.tgtype & 16) <> 0 THEN 'UPDATE' END,
                     CASE WHEN (t.tgtype & 32) <> 0 THEN 'TRUNCATE' END),
                   CASE WHEN (t.tgtype & 2) <> 0 THEN 'BEFORE' WHEN (t.tgtype & 64) <> 0 THEN 'INSTEAD OF' ELSE 'AFTER' END,
                   (t.tgenabled = 'D'),
                   {(definitions ? $"LEFT(pg_get_triggerdef(t.oid), {maxDefinition})" : "NULL")}
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE NOT t.tgisinternal AND n.nspname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY n.nspname, c.relname, t.tgname
            """,
        // trigger_body is a LONG column: it cannot be shortened in SQL, the reader truncates it.
        DbEngine.Oracle => $"""
            SELECT t.trigger_name, NULL, t.table_name, t.triggering_event, t.trigger_type,
                   CASE WHEN t.status = 'DISABLED' THEN 1 ELSE 0 END,
                   {(definitions ? "t.trigger_body" : "NULL")}
            FROM all_triggers t
            WHERE t.table_owner = {OracleSchema} AND t.base_object_type = 'TABLE' AND t.table_name NOT LIKE 'BIN$%'
            ORDER BY t.table_name, t.trigger_name
            """,
        DbEngine.Sqlite => $"""
            SELECT m.name, NULL, m.tbl_name,
                   CASE WHEN instr(h.head, ' INSERT') > 0 THEN 'INSERT' WHEN instr(h.head, ' DELETE') > 0 THEN 'DELETE' ELSE 'UPDATE' END,
                   CASE WHEN instr(h.head, ' INSTEAD OF') > 0 THEN 'INSTEAD OF' WHEN instr(h.head, ' BEFORE') > 0 THEN 'BEFORE' ELSE 'AFTER' END,
                   0,
                   {(definitions ? $"substr(m.sql, 1, {maxDefinition})" : "NULL")}
            FROM sqlite_master m
            JOIN (SELECT name AS n, upper(substr(sql, 1, instr(upper(sql), ' ON '))) AS head FROM sqlite_master WHERE type = 'trigger') h ON h.n = m.name
            WHERE m.type = 'trigger'
            ORDER BY m.tbl_name, m.name
            """,
        _ => $"""
            SELECT TRIGGER_NAME, NULL, EVENT_OBJECT_TABLE, EVENT_MANIPULATION, ACTION_TIMING, 0,
                   {(definitions ? $"LEFT(ACTION_STATEMENT, {maxDefinition})" : "NULL")}
            FROM information_schema.TRIGGERS
            WHERE TRIGGER_SCHEMA = DATABASE()
            ORDER BY EVENT_OBJECT_TABLE, TRIGGER_NAME
            """
    };

    // ---------------------------------------------------------------- views

    /// <summary>Columns: schema, name, definition.</summary>
    public static string Views(DbEngine engine, bool definitions, int maxDefinition) => engine switch
    {
        DbEngine.SqlServer => $"""
            SELECT s.name, v.name, {(definitions ? $"LEFT(OBJECT_DEFINITION(v.object_id), {maxDefinition})" : "NULL")}
            FROM sys.views v
            JOIN sys.schemas s ON s.schema_id = v.schema_id
            WHERE v.is_ms_shipped = 0
            ORDER BY s.name, v.name
            """,
        DbEngine.PostgreSql => $"""
            SELECT schemaname, viewname, {(definitions ? $"LEFT(definition, {maxDefinition})" : "NULL")}
            FROM pg_views
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            UNION ALL
            SELECT schemaname, matviewname, {(definitions ? $"LEFT(definition, {maxDefinition})" : "NULL")}
            FROM pg_matviews
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY 1, 2
            """,
        DbEngine.Oracle => $"""
            SELECT NULL, v.view_name, {(definitions ? $"SUBSTR(v.text_vc, 1, {Math.Min(maxDefinition, 4000)})" : "NULL")}
            FROM all_views v
            WHERE v.owner = {OracleSchema}
            ORDER BY v.view_name
            """,
        DbEngine.Sqlite => $"""
            SELECT NULL, m.name, {(definitions ? $"substr(m.sql, 1, {maxDefinition})" : "NULL")}
            FROM sqlite_master m
            WHERE m.type = 'view'
            ORDER BY m.name
            """,
        _ => $"""
            SELECT NULL, TABLE_NAME, {(definitions ? $"LEFT(VIEW_DEFINITION, {maxDefinition})" : "NULL")}
            FROM information_schema.VIEWS
            WHERE TABLE_SCHEMA = DATABASE()
            ORDER BY TABLE_NAME
            """
    };

    // ---------------------------------------------------------------- routines

    /// <summary>Columns: schema, name, kind, parameters, definition.</summary>
    public static string Routines(DbEngine engine, bool definitions, int maxDefinition) => engine switch
    {
        DbEngine.SqlServer => $"""
            SELECT s.name, o.name,
                   CASE WHEN o.type = 'P' THEN 'PROCEDURE' ELSE 'FUNCTION' END,
                   STUFF((SELECT ', ' + p.name + ' ' + ty.name + CASE WHEN p.is_output = 1 THEN ' OUT' ELSE '' END
                          FROM sys.parameters p
                          JOIN sys.types ty ON ty.user_type_id = p.user_type_id
                          WHERE p.object_id = o.object_id AND p.parameter_id > 0
                          ORDER BY p.parameter_id
                          FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, ''),
                   {(definitions ? $"LEFT(OBJECT_DEFINITION(o.object_id), {maxDefinition})" : "NULL")}
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('P', 'FN', 'IF', 'TF') AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name
            """,
        DbEngine.PostgreSql => $"""
            SELECT n.nspname, p.proname,
                   CASE p.prokind WHEN 'p' THEN 'PROCEDURE' ELSE 'FUNCTION' END,
                   pg_get_function_arguments(p.oid),
                   {(definitions ? $"LEFT(pg_get_functiondef(p.oid), {maxDefinition})" : "NULL")}
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND p.prokind IN ('f', 'p')
              AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
            ORDER BY n.nspname, p.proname
            """,
        // Package bodies hold the actual PL/SQL code; source lines are concatenated up to the VARCHAR2 limit.
        DbEngine.Oracle => $"""
            SELECT NULL, o.object_name, o.object_type,
                   (SELECT LISTAGG(a.argument_name || ' ' || a.in_out || ' ' || a.data_type, ', ' ON OVERFLOW TRUNCATE '…' WITHOUT COUNT)
                           WITHIN GROUP (ORDER BY a.position)
                    FROM all_arguments a
                    WHERE a.owner = o.owner AND a.object_name = o.object_name AND a.package_name IS NULL
                      AND a.argument_name IS NOT NULL AND a.data_level = 0),
                   {(definitions ? OracleSource : "NULL")}
            FROM all_objects o
            WHERE o.owner = {OracleSchema} AND o.object_type IN ('PROCEDURE', 'FUNCTION', 'PACKAGE BODY')
            ORDER BY o.object_type, o.object_name
            """,
        DbEngine.Sqlite => "SELECT NULL, NULL, NULL, NULL, NULL WHERE 0",
        _ => $"""
            SELECT NULL, r.ROUTINE_NAME, r.ROUTINE_TYPE,
                   (SELECT GROUP_CONCAT(CONCAT_WS(' ', p.PARAMETER_MODE, p.PARAMETER_NAME, p.DATA_TYPE)
                                        ORDER BY p.ORDINAL_POSITION SEPARATOR ', ')
                    FROM information_schema.PARAMETERS p
                    WHERE p.SPECIFIC_SCHEMA = r.ROUTINE_SCHEMA AND p.SPECIFIC_NAME = r.SPECIFIC_NAME AND p.ORDINAL_POSITION > 0),
                   {(definitions ? $"LEFT(r.ROUTINE_DEFINITION, {maxDefinition})" : "NULL")}
            FROM information_schema.ROUTINES r
            WHERE r.ROUTINE_SCHEMA = DATABASE()
            ORDER BY r.ROUTINE_NAME
            """
    };

    // ---------------------------------------------------------------- constraints

    /// <summary>Columns: schema, table, name, type, definition. CHECK constraints only (unique/PK/FK are covered elsewhere).</summary>
    public static string CheckConstraints(DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => """
            SELECT s.name, t.name, cc.name, 'CHECK', cc.definition
            FROM sys.check_constraints cc
            JOIN sys.tables t ON t.object_id = cc.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, cc.name
            """,
        DbEngine.PostgreSql => """
            SELECT n.nspname, c.relname, con.conname, 'CHECK', pg_get_constraintdef(con.oid)
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE con.contype = 'c' AND n.nspname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY n.nspname, c.relname, con.conname
            """,
        // NOT NULL is stored as a generated CHECK constraint in Oracle; those are already part of the column list.
        DbEngine.Oracle => $"""
            SELECT NULL, c.table_name, c.constraint_name, 'CHECK', c.search_condition_vc
            FROM all_constraints c
            WHERE c.owner = {OracleSchema} AND c.constraint_type = 'C' AND c.table_name NOT LIKE 'BIN$%'
              AND NOT (c.generated = 'GENERATED NAME' AND c.search_condition_vc LIKE '"%" IS NOT NULL')
            ORDER BY c.table_name, c.constraint_name
            """,
        // SQLite keeps CHECK constraints only inside CREATE TABLE; they are parsed from the table SQL instead.
        DbEngine.Sqlite => "SELECT NULL, NULL, NULL, NULL, NULL WHERE 0",
        _ => """
            SELECT NULL, tc.TABLE_NAME, tc.CONSTRAINT_NAME, 'CHECK', cc.CHECK_CLAUSE
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.CHECK_CONSTRAINTS cc
              ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
            WHERE tc.TABLE_SCHEMA = DATABASE() AND tc.CONSTRAINT_TYPE = 'CHECK'
            ORDER BY tc.TABLE_NAME, tc.CONSTRAINT_NAME
            """
    };

    // ---------------------------------------------------------------- sample data

    /// <summary>
    /// Builds a sample query. Random ordering forces a full scan, so it is only used for small tables;
    /// large tables return their first rows, which is cheap on every engine.
    /// </summary>
    public static string SampleQuery(DbEngine engine, TableInfo table, IReadOnlyList<string> columns, int rows, bool random)
    {
        string cols = string.Join(", ", columns.Select(c => Quote(engine, c)));
        string from = QuoteTable(engine, table);
        return engine switch
        {
            DbEngine.SqlServer => $"SELECT TOP ({rows}) {cols} FROM {from}" + (random ? " ORDER BY NEWID()" : ""),
            DbEngine.PostgreSql or DbEngine.Sqlite => $"SELECT {cols} FROM {from}" + (random ? " ORDER BY random()" : "") + $" LIMIT {rows}",
            DbEngine.Oracle => $"SELECT {cols} FROM {from}" + (random ? " ORDER BY DBMS_RANDOM.VALUE" : "") + $" FETCH FIRST {rows} ROWS ONLY",
            _ => $"SELECT {cols} FROM {from}" + (random ? " ORDER BY RAND()" : "") + $" LIMIT {rows}"
        };
    }

    // ---------------------------------------------------------------- SQLite CHECK constraints

    /// <summary>Extracts the CHECK constraints from a SQLite CREATE TABLE statement (name, expression).</summary>
    public static IReadOnlyList<(string Name, string Expression)> ParseSqliteChecks(string createSql)
    {
        var result = new List<(string, string)>();
        int i = 0;
        while (i < createSql.Length)
        {
            int at = IndexOfKeyword(createSql, "CHECK", i);
            if (at < 0) break;

            int open = createSql.IndexOf('(', at + 5);
            if (open < 0 || createSql[(at + 5)..open].Trim().Length > 0) { i = at + 5; continue; }

            int depth = 0, end = -1;
            char quote = '\0';
            for (int j = open; j < createSql.Length; j++)
            {
                char c = createSql[j];
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c is '\'' or '"' or '`') { quote = c; continue; }
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) { end = j; break; }
            }
            if (end < 0) break;

            // "CONSTRAINT name CHECK (…)" gives the constraint a name.
            string before = createSql[..at].TrimEnd();
            var named = System.Text.RegularExpressions.Regex.Match(before, @"CONSTRAINT\s+[""`\[]?([^\s""`\]]+)[""`\]]?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string name = named.Success ? named.Groups[1].Value : $"check_{result.Count + 1}";
            result.Add((name, createSql[(open + 1)..end].Trim()));
            i = end + 1;
        }
        return result;
    }

    private static int IndexOfKeyword(string sql, string keyword, int start)
    {
        for (int i = sql.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase); i >= 0; i = sql.IndexOf(keyword, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            bool left = i == 0 || !char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_';
            int after = i + keyword.Length;
            bool right = after >= sql.Length || !char.IsLetterOrDigit(sql[after]) && sql[after] != '_';
            if (left && right) return i;
        }
        return -1;
    }
}
