using System.Text;
using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Insights;

/// <summary>
/// Offline, rule based schema review. Runs instantly after extraction (no AI, no network) and produces
/// a health score plus concrete findings. The AI analysis builds on these results.
/// </summary>
public static class SchemaInsightsService
{
    private const int MaxListedObjects = 25;

    private static readonly Dictionary<FindingSeverity, double> Weights = new()
    {
        [FindingSeverity.Critical] = 14,
        [FindingSeverity.High] = 8,
        [FindingSeverity.Medium] = 4,
        [FindingSeverity.Low] = 1.5,
        [FindingSeverity.Info] = 0.2
    };

    public static SchemaInsights Analyze(DatabaseSchema schema)
    {
        var ctx = new Context(schema);
        var skipped = new List<string>();

        NoPrimaryKey(ctx);
        ForeignKeyTypeMismatch(ctx);
        PlaintextCredentials(ctx);
        SensitiveColumns(ctx);
        OrphanCandidates(ctx);
        CircularDependencies(ctx);
        TinyIdentityKeys(ctx);
        WideTables(ctx);
        DeprecatedTypes(ctx);
        MoneyAsFloat(ctx);
        DatesStoredAsText(ctx);
        FlagsStoredAsWideTypes(ctx);
        ExcessiveNullability(ctx);
        ConflictingColumnTypes(ctx);
        NamingConsistency(ctx);
        MissingAuditColumns(ctx);
        DisabledTriggers(ctx);
        IsolatedTables(ctx);
        EmptyTables(ctx);

        if (schema.IndexesIncluded)
        {
            ForeignKeysWithoutIndex(ctx);
            DuplicateIndexes(ctx);
            LargeTablesWithoutIndexes(ctx);
            TooManyIndexes(ctx);
            RandomGuidClusteredKeys(ctx);
        }
        else
        {
            skipped.Add("Index checks were skipped because indexes were not extracted.");
        }

        double penalty = ctx.Entries.Sum(e => Weights[e.Finding.Severity] * (0.35 + 0.65 * e.Ratio));
        int score = (int)Math.Clamp(Math.Round(100 - penalty), 0, 100);

        return new SchemaInsights
        {
            Score = score,
            Grade = score >= 90 ? "A" : score >= 80 ? "B" : score >= 70 ? "C" : score >= 55 ? "D" : "F",
            Findings = ctx.Entries
                .Select(e => e.Finding)
                .OrderByDescending(f => f.Severity)
                .ThenByDescending(f => f.Count)
                .ToList(),
            SkippedChecks = skipped
        };
    }

    // ================================================================== rules

    private static void NoPrimaryKey(Context c)
    {
        var tables = c.Schema.Tables.Where(t => !t.HasPrimaryKey).Select(t => t.FullName).ToList();
        if (tables.Count == 0) return;
        c.Add("NO_PK", FindingSeverity.High, FindingCategory.Design,
            "Tables without a primary key",
            "Without a primary key rows cannot be addressed reliably, replication and ORMs struggle, and the table is stored as an unordered heap.",
            "Add a (preferably narrow, immutable, ever-increasing) primary key to every table.",
            tables, c.Schema.Tables.Count);
    }

    private static void ForeignKeyTypeMismatch(Context c)
    {
        var typeMismatch = new List<string>();
        var lengthMismatch = new List<string>();

        foreach (var fk in c.Schema.ForeignKeys)
        {
            var from = c.Schema.FindTable(fk.FromTable);
            var to = c.Schema.FindTable(fk.ToTable);
            if (from is null || to is null) continue;

            for (int i = 0; i < fk.FromColumns.Count && i < fk.ToColumns.Count; i++)
            {
                var a = from.Columns.FirstOrDefault(x => string.Equals(x.Name, fk.FromColumns[i], StringComparison.OrdinalIgnoreCase));
                var b = to.Columns.FirstOrDefault(x => string.Equals(x.Name, fk.ToColumns[i], StringComparison.OrdinalIgnoreCase));
                if (a is null || b is null) continue;

                string label = $"{fk.FromTable}.{a.Name} ({a.DataType}) → {fk.ToTable}.{b.Name} ({b.DataType})";
                if (TypeFamily(a.BaseType) != TypeFamily(b.BaseType)) typeMismatch.Add(label);
                else if (TypeFamily(a.BaseType) == "string" && a.MaxLength is > 0 && b.MaxLength is > 0 && a.MaxLength != b.MaxLength)
                    lengthMismatch.Add(label);
            }
        }

        if (typeMismatch.Count > 0)
            c.Add("FK_TYPE_MISMATCH", FindingSeverity.High, FindingCategory.Performance,
                "Foreign keys with mismatching data types",
                "Joining columns of different types forces implicit conversions that can disable index seeks and cause subtle bugs.",
                "Align the data type of the referencing column with the referenced key.",
                typeMismatch, c.Schema.ForeignKeys.Count);

        if (lengthMismatch.Count > 0)
            c.Add("FK_LENGTH_MISMATCH", FindingSeverity.Low, FindingCategory.Integrity,
                "Foreign keys with different text lengths",
                "Referencing and referenced text columns have different maximum lengths.",
                "Use the same length on both sides of the relationship.",
                lengthMismatch, c.Schema.ForeignKeys.Count);
    }

