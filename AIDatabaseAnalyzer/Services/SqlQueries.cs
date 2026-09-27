namespace AIDatabaseAnalyzer.Services;

public static class SqlQueries
{
    public static string TableList(DbEngine engine) => engine switch
    {
        DbEngine.SqlServer => "SELECT s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id ORDER BY s.name, t.name",
        DbEngine.PostgreSql => "SELECT schemaname, tablename FROM pg_catalog.pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema') ORDER BY schemaname, tablename",
        DbEngine.MySql => "SELECT TABLE_SCHEMA, TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME",
        _ => throw new ArgumentOutOfRangeException(nameof(engine))
    };

    // Result sets keep object schema/name separate; display keys never enter provider SQL.
    public const string SqlServerSchema = @"
        SELECT s.name, t.name, c.name AS ColumnName, ty.name AS DataType,
            c.max_length AS MaxLength,
            c.is_nullable AS IsNullable,
            CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.indexes pk JOIN sys.index_columns pic ON pic.object_id = pk.object_id AND pic.index_id = pk.index_id WHERE pk.object_id = t.object_id AND pk.is_primary_key = 1 AND pic.column_id = c.column_id AND pic.key_ordinal > 0) THEN 1 ELSE 0 END AS bit) AS IsPrimaryKey
        FROM sys.tables t INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        INNER JOIN sys.columns c ON t.object_id = c.object_id
        INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id ORDER BY s.name, t.name, c.column_id;
        SELECT fk.name, sp.name, tp.name AS ParentTable, cp.name,
            sr.name, tr.name AS RefTable, cr.name, fkc.constraint_column_id
        FROM sys.foreign_keys fk
        INNER JOIN sys.tables tp ON fk.parent_object_id = tp.object_id INNER JOIN sys.tables tr ON fk.referenced_object_id = tr.object_id
        INNER JOIN sys.schemas sp ON sp.schema_id = tp.schema_id INNER JOIN sys.schemas sr ON sr.schema_id = tr.schema_id
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns cp ON fkc.parent_column_id = cp.column_id AND fkc.parent_object_id = cp.object_id
        INNER JOIN sys.columns cr ON fkc.referenced_column_id = cr.column_id AND fkc.referenced_object_id = cr.object_id;
        SELECT s.name, t.name, i.name, i.type_desc, i.is_unique, c.name,
            CASE WHEN ic.is_included_column = 1 THEN ic.index_column_id ELSE ic.key_ordinal END,
            ic.is_included_column, i.filter_definition, CAST(NULL AS nvarchar(max))
        FROM sys.indexes i INNER JOIN sys.tables t ON i.object_id = t.object_id
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id WHERE i.type > 0
            ORDER BY s.name, t.name, i.index_id, ic.index_column_id;
        SELECT tr.name, s.name, s.name, t.name, tr.is_disabled FROM sys.triggers tr INNER JOIN sys.tables t ON tr.parent_id = t.object_id INNER JOIN sys.schemas s ON s.schema_id = t.schema_id;
        SELECT s.name, v.name FROM sys.views v INNER JOIN sys.schemas s ON s.schema_id = v.schema_id;
        SELECT s.name, p.name AS SpName, param.name AS ParamName, ty.name AS DataType
        FROM sys.procedures p LEFT JOIN sys.parameters param ON p.object_id = param.object_id
        INNER JOIN sys.schemas s ON s.schema_id = p.schema_id
        LEFT JOIN sys.types ty ON param.user_type_id = ty.user_type_id WHERE p.is_ms_shipped = 0;
        SELECT s.name, t.name, c.name, 'CHECK', c.definition FROM sys.check_constraints c INNER JOIN sys.tables t ON c.parent_object_id = t.object_id INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        UNION ALL
        SELECT s.name, t.name, d.name, 'DEFAULT', d.definition FROM sys.default_constraints d INNER JOIN sys.tables t ON d.parent_object_id = t.object_id INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        UNION ALL
        SELECT s.name, t.name, k.name, k.type_desc, CAST(NULL AS nvarchar(max)) FROM sys.key_constraints k INNER JOIN sys.tables t ON k.parent_object_id = t.object_id INNER JOIN sys.schemas s ON s.schema_id = t.schema_id;";

    public const string PostgreSqlSchema = @"
        SELECT c.table_schema, c.table_name, c.column_name, c.data_type, c.character_maximum_length,
            c.is_nullable = 'YES', EXISTS (SELECT 1 FROM information_schema.table_constraints tc JOIN information_schema.key_column_usage kcu ON kcu.constraint_schema = tc.constraint_schema AND kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema AND kcu.table_name = tc.table_name WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = c.table_schema AND tc.table_name = c.table_name AND kcu.column_name = c.column_name)
            FROM information_schema.columns c WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY c.table_schema, c.table_name, c.ordinal_position;
        SELECT c.conname, pn.nspname, pt.relname, pa.attname, rn.nspname, rt.relname, ra.attname, ck.ord
            FROM pg_constraint c JOIN pg_class pt ON pt.oid = c.conrelid JOIN pg_namespace pn ON pn.oid = pt.relnamespace
            JOIN pg_class rt ON rt.oid = c.confrelid JOIN pg_namespace rn ON rn.oid = rt.relnamespace
            JOIN LATERAL unnest(c.conkey) WITH ORDINALITY AS ck(attnum, ord) ON true
            JOIN LATERAL unnest(c.confkey) WITH ORDINALITY AS rk(attnum, ord) ON rk.ord = ck.ord
            JOIN pg_attribute pa ON pa.attrelid = pt.oid AND pa.attnum = ck.attnum
            JOIN pg_attribute ra ON ra.attrelid = rt.oid AND ra.attnum = rk.attnum WHERE c.contype = 'f';
        SELECT n.nspname, t.relname, idx.relname, am.amname, ix.indisunique, a.attname,
            k.ord, k.ord > ix.indnkeyatts, pg_get_expr(ix.indpred, ix.indrelid), pg_get_indexdef(ix.indexrelid)
            FROM pg_index ix JOIN pg_class t ON t.oid = ix.indrelid JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_class idx ON idx.oid = ix.indexrelid JOIN pg_am am ON am.oid = idx.relam
            LEFT JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
            LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema');
        SELECT trigger_name, trigger_schema, event_object_schema, event_object_table, NULL FROM information_schema.triggers WHERE trigger_schema NOT IN ('pg_catalog', 'information_schema');
        SELECT table_schema, table_name FROM information_schema.views WHERE table_schema NOT IN ('pg_catalog', 'information_schema');
        SELECT r.routine_schema, r.routine_name, p.parameter_name, p.data_type FROM information_schema.routines r LEFT JOIN information_schema.parameters p ON r.specific_name = p.specific_name AND r.specific_schema = p.specific_schema WHERE r.routine_schema NOT IN ('pg_catalog', 'information_schema');
        SELECT n.nspname, t.relname, c.conname,
            CASE c.contype WHEN 'c' THEN 'CHECK' WHEN 'u' THEN 'UNIQUE' ELSE 'PRIMARY KEY' END,
            pg_get_constraintdef(c.oid)
            FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND c.contype IN ('c', 'u', 'p');";

    public const string MySqlSchema = @"
        SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE = 'YES', COLUMN_KEY = 'PRI' FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME, ORDINAL_POSITION;
        SELECT CONSTRAINT_NAME, TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_SCHEMA, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME, ORDINAL_POSITION FROM information_schema.KEY_COLUMN_USAGE WHERE REFERENCED_TABLE_NAME IS NOT NULL AND TABLE_SCHEMA = DATABASE();
        SELECT TABLE_SCHEMA, TABLE_NAME, INDEX_NAME, INDEX_TYPE, IF(NON_UNIQUE=0, 1, 0), COLUMN_NAME, SEQ_IN_INDEX, 0, NULL, NULL FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;
        SELECT TRIGGER_NAME, TRIGGER_SCHEMA, EVENT_OBJECT_SCHEMA, EVENT_OBJECT_TABLE, NULL FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = DATABASE();
        SELECT TABLE_SCHEMA, TABLE_NAME FROM information_schema.VIEWS WHERE TABLE_SCHEMA = DATABASE();
        SELECT r.ROUTINE_SCHEMA, r.ROUTINE_NAME, p.PARAMETER_NAME, p.DATA_TYPE FROM information_schema.ROUTINES r LEFT JOIN information_schema.PARAMETERS p ON r.SPECIFIC_NAME = p.SPECIFIC_NAME AND r.ROUTINE_SCHEMA = p.SPECIFIC_SCHEMA WHERE r.ROUTINE_SCHEMA = DATABASE();
        SELECT tc.TABLE_SCHEMA, tc.TABLE_NAME, tc.CONSTRAINT_NAME, tc.CONSTRAINT_TYPE, cc.CHECK_CLAUSE
            FROM information_schema.TABLE_CONSTRAINTS tc LEFT JOIN information_schema.CHECK_CONSTRAINTS cc
                ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
            WHERE tc.TABLE_SCHEMA = DATABASE() AND tc.CONSTRAINT_TYPE IN ('CHECK', 'UNIQUE', 'PRIMARY KEY');";
}
