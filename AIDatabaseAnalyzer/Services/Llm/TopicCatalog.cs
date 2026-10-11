using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>A named selection of topics ("Quick health check", "Security &amp; compliance"…).</summary>
public sealed record TopicPreset(string Name, string Description, Func<AnalysisTopic, bool> Includes);

/// <summary>The built-in analysis questions, grouped into categories the user can filter by.</summary>
public static class TopicCatalog
{
    public static readonly string[] Categories = { "Performance", "Design", "Data Quality", "Security", "Compliance", "Scalability", "Operations", "Developer" };

    private static AnalysisTopic T(string category, string title, string prompt, TopicNeeds needs = TopicNeeds.None) =>
        new() { Category = category, Title = title, Prompt = prompt, Needs = needs };

    private static readonly HashSet<string> QuickCheck = new(StringComparer.Ordinal)
    {
        "Heap Table and Primary Key Analysis",
        "Foreign Key Index Deficiency and Hidden Table Scans",
        "Orphan Records and Missing Relational Integrity",
        "Data Type Inconsistency Analysis",
        "Referential Actions and Cascade Risk Review",
        "Sensitive Data (PII/PHI) Encryption at Rest Deficiencies",
        "Missing Audit Trails and Logging Losses",
        "Ready-to-Run DDL Fix Script"
    };

    public static IReadOnlyList<TopicPreset> Presets { get; } = new[]
    {
        new TopicPreset("Quick health check", "Eight high-value checks for a first look",
            t => QuickCheck.Contains(t.Title)),
        new TopicPreset("Performance tuning", "Indexes, query shapes, storage and maintenance",
            t => t.Category == "Performance" || t.Title is "Partitioning and Archiving Strategy" or "Index and Statistics Maintenance Plan"),
        new TopicPreset("Security & compliance", "Access, injection, personal data and retention",
            t => t.Category is "Security" or "Compliance"),
        new TopicPreset("Design review", "Modelling, integrity and data quality",
            t => t.Category is "Design" or "Data Quality"),
        new TopicPreset("Developer handbook", "Documentation, queries, models and test data",
            t => t.Category == "Developer" || t.Title == "Naming Standards and Consistency"),
        new TopicPreset("Operations & growth", "Maintenance, change safety, monitoring and scaling",
            t => t.Category is "Operations" or "Scalability"),
        new TopicPreset("Everything", "Every built-in topic", t => !t.IsCustom),
    };

    /// <summary>Explains why a topic cannot be answered well with what was extracted, or null when it can.</summary>
    public static string? Limitation(TopicNeeds needs, DatabaseSchema schema)
    {
        if (needs.HasFlag(TopicNeeds.SampleData) && schema.SampleData.Count == 0)
            return "Works best with sample rows — enable them on the Scope page.";
        if (needs.HasFlag(TopicNeeds.Triggers) && schema.Triggers.Count == 0)
            return "No triggers were extracted.";
        if (needs.HasFlag(TopicNeeds.Views) && schema.Views.Count == 0)
            return "No views were extracted.";
        if (needs.HasFlag(TopicNeeds.Routines) && schema.Routines.Count == 0)
            return "No stored procedures or functions were extracted.";
        if (needs.HasFlag(TopicNeeds.CodeDefinitions)
            && !schema.Triggers.Any(t => !string.IsNullOrEmpty(t.Definition))
            && !schema.Views.Any(v => !string.IsNullOrEmpty(v.Definition))
            && !schema.Routines.Any(r => !string.IsNullOrEmpty(r.Definition)))
            return "Needs SQL definitions — enable them on the Scope page.";
        if (needs.HasFlag(TopicNeeds.Indexes) && !schema.IndexesIncluded)
            return "Indexes were not extracted.";
        return null;
    }