    private static void PlaintextCredentials(Context c)
    {
        string[] safe = { "hash", "salt", "hashed", "encrypted", "policy", "reset", "changed", "expire", "expiry", "date", "count", "attempt", "required", "history", "lastchange", "failed" };

        var passwords = new List<string>();
        var secrets = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            foreach (var col in t.Columns)
            {
                string key = Key(col.Name);
                if (safe.Any(s => key.Contains(s, StringComparison.Ordinal))) continue;
                if (key.Contains("password") || key.Contains("passwd") || key == "pwd" || key.EndsWith("pwd", StringComparison.Ordinal))
                    passwords.Add($"{t.FullName}.{col.Name} ({col.DataType})");
                else if (key.Contains("apikey") || key.Contains("secret") || key.Contains("privatekey") || key == "token" || key.EndsWith("accesstoken", StringComparison.Ordinal))
                    secrets.Add($"{t.FullName}.{col.Name} ({col.DataType})");
            }
        }

        if (passwords.Count > 0)
            c.Add("PLAINTEXT_PASSWORD", FindingSeverity.High, FindingCategory.Security,
                "Columns that may hold plain-text passwords",
                "Columns named like passwords do not indicate that they store a salted hash. Plain-text credentials are a critical breach risk.",
                "Store only salted, slow hashes (Argon2id, bcrypt or PBKDF2) and name the column accordingly (e.g. password_hash).",
                passwords, c.Schema.ColumnCount);

