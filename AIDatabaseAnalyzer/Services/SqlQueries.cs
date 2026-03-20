namespace AIDatabaseAnalyzer.Services;

public static class SqlQueries
{
    public const string SqlServerSchema = @"
        SELECT t.name AS TableName, c.name AS ColumnName, ty.name AS DataType, c.max_length AS MaxLength
        FROM sys.tables t INNER JOIN sys.columns c ON t.object_id = c.object_id
        INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id ORDER BY t.name, c.column_id;
        SELECT fk.name, tp.name AS ParentTable, cp.name, tr.name AS RefTable, cr.name
        FROM sys.foreign_keys fk
        INNER JOIN sys.tables tp ON fk.parent_object_id = tp.object_id INNER JOIN sys.tables tr ON fk.referenced_object_id = tr.object_id
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns cp ON fkc.parent_column_id = cp.column_id AND fkc.parent_object_id = cp.object_id
        INNER JOIN sys.columns cr ON fkc.referenced_column_id = cr.column_id AND fkc.referenced_object_id = cr.object_id;
        SELECT t.name, i.name, i.type_desc, i.is_unique, c.name
        FROM sys.indexes i INNER JOIN sys.tables t ON i.object_id = t.object_id
        INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id WHERE i.type > 0;
        SELECT tr.name, t.name, tr.is_disabled FROM sys.triggers tr INNER JOIN sys.tables t ON tr.parent_id = t.object_id;
        SELECT name FROM sys.views;
        SELECT p.name AS SpName, param.name AS ParamName, ty.name AS DataType
        FROM sys.procedures p LEFT JOIN sys.parameters param ON p.object_id = param.object_id
        LEFT JOIN sys.types ty ON param.user_type_id = ty.user_type_id WHERE p.is_ms_shipped = 0;
        SELECT t.name, c.name, 'CHECK', c.definition FROM sys.check_constraints c INNER JOIN sys.tables t ON c.parent_object_id = t.object_id
        UNION ALL
        SELECT t.name, d.name, 'DEFAULT', d.definition FROM sys.default_constraints d INNER JOIN sys.tables t ON d.parent_object_id = t.object_id;";

    public const string PostgreSqlSchema = @"
        SELECT table_name, column_name, data_type, character_maximum_length FROM information_schema.columns WHERE table_schema = 'public' ORDER BY table_name, ordinal_position;
        SELECT tc.constraint_name, tc.table_name, kcu.column_name, ccu.table_name AS foreign_table_name, ccu.column_name AS foreign_column_name FROM information_schema.table_constraints AS tc JOIN information_schema.key_column_usage AS kcu ON tc.constraint_name = kcu.constraint_name JOIN information_schema.constraint_column_usage AS ccu ON ccu.constraint_name = tc.constraint_name WHERE tc.constraint_type = 'FOREIGN KEY';
        SELECT tablename, indexname, 'INDEX', false, '' FROM pg_indexes WHERE schemaname = 'public';
        SELECT trigger_name, event_object_table, false FROM information_schema.triggers WHERE trigger_schema = 'public';
        SELECT table_name FROM information_schema.views WHERE table_schema = 'public';
        SELECT r.routine_name, p.parameter_name, p.data_type FROM information_schema.routines r LEFT JOIN information_schema.parameters p ON r.specific_name = p.specific_name WHERE r.routine_schema = 'public';
        SELECT tc.table_name, tc.constraint_name, tc.constraint_type, '' FROM information_schema.table_constraints tc WHERE tc.table_schema = 'public' AND tc.constraint_type IN ('CHECK', 'UNIQUE');";

    public const string MySqlSchema = @"
        SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME, ORDINAL_POSITION;
        SELECT CONSTRAINT_NAME, TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE REFERENCED_TABLE_NAME IS NOT NULL AND TABLE_SCHEMA = DATABASE();
        SELECT TABLE_NAME, INDEX_NAME, INDEX_TYPE, IF(NON_UNIQUE=0, 1, 0), COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE();
        SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE, 0 FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = DATABASE();
        SELECT TABLE_NAME FROM information_schema.VIEWS WHERE TABLE_SCHEMA = DATABASE();
        SELECT r.ROUTINE_NAME, p.PARAMETER_NAME, p.DATA_TYPE FROM information_schema.ROUTINES r LEFT JOIN information_schema.PARAMETERS p ON r.SPECIFIC_NAME = p.SPECIFIC_NAME WHERE r.ROUTINE_SCHEMA = DATABASE();
        SELECT TABLE_NAME, CONSTRAINT_NAME, CONSTRAINT_TYPE, '' FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = DATABASE() AND CONSTRAINT_TYPE IN ('CHECK', 'UNIQUE');";
}