    public static List<AnalysisTopic> CreateDefaults() => new()
    {
        // ------------------------------------------------------------------ Performance
        T("Performance", "Non-SARGable Query Predicates and Missing Index Detection",
            "Act as a Senior Database Performance Expert. Find missing indexes and structural designs that force non-SARGable predicates. Evaluate table relationships, primary keys and existing indexes; identify columns with clear JOIN or filter potential that lack a suitable index. Only recommend selective (high-cardinality) columns or proven composite orders, show the CREATE INDEX statement, and do not recommend indexes that already exist under another name.",
            TopicNeeds.Indexes),
        T("Performance", "Over-indexing and Overlapping Index Costs",
            "Assume the role of a Database Architecture Auditor. Identify over-indexed tables and overlapping or redundant composite indexes (same leading columns, left-prefix duplicates, unique and non-unique twins). Consider how write-heavy each table looks from its name, size and sample data. Output a table that states which indexes to drop or merge and the write and storage cost they cause.",
            TopicNeeds.Indexes),
        T("Performance", "Trigger-Induced Performance Bottlenecks and Deadlocks",
            "Acting as an SQL Performance Tuning Engineer, analyse the trigger logic. Focus on triggers that update other tables during DML, row-by-row processing, cascading or recursive trigger chains and long-running work inside the transaction. Pinpoint logic that should move to asynchronous processing or the application layer.",
            TopicNeeds.Triggers | TopicNeeds.CodeDefinitions),
        T("Performance", "Hidden Complexity in View Layers and Transient Data Load",
            "Assuming the role of an Enterprise Data Warehouse Architect, parse the view definitions to find excessive nesting (views on views), heavy joins, scalar functions and SELECT *. Decide whether each problem should be solved with a rewritten query, a CTE, an indexed or materialised view, or a reporting table.",
            TopicNeeds.Views | TopicNeeds.CodeDefinitions),
        T("Performance", "Foreign Key Index Deficiency and Hidden Table Scans",
            "As a Database Performance Expert, check every foreign key for an index whose leading columns match the referencing columns. Unindexed foreign keys cause full scans on joins and during deletes or updates of the parent row (and lock escalation). Report which foreign keys urgently need an index, ordered by the size of the referencing table, with the CREATE INDEX statements.",
            TopicNeeds.Indexes),
        T("Performance", "Execution Plan Simulation and Cost Estimation",
            "Acting as a Query Optimization Engineer, construct the three most expensive realistic queries this schema will face (based on relationships, row counts and sample data). Describe how the engine is likely to execute them (join strategies, scans versus seeks, sorts, spills), predict the bottlenecks and give concrete rewrites or index changes that lower the cost."),
        T("Performance", "Implicit Conversions on Join Paths",
            "As a Database Performance Expert, compare the data types of every pair of columns that are joined through foreign keys or that share the same name across tables (for example int versus bigint, varchar versus nvarchar, char versus varchar, numeric precision, text-encoded numbers or dates). Explain where an implicit conversion would make an index unusable, and give the ALTER statements to align the types. Collations are not part of the extracted metadata; mention collation only as something to verify."),
        T("Performance", "Write Hot Spots and Insert Contention",
            "Act as a Site Reliability Engineer for a busy OLTP system. Identify structures that concentrate concurrent writes: ever-increasing clustered keys on very active tables (last-page insert contention), random GUID keys causing page splits, counter or sequence tables updated on every transaction, wide indexes on frequently updated columns and status columns that flip constantly. For each, explain the symptom under load and the engine-specific mitigation.",
            TopicNeeds.Indexes),
        T("Performance", "Heap Table and Primary Key Analysis",
            "Act as a Database Architecture Expert. Identify tables without a primary key or clustered index (heaps). Explain the performance, fragmentation and replication risks for this engine, and recommend the column(s) that should become the primary key based on names, types and sample data, with the DDL."),
        T("Performance", "Table Width and Page Fragmentation Risk",
            "Act as a storage specialist for this database engine. Using the column types and lengths, estimate the maximum row size of each table and flag tables whose rows can exceed the engine's page or row limits (8 KB pages in SQL Server and PostgreSQL with TOAST, 16 KB pages and off-page storage in InnoDB). Point out wide variable-length columns that should move to a side table or be shortened, and explain the effect on I/O and caching."),

        // ------------------------------------------------------------------ Design
        T("Design", "Bloated Records and Data Type Wastage",
            "As a Data Modeling Expert, identify columns with unnecessarily large data types. Compare the declared types with the sample values (for example a 255-character column holding a flag, bigint for tiny lookup tables, datetime where a date is enough). Recommend narrower types, and state the truncation risk and the migration steps for each change.",
            TopicNeeds.SampleData),
        T("Design", "Normalization Violations and Data Redundancy",
            "Act as a Database Design Auditor. Using the schema and the sample data, detect violations of the normal forms, in particular 3NF: repeating groups, comma-separated lists in one column, and transitive dependencies. Decide for each case whether it is a design flaw or a deliberate read optimisation, and show the normalised design when it is a flaw.",
            TopicNeeds.SampleData),
        T("Design", "Poorly Designed Denormalization and Loss of Data Integrity",
            "Assume the role of a Data Modeling Architect. Find denormalised (copied) data and work out how it is kept in sync — triggers, application code or nothing. Describe the inconsistencies that appear when the copy is not updated, and list the database-level guarantees that are missing."),
        T("Design", "Orphan Records and Missing Relational Integrity",
            "Acting as a Data Integrity Specialist, identify columns that are clearly references to other tables but have no foreign key constraint. Use column names, types and matching sample values as evidence. Report only high-confidence relationships, with the ALTER TABLE … ADD CONSTRAINT statement and a query that finds existing orphan rows first."),
        T("Design", "Circular Dependency and Infinite Loops",
            "As a Solutions Architect, map circular dependency routes through foreign keys and triggers. Analyse whether hierarchical or relational designs create loops, whether rows can be inserted at all without deferring constraints, and whether recursive trigger execution is possible. List each route step by step."),
        T("Design", "ORM Anti-Patterns: Schema Designs Prone to N+1 Queries",
            "Assuming the role of a Backend Integration Expert, identify table designs that typically cause N+1 query problems or chatty access in ORMs: data that is always read together but split into many one-to-one tables, deep chains of small lookups, and entity-attribute-value structures. Suggest schema or query-shape changes."),
        T("Design", "Recursive CTE Misusage and Depth Overflows",
            "In the role of a Site Reliability Engineer, identify self-referencing columns (hierarchies and trees). Check for missing depth limits or cycle protection, and recommend a structure (adjacency list with constraints, materialised path, closure table or nested sets) that fits each hierarchy and how it is queried."),
        T("Design", "Siloed and Fragmented Schema Modules",
            "Act as an Enterprise Architecture Lead. Group all tables into modules and identify isolated silos that are not integrated with the rest of the schema. Within a Domain-Driven Design view, find similar data stored twice with different structures or rules."),
        T("Design", "Polymorphic Relationship Flaws and Loss of Referential Integrity",
            "Acting as a Data Modeling Expert, examine dual-column combinations such as entity_id plus entity_type (polymorphic associations). Show how they can produce orphan records without foreign keys, and design an alternative (exclusive arcs with CHECK constraints, a supertype table or separate link tables)."),
        T("Design", "Data Type Inconsistency Analysis",
            "Assume the role of a Data Quality Analyst. Find columns with the same conceptual name across tables (for example id, user_id, status, created_at) that use different data types or lengths. Explain the risks (implicit conversions in joins, truncation when copying data) and propose one canonical type per concept."),
        T("Design", "Identity Column Capacity Audit",
            "In the role of a Database Capacity Planner, check identity / auto-increment and sequence-backed keys. Flag small key types (tinyint, smallint, int on large or fast-growing tables) and estimate how close each is to exhaustion from the row counts. Explain the migration path to a larger type with minimal downtime."),
        T("Design", "Naming Standards and Consistency",
            "Assume the role of a Database Code Reviewer. Review the naming of tables, columns, keys and indexes. Identify mixed conventions (PascalCase, camelCase, snake_case, prefixes like tbl_, plural versus singular), reserved words and unclear abbreviations, and propose one consistent convention with a rename list for the worst offenders."),
        T("Design", "Soft Delete Pattern Consistency",
            "Assume the role of a Software Architecture Auditor. Search for soft delete markers (IsDeleted, deleted_at, IsActive, status codes). Identify core tables that use it and related tables that do not, unique constraints that ignore soft-deleted rows, and missing filtered or partial indexes. Detail the reporting and integrity risks of the inconsistent approach."),
        T("Design", "Excessive Nullability and Constraint Audit",
            "Act as a Data Integrity Specialist. Review nullability across the schema. Identify business-critical columns that allow NULL without a default or CHECK constraint, and explain how this pushes validation to the application. Propose NOT NULL, DEFAULT and CHECK changes, together with the query to find violating rows first."),
        T("Design", "Referential Actions and Cascade Risk Review",
            "Assume the role of a Data Integrity Architect. Review the ON DELETE / ON UPDATE behaviour of every foreign key (a missing action means NO ACTION). Identify dangerous ON DELETE CASCADE chains that could silently delete large amounts of data, SET NULL actions on NOT NULL columns, and relationships that should use soft delete or RESTRICT instead. Present a table with the foreign key, current action, risk and recommended action."),
        T("Design", "Ready-to-Run DDL Fix Script",
            "Act as a Senior DBA who prepares a safe migration. Write a single reviewed SQL script for this database engine that fixes the most valuable and least risky problems: missing primary keys, missing indexes on foreign keys, redundant indexes and obviously wrong data types. Group the statements by risk (safe / needs testing / needs a maintenance window), put a short comment above each statement explaining why, and make the script idempotent where the engine allows it. Do not touch objects you cannot see in the schema, and begin with a warning to run it on a copy first.",
            TopicNeeds.Indexes),

        // ------------------------------------------------------------------ Data Quality
        T("Data Quality", "Sample Data Quality Signals",
            "Act as a Data Quality Engineer. Inspect the sample rows of every table for quality problems: placeholder or test values (test, xxx, 1900-01-01, 9999), inconsistent formats in the same column (phone numbers, dates, casing, trailing spaces), numbers or dates stored as text, impossible values (negative quantities, future birth dates) and mixed units or currencies. For each signal, name the column, quote an example value, and propose the constraint or cleanup that prevents it.",
            TopicNeeds.SampleData),
        T("Data Quality", "Uniqueness and Natural Key Integrity",
            "Act as a Data Integrity Specialist. Identify natural or business keys (e-mail, username, SKU, code, slug, external ids, document numbers) that are not protected by a UNIQUE constraint or unique index, including case-insensitive and soft-delete aware uniqueness. Use sample data as evidence where it helps. Give the DDL and a query that lists existing duplicates before the constraint is added."),
        T("Data Quality", "Enumerations, Status Codes and Lookup Tables",
            "Act as a Data Modeling Expert. Find status, type and category columns stored as free text or magic numbers. Recommend, per column, a lookup table with a foreign key, a CHECK constraint or a native enum type for this engine, list the allowed values seen in the sample data, and point out columns where the same concept uses different codes in different tables."),
        T("Data Quality", "Temporal Data and Time Zone Consistency",
            "Act as a Data Architect specialised in temporal data. Review date and time columns: types without time zone where events from several regions are stored, dates kept as text or numbers, inconsistent created/updated column names and types, valid-from/valid-to ranges without CHECK constraints or overlap protection, and missing defaults for creation timestamps. Recommend the canonical approach for this engine."),

        // ------------------------------------------------------------------ Security
        T("Security", "Multi-Tenant Isolation and Schema Leakage Analysis",
            "Act as a SaaS Systems Architect. Determine whether the database stores several tenants in shared tables and audit the isolation. Check tenant identifier columns (tenant_id, organization_id, account_id); report tables that lack them, foreign keys that could connect rows of different tenants, and unique constraints that ignore the tenant. Recommend row-level security or schema-per-tenant options for this engine."),
        T("Security", "RBAC and Principle of Least Privilege Violations",
            "Act as a Cybersecurity and Database Auditor. Analyse user, role and permission tables to find weaknesses in the role-based access control model: direct user-to-permission grants, missing role hierarchy constraints, super-user flags and delegation without expiry. Use sample data only as supporting evidence."),
        T("Security", "Sensitive Data (PII/PHI) Encryption at Rest Deficiencies",
            "Assuming the role of a Data Privacy Compliance Specialist, analyse column names and sample data to identify unprotected national identity numbers, health information, passwords, payment data and contact details. Focus on fields that create regulatory risk and propose hashing, encryption, tokenisation or dynamic masking per column, with the features available in this engine."),
        T("Security", "Over-reliance on Client-Side Controls (Business Logic Flaws)",
            "As a Penetration Testing Expert, find numeric and status columns without CHECK constraints that business rules depend on (balances, quantities, prices, discounts, state transitions). Describe concrete abuse scenarios such as negative balances or skipping a workflow state, and give the constraints that close them."),
        T("Security", "Privilege Escalation and Insecure Direct Object Reference (IDOR)",
            "Act as a Security Architecture Analyst. Examine the primary keys of entities that are likely exposed through APIs or URLs. Assess enumeration risk from sequential integer keys and recommend where an additional unguessable public identifier (UUID or random token) should be added, without changing internal joins."),
        T("Security", "Dynamic Procedures Leading to SQL Injection",
            "In the role of an application security reviewer, inspect stored procedures, functions and views for dynamic SQL built by string concatenation, including second-order cases where stored values are later concatenated. For each finding, quote the statement briefly and show the parameterised rewrite (sp_executesql, EXECUTE … USING, prepared statements or format() with quoting functions, depending on the engine).",
            TopicNeeds.Routines | TopicNeeds.CodeDefinitions),
        T("Security", "Double Spending and Race Condition Vulnerabilities",
            "Acting as a Financial Security and Fraud Investigation Expert, analyse wallet, balance, coupon, stock and reservation tables for double spending and race conditions. Check for optimistic concurrency columns (rowversion, version numbers), unique constraints that make operations idempotent, and ledger-style append-only designs. Recommend the locking or constraint strategy per table."),
        T("Security", "Missing Audit Trails and Logging Losses",
            "As an IT Auditor, identify tables that hold sensitive or financial data and check whether changes are traceable: created/updated by and at columns, history or audit tables, temporal tables or triggers. Propose an audit design (system-versioned tables, change data capture or audit triggers) suitable for this engine for the tables that only keep the current state."),
        T("Security", "Unexpected Input Manipulation and Format Bypass (Data Poisoning)",
            "Assuming the role of a Business Logic Vulnerability Researcher, locate columns that require a specific format (e-mail, phone, URL, IP address, IBAN, postal code, JSON) and check for missing length limits, CHECK constraints or domain types. Describe how unvalidated input could poison reports or downstream systems, and give the constraints."),
        T("Security", "Stored Code Quality Review (Procedures, Functions, Triggers, Views)",
            "Act as a Senior Database Developer performing a code review. Review the SQL definitions of the stored procedures, functions, triggers and views. Look for row-by-row (cursor/loop) processing, missing error handling and transactions, SELECT *, unbounded result sets, non-sargable predicates, missing SET NOCOUNT or equivalent, unsafe dynamic SQL and hard-coded values. Quote the offending statement briefly, explain the impact and show a corrected version.",
            TopicNeeds.CodeDefinitions),

        // ------------------------------------------------------------------ Compliance
        T("Compliance", "Personal Data Inventory (GDPR / KVKK Records of Processing)",
            "Act as a Data Protection Officer preparing a GDPR and KVKK (Turkish data protection law) inventory. Produce a table of every column that holds personal data with: table, column, data category (identity, contact, financial, health, location, online identifiers, special category), likely purpose, and the protective measure in place or missing. Finish with the highest-risk tables."),
        T("Compliance", "Right to Erasure Impact Analysis",
            "Act as a Privacy Engineer. Starting from the tables that identify a person (customers, users, employees, contacts), follow the foreign keys and trace everything that would have to be deleted, anonymised or retained for legal reasons when a data subject asks for erasure. Point out cascades that would delete business records that must be kept, and tables without a link that would leave personal data behind. Give an anonymisation approach per table."),
        T("Compliance", "Data Retention (Storage Limitation) under GDPR, KVKK and HIPAA",
            "Act as an IT Law and Regulatory Auditor. Audit the schema against the storage limitation principle. Check for mechanisms that decide when logs, sessions, cancelled transactions and personal data are deleted or anonymised (retention dates, deleted_at markers, partitions that can be dropped), and propose a retention schedule per table group with the regulatory risk of keeping data longer."),
        T("Compliance", "Payment Card Data Scope (PCI DSS)",
            "Act as a PCI DSS Qualified Security Assessor. Identify tables and columns that may store card numbers, expiry dates, card verification codes or bank account data (names, types and sample values). Explain which tables would fall into the cardholder data environment, what must never be stored, and how tokenisation or truncation would shrink the scope.",
            TopicNeeds.SampleData),

        // ------------------------------------------------------------------ Scalability
        T("Scalability", "In-Memory and Horizontal Scalability Bottleneck Detection",
            "Act as a Distributed Systems Architect. Evaluate how well the schema could be sharded or split. Identify tables that are joined to almost everything (scale bottlenecks), candidate shard or partition keys present on the large tables, and cross-entity foreign keys that would block a split."),
        T("Scalability", "Improper Storage Architecture for Time-Series Data",
            "As a Database Design Auditor, identify metric, event and log tables that receive a continuous stream of timestamped inserts. Explain the effect of their current keys and indexes on write throughput and retention, and recommend partitioning, BRIN or columnstore indexes, or a dedicated time-series store where it is justified."),
        T("Scalability", "Platform Migration Risk Assessment",
            "Act as a Cloud Database Migration Architect. Assess the effort and risk of migrating this schema to another engine or a managed cloud service. List the proprietary features in use (data types, identity behaviour, procedural code, engine-specific syntax and constraints) with the equivalent on the most likely targets."),
        T("Scalability", "Business Domain and Microservice Boundary Grouping",
            "Act as a Domain-Driven Design Architect. Group the tables into business domains (for example Identity, Catalogue, Sales, Billing). Based on relationship density, suggest data ownership boundaries for services and list the foreign keys that would cross those boundaries and need an integration pattern instead."),
        T("Scalability", "Partitioning and Archiving Strategy",
            "Act as a Database Capacity Planner. Using the estimated row counts, column names and data types, identify the tables that will dominate storage and I/O. For each, recommend partitioning (name the partition key and scheme), archiving or purging, or compression, explain the trade-offs for this engine, and say clearly when the row counts are too small to justify any change."),

        // ------------------------------------------------------------------ Operations
        T("Operations", "Index and Statistics Maintenance Plan",
            "Act as a production DBA. Write a maintenance plan for this database: which tables and indexes need regular defragmentation, rebuilds or reorganisation, statistics updates, vacuum/analyse or OPTIMIZE, how often, and with which thresholds — based on table sizes, key types and how volatile the tables look. Provide the commands for this engine and a suggested schedule.",
            TopicNeeds.Indexes),
        T("Operations", "Online Schema Change Safety",
            "Act as a DBA reviewing upcoming migrations. For the largest and most connected tables, explain which common changes (adding a NOT NULL column, changing a type, adding an index or foreign key, renaming) would lock or rewrite the table on this engine, and give the online-safe procedure for each (online index builds, CONCURRENTLY, NOT VALID constraints, expand-and-contract, online schema change tools)."),
        T("Operations", "Monitoring Query Pack",
            "Act as a Site Reliability Engineer. Write a pack of ready-to-run monitoring queries for this engine tailored to this schema: table and index size growth for the largest tables, unused and missing indexes, blocking and long-running queries, lock waits, and bloat or fragmentation. For each query, say what to watch and the alert threshold you would use."),
        T("Operations", "Backup and Restore Strategy by Data Criticality",
            "Act as a Data Platform Engineer. Classify the tables into transactional data, reference data, logs/telemetry and derived data based on names, relationships and sizes. Recommend recovery point and recovery time objectives per class, backup frequency and type for this engine, which tables could be excluded or restored separately, and how to verify restores."),

        // ------------------------------------------------------------------ Developer
        T("Developer", "Automated Data Dictionary Documentation",
            "Act as a Technical Writer and Database Administrator. Generate a professional data dictionary in Markdown. For each table, describe its business purpose from its name, columns and relationships, then list the critical columns with their role, type and constraints, so a new developer can use it immediately."),
        T("Developer", "Developer Onboarding Guide",
            "Act as the lead engineer onboarding a new developer. Explain this database in plain language: the business domain it serves, the core entities and how they relate, the life cycle of the most important records, the conventions in use and the pitfalls to know (soft deletes, denormalised copies, unusual keys). Include a small text diagram of the core relationships."),
        T("Developer", "Common Query Cookbook",
            "Act as a senior SQL developer. Write the 10 most useful queries a developer or analyst will need for this schema (for example an order with its customer and lines, top customers, recent activity, integrity checks). Use only real tables and columns, join along the foreign keys, write correct SQL for this engine and add a one-line explanation and the index each query relies on."),
        T("Developer", "Entity Model Generation (EF Core and TypeScript)",
            "Act as a backend developer. For the 10 most central tables, generate C# entity classes for Entity Framework Core (with keys, required fields, max lengths and navigation properties matching the foreign keys) and matching TypeScript interfaces. Keep names in the language's conventions and mention mapping attributes where the database names differ."),
        T("Developer", "Realistic Test Data Script",
            "Act as a QA engineer. Write an INSERT script for this engine that fills the core tables with a small, realistic and internally consistent data set (around 5–20 rows per table), inserting parents before children so every foreign key, NOT NULL and CHECK constraint is satisfied. Never copy real values from the sample data; invent clearly fictional ones."),
        T("Developer", "API Resource Design (REST)",
            "Act as an API architect. Derive a REST resource model from this schema: aggregates and their root resources, nested sub-resources, the endpoints with methods, filtering and paging parameters backed by existing indexes, and the fields that must never be exposed (secrets, internal flags, personal data). Present it as a concise endpoint table."),
    };
}