        if (secrets.Count > 0)
            c.Add("SECRETS_AT_REST", FindingSeverity.Medium, FindingCategory.Security,
                "Secrets or tokens stored in the database",
                "API keys, tokens and secrets stored as plain text are exposed to anyone who can read the table or its backups.",
                "Encrypt these values (column encryption / KMS) or keep only hashes of tokens.",
                secrets, c.Schema.ColumnCount);
    }

    private static void SensitiveColumns(Context c)
    {
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            foreach (var col in t.Columns)
            {
                string? label = SensitiveLabel(Key(col.Name));
                if (label is not null) hits.Add($"{t.FullName}.{col.Name} ({label})");
            }
        }

        if (hits.Count == 0) return;
        c.Add("PII_COLUMNS", FindingSeverity.Medium, FindingCategory.Security,
            "Columns that look like personal or sensitive data",
            "These column names suggest personal data (PII), financial data or identifiers that are covered by GDPR/KVKK/HIPAA style regulations.",
            "Classify the data, restrict access (RBAC), encrypt or mask it, and define retention periods. Review whether each column is really needed.",
            hits, c.Schema.ColumnCount);
    }

    private static void OrphanCandidates(Context c)
    {
        var tablesByKey = new Dictionary<string, TableInfo>();
        foreach (var t in c.Schema.Tables) tablesByKey.TryAdd(Key(t.Name), t);

        var existingFks = c.Schema.ForeignKeys
            .SelectMany(f => f.FromColumns.Select(col => (f.FromTable.ToLowerInvariant(), col.ToLowerInvariant())))
            .ToHashSet();

        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            foreach (var col in t.Columns)
            {
                string key = Key(col.Name);
                string? stem = key.EndsWith("id", StringComparison.Ordinal) ? key[..^2]
                    : key.EndsWith("fk", StringComparison.Ordinal) ? key[..^2]
                    : null;
                if (string.IsNullOrEmpty(stem) || stem.Length < 3) continue;
                if (existingFks.Contains((t.FullName.ToLowerInvariant(), col.Name.ToLowerInvariant()))) continue;
                if (TypeFamily(col.BaseType) is not ("int" or "bigint" or "smallint" or "guid" or "string")) continue;

                TableInfo? target = null;
                foreach (var candidate in new[] { stem, stem + "s", stem + "es", stem.EndsWith('y') ? stem[..^1] + "ies" : stem })
                {
                    if (tablesByKey.TryGetValue(candidate, out target)) break;
                }
                if (target is null || ReferenceEquals(target, t)) continue;
                hits.Add($"{t.FullName}.{col.Name} → {target.FullName}");
            }
        }

        if (hits.Count == 0) return;
        c.Add("MISSING_FK", FindingSeverity.Medium, FindingCategory.Integrity,
            "Probable relationships without a foreign key",
            "These columns are named like a reference to another table but have no FOREIGN KEY constraint, so orphan rows are possible.",
            "Verify the relationship and add a foreign key (clean up existing orphan rows first).",
            hits, c.Schema.ColumnCount);
    }

    private static void CircularDependencies(Context c)
    {
        var graph = c.Schema.Tables.ToDictionary(t => t.FullName, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var fk in c.Schema.ForeignKeys)
        {
            if (string.Equals(fk.FromTable, fk.ToTable, StringComparison.OrdinalIgnoreCase)) continue;
            if (graph.TryGetValue(fk.FromTable, out var list) && graph.ContainsKey(fk.ToTable)) list.Add(fk.ToTable);
        }

        var components = StronglyConnectedComponents(graph).Where(comp => comp.Count > 1).ToList();
        if (components.Count == 0) return;

        c.Add("CIRCULAR_FK", FindingSeverity.Medium, FindingCategory.Design,
            "Circular foreign key dependencies",
            "Tables that reference each other in a cycle make inserts, deletes, bulk loads and migrations order-dependent.",
            "Break the cycle (make one side nullable, introduce a link table, or move the reference to the application layer).",
            components.Select(comp => string.Join(" ↔ ", comp.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))).ToList(),
            c.Schema.Tables.Count);
    }

    private static void TinyIdentityKeys(Context c)
    {
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            foreach (var col in t.PrimaryKeyColumns.Where(p => p.IsIdentity))
            {
                string family = TypeFamily(col.BaseType);
                if (family is "tinyint" or "smallint")
                    hits.Add($"{t.FullName}.{col.Name} ({col.DataType}, {(family == "tinyint" ? "max 255" : "max 32,767")}{(t.RowCount is { } r ? $", ≈{r:N0} rows" : "")})");
            }
        }

        if (hits.Count == 0) return;
        c.Add("SMALL_IDENTITY", FindingSeverity.Medium, FindingCategory.Design,
            "Auto-increment keys with a very small range",
            "TINYINT / SMALLINT identity keys run out of values quickly and then make inserts fail.",
            "Use INT or BIGINT for surrogate keys of tables that can grow.",
            hits, c.Schema.Tables.Count);
    }

    private static void WideTables(Context c)
    {
        var wide = new List<string>();
        var manyColumns = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            long bytes = t.Columns.Sum(EstimateColumnBytes);
            if (bytes > 8060) wide.Add($"{t.FullName} (≈{bytes:N0} bytes, {t.Columns.Count} columns)");
            else if (t.Columns.Count > 60) manyColumns.Add($"{t.FullName} ({t.Columns.Count} columns)");
        }

        if (wide.Count > 0)
            c.Add("WIDE_ROWS", FindingSeverity.Medium, FindingCategory.Performance,
                "Rows that can exceed the 8 KB page size",
                "Rows wider than a data page spill into overflow storage (row chaining), which slows scans and increases I/O.",
                "Move rarely used or very large columns to a side table, or switch them to LOB types that are stored off-row.",
                wide, c.Schema.Tables.Count);

        if (manyColumns.Count > 0)
            c.Add("MANY_COLUMNS", FindingSeverity.Low, FindingCategory.Design,
                "Tables with a very large number of columns",
                "Very wide tables are hard to maintain and often hide missing normalisation.",
                "Split the table by responsibility (vertical partitioning) or normalise repeating column groups.",
                manyColumns, c.Schema.Tables.Count);
    }

    private static void DeprecatedTypes(Context c)
    {
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            foreach (var col in t.Columns)
            {
                string type = col.BaseType.ToLowerInvariant();
                bool legacySqlServer = c.Schema.Engine == DbEngine.SqlServer && type is "text" or "ntext" or "image";
                bool moneyPostgres = c.Schema.Engine == DbEngine.PostgreSql && type == "money";
                if (legacySqlServer || moneyPostgres) hits.Add($"{t.FullName}.{col.Name} ({col.DataType})");
            }
        }

        if (hits.Count == 0) return;
        c.Add("DEPRECATED_TYPES", FindingSeverity.Medium, FindingCategory.Maintainability,
            "Deprecated or discouraged data types",
            "These types are deprecated or have known pitfalls (limited functions, locale dependent rounding).",
            "Use VARCHAR(MAX) / NVARCHAR(MAX) / VARBINARY(MAX) on SQL Server and NUMERIC on PostgreSQL.",
            hits, c.Schema.ColumnCount);
    }

    private static void MoneyAsFloat(Context c)
    {
        string[] moneyWords = { "price", "amount", "total", "balance", "salary", "cost", "fee", "tax", "revenue", "payment", "discount", "vat" };
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
            foreach (var col in t.Columns)
                if (TypeFamily(col.BaseType) == "float" && moneyWords.Any(w => Key(col.Name).Contains(w, StringComparison.Ordinal)))
                    hits.Add($"{t.FullName}.{col.Name} ({col.DataType})");

        if (hits.Count == 0) return;
        c.Add("MONEY_AS_FLOAT", FindingSeverity.Medium, FindingCategory.Integrity,
            "Monetary values stored as floating point",
            "FLOAT / DOUBLE cannot represent most decimal fractions exactly, causing rounding drift in sums and comparisons.",
            "Use DECIMAL/NUMERIC with an explicit precision and scale (e.g. DECIMAL(18,2)).",
            hits, c.Schema.ColumnCount);
    }

    private static void DatesStoredAsText(Context c)
    {
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
            foreach (var col in t.Columns)
                if (TypeFamily(col.BaseType) == "string" && IsDateName(col.Name))
                    hits.Add($"{t.FullName}.{col.Name} ({col.DataType})");

        if (hits.Count == 0) return;
        c.Add("DATE_AS_TEXT", FindingSeverity.Medium, FindingCategory.Integrity,
            "Dates stored as text",
            "Text dates cannot be validated by the database, sort and compare incorrectly across formats and prevent range index scans.",
            "Convert the columns to DATE / DATETIME2 / TIMESTAMP types.",
            hits, c.Schema.ColumnCount);
    }

    private static bool IsDateName(string name)
    {
        string key = Key(name);
        if (key.EndsWith("candidate", StringComparison.Ordinal) || key.EndsWith("update", StringComparison.Ordinal)
            || key.EndsWith("validate", StringComparison.Ordinal) || key.EndsWith("mandate", StringComparison.Ordinal)) return false;

        string[] suffixes = { "date", "datetime", "timestamp", "createdat", "updatedat", "deletedat", "modifiedat", "expiresat", "birthday", "dob" };
        return suffixes.Any(suffix => key.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static void FlagsStoredAsWideTypes(Context c)
    {
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            foreach (var col in t.Columns)
            {
                if (!IsFlagName(col.Name)) continue;

                string family = TypeFamily(col.BaseType);
                bool oversized = family is "bigint" or "decimal" or "float"
                                 || family == "string" && (col.MaxLength is null || col.MaxLength < 0 || col.MaxLength > 5);
                if (oversized) hits.Add($"{t.FullName}.{col.Name} ({col.DataType})");
            }
        }

        if (hits.Count == 0) return;
        c.Add("FLAG_WIDE_TYPE", FindingSeverity.Low, FindingCategory.Design,
            "Boolean flags stored in oversized types",
            "Flags such as is_*/has_* are stored as wide numbers or long text, wasting space and allowing invalid values.",
            "Use BIT / BOOLEAN (or TINYINT(1)) together with a NOT NULL DEFAULT.",
            hits, c.Schema.ColumnCount);
    }

    private static bool IsFlagName(string name)
    {
        foreach (string prefix in new[] { "is", "has", "can", "should" })
        {
            if (name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && char.IsUpper(name[prefix.Length])) return true;
        }
        return false;
    }

    private static void ExcessiveNullability(Context c)
    {
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            var data = t.Columns.Where(x => !x.IsPrimaryKey).ToList();
            if (data.Count < 5) continue;
            int nullable = data.Count(x => x.IsNullable);
            double share = nullable / (double)data.Count;
            if (share > 0.6) hits.Add($"{t.FullName} ({nullable}/{data.Count} nullable, {share:P0})");
        }

        if (hits.Count == 0) return;
        c.Add("EXCESSIVE_NULLS", FindingSeverity.Low, FindingCategory.Integrity,
            "Tables where most columns allow NULL",
            "When almost every column is optional the database enforces nothing and data quality depends entirely on the application.",
            "Mark business-critical columns NOT NULL and add defaults or CHECK constraints.",
            hits, c.Schema.Tables.Count);
    }

    private static void ConflictingColumnTypes(Context c)
    {
        var groups = c.Schema.Tables
            .SelectMany(t => t.Columns.Select(col => (table: t, col)))
            .Where(x =>
            {
                string key = Key(x.col.Name);
                return key.Length > 2 && (key.EndsWith("id", StringComparison.Ordinal) || key is "status" or "code" or "type")
                       && !x.col.IsPrimaryKey;
            })
            .GroupBy(x => Key(x.col.Name));

        var hits = new List<string>();
        foreach (var g in groups)
        {
            var types = g.Select(x => x.col.DataType.ToLowerInvariant()).Distinct().ToList();
            if (types.Count < 2) continue;
            hits.Add($"{g.First().col.Name}: {string.Join(" / ", types.Take(4))} ({g.Count()} tables)");
        }

        if (hits.Count == 0) return;
        c.Add("CONFLICTING_TYPES", FindingSeverity.Low, FindingCategory.Integrity,
            "Same column name, different data types",
            "Columns with the same meaning use different types across tables, which risks implicit conversions in joins.",
            "Standardise the data type of shared concepts (ids, status codes) across the schema.",
            hits, c.Schema.Tables.Count);
    }

    private static void NamingConsistency(Context c)
    {
        CheckNaming(c, "TABLE_NAMING", "Inconsistent table naming",
            c.Schema.Tables.Select(t => t.Name).ToList(), c.Schema.Tables.Count);
        CheckNaming(c, "COLUMN_NAMING", "Inconsistent column naming",
            c.Schema.Tables.SelectMany(t => t.Columns.Select(x => x.Name)).Distinct(StringComparer.Ordinal).ToList(), c.Schema.ColumnCount);

        var special = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            if (HasSpecialChars(t.Name)) special.Add(t.FullName);
            special.AddRange(t.Columns.Where(x => HasSpecialChars(x.Name)).Select(x => $"{t.FullName}.{x.Name}"));
        }

        if (special.Count > 0)
            c.Add("SPECIAL_CHARS", FindingSeverity.Low, FindingCategory.Maintainability,
                "Names with spaces or special characters",
                "Identifiers that need quoting are error-prone in SQL, ORMs and scripts.",
                "Use letters, digits and underscores only.",
                special, c.Schema.Tables.Count + c.Schema.ColumnCount);
    }

    private static void CheckNaming(Context c, string ruleId, string title, List<string> names, int total)
    {
        var styles = names.Where(n => !HasSpecialChars(n))
            .Select(n => (name: n, style: NamingStyle(n)))
            .Where(x => x.style != null)
            .ToList();
        if (styles.Count < 4) return;

        var groups = styles.GroupBy(x => x.style!).ToDictionary(g => g.Key, g => g.Select(x => x.name).ToList());
        var significant = groups.Where(g => g.Value.Count >= 2 && g.Value.Count / (double)styles.Count >= 0.1).ToList();
        if (significant.Count < 2) return;

        string summary = string.Join(", ", significant.OrderByDescending(g => g.Value.Count)
            .Select(g => $"{g.Key}: {g.Value.Count} (e.g. {string.Join(", ", g.Value.Take(2))})"));

        c.Add(ruleId, FindingSeverity.Low, FindingCategory.Maintainability, title,
            "The schema mixes several naming conventions, which makes it harder to read and to map in code.",
            "Pick one convention (snake_case is the most portable) and rename outliers.",
            new[] { summary }, Math.Max(total, 1), significant.Sum(g => g.Value.Count) - significant.Max(g => g.Value.Count));
    }

    /// <summary>Returns null for neutral names (a single lower-case word fits every convention).</summary>
    private static string? NamingStyle(string name)
    {
        bool underscore = name.Contains('_');
        bool upper = name.Any(char.IsUpper);
        bool lower = name.Any(char.IsLower);

        if (underscore) return upper && !lower ? "UPPER_SNAKE" : upper ? "Mixed_Snake" : "snake_case";
        if (!upper) return null;
        if (!lower) return name.Length > 2 ? "UPPERCASE" : null;
        return char.IsUpper(name[0]) ? "PascalCase" : "camelCase";
    }

    private static bool HasSpecialChars(string name) => name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_'));

    private static void MissingAuditColumns(Context c)
    {
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            if (t.Columns.Count < 4) continue;
            bool junction = t.PrimaryKeyColumns.Count() >= 2 && t.Columns.Count <= 4;
            if (junction) continue;
            bool hasAudit = t.Columns.Any(col =>
            {
                string key = Key(col.Name);
                return key.Contains("creat") || key.Contains("updat") || key.Contains("modif") || key.Contains("timestamp") || key.Contains("rowversion") || key.Contains("insertdate");
            });
            if (!hasAudit) hits.Add(t.FullName);
        }

        if (hits.Count == 0) return;
        c.Add("NO_AUDIT_COLUMNS", FindingSeverity.Low, FindingCategory.Security,
            "Tables without creation / modification timestamps",
            "Without created/updated columns you cannot tell when data changed, which hurts debugging, auditing and incremental loads.",
            "Add created_at / updated_at (and created_by / updated_by for sensitive tables), or enable temporal tables / CDC.",
            hits, c.Schema.Tables.Count);
    }

    private static void DisabledTriggers(Context c)
    {
        var hits = c.Schema.Triggers.Where(t => t.IsDisabled).Select(t => $"{t.Table}.{t.Name}").ToList();
        if (hits.Count == 0) return;
        c.Add("DISABLED_TRIGGERS", FindingSeverity.Low, FindingCategory.Maintainability,
            "Disabled triggers",
            "Disabled triggers usually mean business rules are silently not enforced.",
            "Re-enable or drop them so the schema reflects the real behaviour.",
            hits, Math.Max(c.Schema.Triggers.Count, 1));
    }

    private static void IsolatedTables(Context c)
    {
        if (c.Schema.Tables.Count < 4 || c.Schema.ForeignKeys.Count == 0) return;
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in c.Schema.ForeignKeys)
        {
            connected.Add(fk.FromTable);
            connected.Add(fk.ToTable);
        }

        var hits = c.Schema.Tables.Where(t => !connected.Contains(t.FullName)).Select(t => t.FullName).ToList();
        if (hits.Count == 0) return;
        c.Add("ISOLATED_TABLES", FindingSeverity.Info, FindingCategory.Design,
            "Tables without any relationships",
            "These tables are neither referenced nor referencing. That is fine for logs or configuration, but can indicate missing foreign keys or unused tables.",
            "Check whether relationships are missing or whether the tables can be archived.",
            hits, c.Schema.Tables.Count);
    }

    private static void EmptyTables(Context c)
    {
        var hits = c.Schema.Tables.Where(t => t.RowCount == 0).Select(t => t.FullName).ToList();
        if (hits.Count == 0 || hits.Count == c.Schema.Tables.Count) return;
        c.Add("EMPTY_TABLES", FindingSeverity.Info, FindingCategory.Maintainability,
            "Tables that appear to be empty",
            "Row counts come from catalog statistics (estimates). Empty tables may be unused leftovers.",
            "Confirm whether they are still needed and drop dead tables.",
            hits, c.Schema.Tables.Count);
    }

    // ---- index based rules

    private static void ForeignKeysWithoutIndex(Context c)
    {
        var hits = new List<string>();
        bool large = false;
        foreach (var fk in c.Schema.ForeignKeys)
        {
            var indexes = c.IndexesByTable[fk.FromTable];
            bool covered = indexes.Any(ix => ix.Columns.Count >= fk.FromColumns.Count
                && fk.FromColumns.All(col => ix.Columns.Take(fk.FromColumns.Count).Contains(col, StringComparer.OrdinalIgnoreCase)));
            if (covered) continue;

            hits.Add($"{fk.FromTable}({string.Join(", ", fk.FromColumns)}) → {fk.ToTable}");
            if (c.Schema.FindTable(fk.FromTable)?.RowCount > 100_000) large = true;
        }

        if (hits.Count == 0) return;
        c.Add("FK_NO_INDEX", large ? FindingSeverity.High : FindingSeverity.Medium, FindingCategory.Performance,
            "Foreign keys without a supporting index",
            "Joins and cascading deletes/updates on unindexed foreign keys cause full table scans and lock escalation.",
            "Create an index that starts with the foreign key columns.",
            hits, c.Schema.ForeignKeys.Count);
    }

    private static void DuplicateIndexes(Context c)
    {
        var hits = new List<string>();
        foreach (var (table, indexes) in c.IndexesByTable)
        {
            var list = indexes.ToList();
            var handled = new HashSet<IndexInfo>();

            foreach (var group in list.GroupBy(ix => string.Join(",", ix.Columns).ToLowerInvariant() + "|" + ix.IsUnique + "|" + string.Join(",", ix.IncludedColumns).ToLowerInvariant() + "|" + ix.Filter))
            {
                if (group.Count() < 2) continue;
                // keep the primary key / first one, report the rest
                var keep = group.OrderByDescending(x => x.IsPrimary).First();
                foreach (var dup in group.Where(x => !ReferenceEquals(x, keep)))
                {
                    handled.Add(dup);
                    hits.Add($"{table}: {dup.Name} duplicates {keep.Name} ({string.Join(", ", dup.Columns)})");
                }
            }

            foreach (var ix in list.Where(x => !x.IsPrimary && !x.IsUnique && !handled.Contains(x) && x.Filter is null))
            {
                var cover = list.FirstOrDefault(other => !ReferenceEquals(other, ix) && !handled.Contains(other)
                    && other.Columns.Count > ix.Columns.Count
                    && ix.Columns.Select((col, i) => string.Equals(col, other.Columns[i], StringComparison.OrdinalIgnoreCase)).All(x => x));
                if (cover is not null) hits.Add($"{table}: {ix.Name} ({string.Join(", ", ix.Columns)}) is covered by {cover.Name} ({string.Join(", ", cover.Columns)})");
            }
        }

        if (hits.Count == 0) return;
        c.Add("DUPLICATE_INDEXES", FindingSeverity.Medium, FindingCategory.Performance,
            "Duplicate or redundant indexes",
            "Redundant indexes waste storage, slow down INSERT/UPDATE/DELETE and add maintenance work without helping reads.",
            "Drop the duplicates and keep the widest index that serves the queries.",
            hits, Math.Max(c.Schema.Indexes.Count, 1));
    }

    private static void LargeTablesWithoutIndexes(Context c)
    {
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables.Where(t => t.RowCount >= 100_000))
        {
            if (c.IndexesByTable[t.FullName].All(ix => ix.IsPrimary))
                hits.Add($"{t.FullName} (≈{t.RowCount:N0} rows)");
        }

        if (hits.Count == 0) return;
        c.Add("LARGE_NO_INDEX", FindingSeverity.Medium, FindingCategory.Performance,
            "Large tables with no secondary index",
            "Only the primary key exists. Any query filtering on another column scans the whole table.",
            "Inspect the most frequent queries and add targeted indexes.",
            hits, c.Schema.Tables.Count);
    }

    private static void TooManyIndexes(Context c)
    {
        var hits = c.IndexesByTable
            .Select(kv => (table: kv.Key, count: kv.Value.Count(ix => !ix.IsPrimary)))
            .Where(x => x.count > 8)
            .Select(x => $"{x.table} ({x.count} secondary indexes)")
            .ToList();
        if (hits.Count == 0) return;
        c.Add("TOO_MANY_INDEXES", FindingSeverity.Low, FindingCategory.Performance,
            "Tables with many indexes",
            "Every index is updated on each write. Tables with many indexes pay a high write amplification cost.",
            "Review index usage statistics and drop the unused ones.",
            hits, c.Schema.Tables.Count);
    }

    private static void RandomGuidClusteredKeys(Context c)
    {
        if (c.Schema.Engine != DbEngine.SqlServer) return;
        var hits = new List<string>();
        foreach (var t in c.Schema.Tables)
        {
            var pk = t.PrimaryKeyColumns.ToList();
            if (pk.Count != 1 || TypeFamily(pk[0].BaseType) != "guid") continue;
            var pkIndex = c.IndexesByTable[t.FullName].FirstOrDefault(ix => ix.IsPrimary);
            if (pkIndex?.Type is { } type && type.Contains("CLUSTERED", StringComparison.OrdinalIgnoreCase) && !type.Contains("NONCLUSTERED", StringComparison.OrdinalIgnoreCase))
                hits.Add($"{t.FullName}.{pk[0].Name}");
        }

        if (hits.Count == 0) return;
        c.Add("GUID_CLUSTERED_PK", FindingSeverity.Low, FindingCategory.Performance,
            "Random GUIDs as clustered primary keys",
            "Random GUIDs fragment the clustered index and cause page splits on every insert.",
            "Use NEWSEQUENTIALID() / sequential GUIDs, or keep an INT/BIGINT clustered key and the GUID as a unique non-clustered key.",
            hits, c.Schema.Tables.Count);
    }

    // ================================================================== helpers

    private sealed class Context
    {
        public Context(DatabaseSchema schema)
        {
            Schema = schema;
            IndexesByTable = schema.Tables.ToDictionary(t => t.FullName, _ => new List<IndexInfo>(), StringComparer.OrdinalIgnoreCase);
            foreach (var ix in schema.Indexes)
            {
                if (IndexesByTable.TryGetValue(ix.Table, out var list)) list.Add(ix);
            }
        }

        public DatabaseSchema Schema { get; }
        public Dictionary<string, List<IndexInfo>> IndexesByTable { get; }
        public List<(Finding Finding, double Ratio)> Entries { get; } = new();

        public void Add(string ruleId, FindingSeverity severity, string category, string title, string description,
            string recommendation, IReadOnlyCollection<string> objects, int total, int? affected = null)
        {
            int count = affected ?? objects.Count;
            var listed = objects.Take(MaxListedObjects).ToList();
            if (objects.Count > MaxListedObjects) listed.Add($"… and {objects.Count - MaxListedObjects} more");

            Entries.Add((new Finding
            {
                RuleId = ruleId,
                Severity = severity,
                Category = category,
                Title = title,
                Description = description,
                Recommendation = recommendation,
                Objects = listed,
                Count = count
            }, Math.Min(1.0, count / (double)Math.Max(total, 1))));
        }
    }

    /// <summary>Lower-case letters and digits only, so <c>Customer_ID</c> and <c>customerId</c> compare equal.</summary>
    internal static string Key(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char ch in name)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    internal static string TypeFamily(string baseType) => baseType.ToLowerInvariant() switch
    {
        "int" or "integer" or "int4" or "mediumint" or "serial" => "int",
        "bigint" or "int8" or "bigserial" => "bigint",
        "smallint" or "int2" or "smallserial" => "smallint",
        "tinyint" => "tinyint",
        "bit" or "bool" or "boolean" => "bool",
        "char" or "varchar" or "nchar" or "nvarchar" or "bpchar" or "character" or "character varying" or "text" or "ntext"
            or "tinytext" or "mediumtext" or "longtext" or "citext" or "name" => "string",
        "float" or "float4" or "float8" or "real" or "double" or "double precision" => "float",
        "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
        "uniqueidentifier" or "uuid" => "guid",
        "datetime" or "datetime2" or "smalldatetime" or "timestamp" or "timestamptz" or "datetimeoffset" => "datetime",
        "binary" or "varbinary" or "bytea" or "blob" or "tinyblob" or "mediumblob" or "longblob" or "image" => "binary",
        var other => other
    };

    private static long EstimateColumnBytes(ColumnInfo col)
    {
        string baseType = col.BaseType.ToLowerInvariant();
        long len = col.MaxLength ?? 0;
        return TypeFamily(baseType) switch
        {
            "int" => 4,
            "bigint" => 8,
            "smallint" => 2,
            "tinyint" or "bool" => 1,
            "float" => 8,
            "decimal" => 9,
            "guid" => 16,
            "datetime" => 8,
            "date" => 3,
            "string" or "binary" when len is <= 0 or > 8000 => 24,
            "string" => baseType.StartsWith('n') ? len * 2 : len,
            "binary" => len,
            _ => 8
        };
    }

    private static string? SensitiveLabel(string key)
    {
        if (key.Contains("email") || key == "mail") return "email";
        if (key.Contains("phone") || key.Contains("mobile") || key.Contains("gsm") || key is "tel" or "telephone") return "phone";
        if (key.Contains("ssn") || key.Contains("socialsecurity") || key.Contains("tckn") || key.Contains("tcno") || key.Contains("tckimlik")
            || key.Contains("nationalid") || key.Contains("identitynumber") || key.Contains("passport") || key.Contains("citizenid")) return "national id";
        if (key.Contains("iban") || key.Contains("creditcard") || key.Contains("cardnumber") || key.Contains("cardno") || key.Contains("cvv") || key.Contains("cvc")
            || key.Contains("bankaccount") || key.Contains("accountnumber")) return "financial";
        if (key.Contains("salary") || key.Contains("income")) return "financial";
        if (key.Contains("birthdate") || key.Contains("dateofbirth") || key == "dob" || key.Contains("birthday")) return "date of birth";
        if (key.Contains("diagnosis") || key.Contains("disease") || key.Contains("medication") || key.Contains("healthcode")) return "health";
        if (key.Contains("address") && !key.Contains("ipaddress") && !key.Contains("macaddress") && !key.Contains("emailaddress") || key.Contains("postalcode") || key.Contains("zipcode")) return "address";
        if (key.Contains("firstname") || key.Contains("lastname") || key.Contains("surname") || key.Contains("fullname")) return "name";
        if (key.Contains("ipaddress") || key is "ip" or "clientip" or "userip") return "ip address";
        return null;
    }

    private static List<List<string>> StronglyConnectedComponents(Dictionary<string, List<string>> graph)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var low = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        var result = new List<List<string>>();
        int counter = 0;

        void Visit(string v)
        {
            index[v] = low[v] = counter++;
            stack.Push(v);
            onStack.Add(v);

            foreach (var w in graph[v])
            {
                if (!index.ContainsKey(w))
                {
                    Visit(w);
                    low[v] = Math.Min(low[v], low[w]);
                }
                else if (onStack.Contains(w))
                {
                    low[v] = Math.Min(low[v], index[w]);
                }
            }

            if (low[v] != index[v]) return;
            var component = new List<string>();
            string w2;
            do
            {
                w2 = stack.Pop();
                onStack.Remove(w2);
                component.Add(w2);
            } while (!string.Equals(w2, v, StringComparison.OrdinalIgnoreCase));
            result.Add(component);
        }

        foreach (var v in graph.Keys)
            if (!index.ContainsKey(v)) Visit(v);

        return result;
    }
}
