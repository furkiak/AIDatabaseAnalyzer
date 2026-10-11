using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>The built-in analysis questions, grouped into categories the user can filter by.</summary>
public static class TopicCatalog
{
    public static readonly string[] Categories = { "Performance", "Design", "Security", "Scalability" };

    public static List<AnalysisTopic> CreateDefaults() => new()
    {
        new AnalysisTopic
        {
            Title = "Non-SARGable Query Predicates and Missing Index Detection",
            Category = "Performance",
            Prompt = "Act as a Senior Database Performance Expert. Analyze the provided JSON schema to find missing index vulnerabilities and potentially non-SARGable structural designs. Contextualize by evaluating all table relationships, primary key assignments, and existing indexes; specifically identify columns with JOIN potential that lack indexes. As a strict constraint, only list high-cardinality columns suitable for B-Tree indexing and avoid unnecessary recommendations."
        },
        new AnalysisTopic
        {
            Title = "Over-indexing and Overlapping Index Costs",
            Category = "Performance",
            Prompt = "Assume the role of a Database Architecture Auditor. Identify over-indexed tables and overlapping, redundant composite indexes within the JSON schema. Estimate data volatility by examining random data samples and evaluate the storage waste caused by redundant indexes. Output the results in a structured table format clearly reporting which indexes should be dropped or merged."
        },
        new AnalysisTopic
        {
            Title = "Trigger-Induced Performance Bottlenecks and Deadlocks",
            Category = "Performance",
            Prompt = "Acting as an SQL Performance Tuning Engineer, analyze the trigger logic and business rules within the schema. Focus on concurrent updates to external tables by triggers executing during DML operations. Identify structures causing logical loops and extended processing times, and pinpoint business logic that should be shifted to asynchronous processes or the application layer."
        },
        new AnalysisTopic
        {
            Title = "Hidden Complexity in View Layers and Transient Data Load",
            Category = "Performance",
            Prompt = "Assuming the role of an Enterprise Data Warehouse Architect, parse the View definitions to audit excessive nesting and complex join structures. Considering that subqueries or cross-view invocations cause performance degradation, analyze whether these logics can be replaced with more efficient Common Table Expressions (CTEs) or materialized views."
        },
        new AnalysisTopic
        {
            Title = "Foreign Key Index Deficiency and Hidden Table Scans",
            Category = "Performance",
            Prompt = "As a Database Performance Expert, scan all foreign key constraints and verify the existence of matching indexes. Considering that unindexed foreign keys are one of the most common hidden bottlenecks causing full table scans during cascading deletes, report which foreign keys urgently require indexing."
        },
        new AnalysisTopic
        {
            Title = "Execution Plan Simulation and Cost Estimation",
            Category = "Performance",
            Prompt = "Acting as a Query Optimization Engineer, construct three critical query scenarios assumed to fetch the most data, based on table relationships and random data samples. Mentally simulate how the database engine will execute them (e.g., Hash Match, Nested Loops, Table Scan). Predict execution bottlenecks and provide query rewrite or structural configuration recommendations to reduce costs."
        },
        new AnalysisTopic
        {
            Title = "Collation Mismatches and Implicit Conversion",
            Category = "Performance",
            Prompt = "As a Database Internationalization and Performance Expert, examine the column properties to detect Collation mismatches between joined tables. Acknowledging that mismatched structures cause implicit conversions and invalidate indexes, focus solely on character-based data bindings to generate standardization strategies."
        },
        new AnalysisTopic
        {
            Title = "Connection Pool Exhaustion and Metadata Impacts",
            Category = "Performance",
            Prompt = "In the role of a Site Reliability Engineer, analyze the metadata configurations, session controls in triggers, and temporary table creation commands. Identify architectural flaws that could bloat the connection pool or cause data residue across sessions."
        },
        new AnalysisTopic
        {
            Title = "Heap Table and Primary Key Analysis",
            Category = "Performance",
            Prompt = "Act as a Database Architecture Expert. Identify tables in the schema that lack a 'Primary Key' or 'Clustered Index' (Heap tables). Explain the performance and fragmentation risks of Heap tables, and recommend appropriate columns to serve as primary keys based on their naming patterns and data types."
        },
        new AnalysisTopic
        {
            Title = "Table Width and Page Fragmentation Risk",
            Category = "Performance",
            Prompt = "Act as a SQL Server Storage Specialist. Analyze the 'MaxLen' properties of columns per table to identify potential row-chaining and page-splitting risks. Flag tables with multiple wide columns (e.g., several VARCHAR(4000) or heavily sized fields) where the combined row size is highly likely to exceed the 8KB physical page limit."
        },
        new AnalysisTopic
        {
            Title = "Bloated Records and Data Type Wastage",
            Category = "Design",
            Prompt = "As a Data Modeling Expert, examine the data schema to identify columns with unnecessarily large data types and bloated record architectures. Compare existing column data types against three random data samples to find wastage (e.g., a 255-character type for a boolean value). Develop recommendations while considering the potential risks of data truncation caused by data type narrowing operations."
        },
        new AnalysisTopic
        {
            Title = "Normalization Violations and Data Redundancy",
            Category = "Design",
            Prompt = "Act as a Database Design Auditor. Using the schema and sample data, detect violations of fundamental normalization forms, particularly the Third Normal Form (3NF). Find anomalies related to repeating data in columns and check for transitive dependencies within rows using the sample data. Constrain your analysis to deduce from structural relationships whether the violation is a design flaw or a conscious, read-optimized choice."
        },
        new AnalysisTopic
        {
            Title = "Poorly Designed Denormalization and Loss of Data Integrity",
            Category = "Design",
            Prompt = "Assume the role of a Data Modeling Architect. Examine the denormalized structures and investigate how these duplicated data points are kept synchronized (e.g., via triggers or application layer). Scenario-test data discrepancies that would occur if the duplicated data is not updated. List the lack of database-level consistency guarantees for the denormalized fields."
        },
        new AnalysisTopic
        {
            Title = "Orphan Records and Missing Relational Integrity",
            Category = "Design",
            Prompt = "Acting as a Data Integrity Specialist, identify tables that should be logically related but lack physical foreign key constraints. Find missing links by analyzing similarities in column names and matches in data samples. Constrain your output to report only high-confidence relationship matches."
        },
        new AnalysisTopic
        {
            Title = "Circular Dependency and Infinite Loops",
            Category = "Design",
            Prompt = "As a Solutions Architect, map out circular dependency routing in the database architecture based on relationships and triggers. Analyze whether hierarchical or relational designs create a loop and evaluate the possibility of recursive trigger execution. Use graph-based network analysis logic to list the routes step-by-step."
        },
        new AnalysisTopic
        {
            Title = "ORM Anti-Patterns: Schema Designs Prone to N+1 Queries",
            Category = "Design",
            Prompt = "Assuming the role of a Backend Integration Expert, examine the relational integrity and data fragmentation to identify table designs that could cause the N+1 query problem in ORM usage. By calculating JOIN costs from the sample data structure, determine if frequently co-queried data is illogically fragmented into sub-tables."
        },
        new AnalysisTopic
        {
            Title = "Recursive CTE Misusage and Depth Overflows",
            Category = "Design",
            Prompt = "In the role of a Site Reliability Engineer, identify self-referencing columns in the schema and analyze whether there are hierarchy limitations. Investigate the absence of depth constraints that would prevent recursive queries from running uncontrollably, and determine performance boundaries by classifying the tree structures."
        },
        new AnalysisTopic
        {
            Title = "Siloed and Fragmented Schema Modules",
            Category = "Design",
            Prompt = "Act as an Enterprise Architecture Lead. Group all tables into modules and identify isolated, non-integrated database silos. Constrain your analysis to a Domain-Driven Design (DDD) context to determine if similar data is copied in an unrelated manner using different logic."
        },
        new AnalysisTopic
        {
            Title = "Polymorphic Relationship Flaws and Loss of Referential Integrity",
            Category = "Design",
            Prompt = "Acting as a Data Modeling Expert, examine structures created with dual-column combinations like entity_id and entity_type (polymorphic relationships). Demonstrate how these structures can lead to orphan records due to the lack of classic foreign key constraints, and design solutions using database-level CHECK constraints or triggers to ensure integrity."
        },
        new AnalysisTopic
        {
            Title = "Data Type Inconsistency Analysis",
            Category = "Design",
            Prompt = "Assume the role of a Data Quality Analyst. Search for columns with the identical conceptual names across different tables (e.g., 'id', 'user_id', 'status') that have conflicting 'DataType' or 'MaxLen' properties. Explain the performance degradation risks, such as 'implicit conversion' during JOIN operations, caused by these mismatches."
        },
        new AnalysisTopic
        {
            Title = "Identity Column Capacity Audit",
            Category = "Design",
            Prompt = "In the role of a Database Capacity Planner, check for 'Identity' or auto-increment patterns within 'DataType' definitions. Identify tables using structurally small numeric types like 'TINYINT' or 'SMALLINT' for primary keys or highly active sequential fields. Warn about the risk of capacity exhaustion and application downtime."
        },
        new AnalysisTopic
        {
            Title = "Naming Standards and Consistency",
            Category = "Design",
            Prompt = "Assume the role of a Database Code Reviewer. Review the naming conventions across all Tables, Columns, and Indexes within the JSON. Identify inconsistencies (e.g., mixing CamelCase, PascalCase, and snake_case) and suggest a unified, standardized naming strategy to improve schema maintainability."
        },
        new AnalysisTopic
        {
            Title = "Automated Data Dictionary Documentation",
            Category = "Design",
            Prompt = "Act as a Technical Writer and Database Administrator. Generate a professional Data Dictionary in Markdown format based on the schema. For each table, describe its business purpose based on its naming context. Briefly explain the role, data type, and logical constraints of critical columns to serve as immediate developer documentation."
        },
        new AnalysisTopic
        {
            Title = "Soft Delete Pattern Consistency",
            Category = "Design",
            Prompt = "Assume the role of a Software Architecture Auditor. Search for 'Soft Delete' patterns in column metadata (e.g., 'IsDeleted', 'IsActive', 'StatusID'). Identify inconsistencies where some core business tables implement this pattern while others do not. Detail the risks of reporting inaccuracies and data state conflicts caused by non-uniform deletion logic."
        },
        new AnalysisTopic
        {
            Title = "Excessive Nullability and Constraint Audit",
            Category = "Design",
            Prompt = "Act as a Data Integrity Specialist. Review the nullability configuration of the schema's columns. Identify tables where an excessive percentage of business-critical columns allow NULL values without corresponding 'Check Constraints' or 'Default Values'. Explain how this architectural leniency degrades data quality and pushes validation burdens entirely to the frontend."
        },
        new AnalysisTopic
        {
            Title = "Referential Actions and Cascade Risk Review",
            Category = "Design",
            Prompt = "Assume the role of a Data Integrity Architect. Review the ON DELETE / ON UPDATE behavior of every foreign key listed in the schema (a missing action means NO ACTION). Identify dangerous ON DELETE CASCADE chains that could silently delete large amounts of data, places where SET NULL conflicts with NOT NULL columns, and relationships that should use soft delete or RESTRICT instead. Present the findings as a table with the foreign key, the current action, the risk and the recommended action."
        },
        new AnalysisTopic
        {
            Title = "Ready-to-Run DDL Fix Script",
            Category = "Design",
            Prompt = "Act as a Senior DBA who prepares a safe migration. Based on the schema, write a single reviewed SQL script for this database engine that fixes the most valuable and least risky problems: missing primary keys, missing indexes on foreign keys, redundant indexes, and obviously wrong data types. Group the statements by risk (safe / needs testing / needs a maintenance window), add a short comment above each statement explaining why, and make the script idempotent where the engine allows it. Do not touch objects you cannot see in the schema, and begin with a warning to run it on a copy first."
        },
        new AnalysisTopic
        {
            Title = "Multi-Tenant Isolation and Schema Leakage Analysis",
            Category = "Security",
            Prompt = "Act as a SaaS Systems Architect. Determine whether the database uses a multi-tenant shared data pool approach and audit its isolation logic. Examine the presence of tenant identifier columns (e.g., tenant_id); report tables lacking these columns or isolation policies as risks for cross-tenant data leakage."
        },
        new AnalysisTopic
        {
            Title = "RBAC and Principle of Least Privilege Violations",
            Category = "Security",
            Prompt = "Act as a Cybersecurity and Database Auditor. Analyze the privilege, role, and permission tables to find vulnerabilities in the Role-Based Access Control (RBAC) logic. Detect erroneous associations granting standard users direct access to critical system tables, analyze the delegation logic from sample data, and verify the implementation of the Principle of Least Privilege."
        },
        new AnalysisTopic
        {
            Title = "Sensitive Data (PII/PHI) Encryption at Rest Deficiencies",
            Category = "Security",
            Prompt = "Assuming the role of a Data Privacy Compliance Specialist, analyze column names and sample data to identify unmasked identification numbers, health codes, passwords, or financial data. Utilizing Named Entity Recognition (NER) logic, focus on fields that expose the company to regulatory risks and propose dynamic data masking and encryption strategies."
        },
        new AnalysisTopic
        {
            Title = "Over-reliance on Client-Side Controls (Business Logic Flaws)",
            Category = "Security",
            Prompt = "As a Penetration Testing Expert, identify the lack of CHECK constraints in numeric and status-based columns to analyze 'Over-reliance on Client Controls' vulnerabilities. Question the database controls against scenarios like entering negative balances or bypassing valid statuses in e-commerce workflows."
        },
        new AnalysisTopic
        {
            Title = "Privilege Escalation and Insecure Direct Object Reference (IDOR)",
            Category = "Security",
            Prompt = "Act as a Security Architecture Analyst. Examine the primary keys used in the schema to identify enumeration strategies. Evaluate whether externally accessible services utilize sequential integers (auto-increment) and assess the risk posed by the absence of unpredictable structures like UUIDs or GUIDs."
        },
        new AnalysisTopic
        {
            Title = "Dynamic Procedures Leading to SQL Injection",
            Category = "Security",
            Prompt = "In the role of a Vulnerability Researcher, inspect stored procedures, dynamic query constructs, and view designs for Second-Order SQL Injection risks. Identify structures that allow unsanitized input to be executed directly, and report on how these processes can be refactored using parameterized queries."
        },
        new AnalysisTopic
        {
            Title = "Double Spending and Race Condition Vulnerabilities",
            Category = "Security",
            Prompt = "Acting as a Financial Security and Fraud Investigation Expert, analyze wallet, coupon, or inventory tables to pinpoint exposures to Double Spending and Race Condition vulnerabilities. Constrain the model to specifically audit for the presence of Optimistic or Pessimistic Locking mechanisms, such as version number columns."
        },
        new AnalysisTopic
        {
            Title = "Missing Audit Trails and Logging Losses",
            Category = "Security",
            Prompt = "As an IT Auditor, identify tables housing sensitive data and analyze the absence of Audit Trail histories tracking their modifications. Check for auditing columns like created_at or updated_by, and propose a Change Data Capture (CDC) architecture for entities that only retain the most current state."
        },
        new AnalysisTopic
        {
            Title = "Data Retention (Storage Limitation) Violations under GDPR and HIPAA",
            Category = "Security",
            Prompt = "Act as an IT Law and Regulatory Auditor. Audit the schema against data limitation principles (Storage Limitation). Check for the absence of mechanisms (e.g., Time to Live, soft_delete markers) dictating when data like user logs or canceled transactions should be deleted or anonymized, and contextualize with regulatory penalty risks."
        },
        new AnalysisTopic
        {
            Title = "Unexpected Input Manipulation and Format Bypass (Data Poisoning)",
            Category = "Security",
            Prompt = "Assuming the role of a Business Logic Vulnerability Researcher, locate tables storing data requiring specific formats (e.g., email, phone, IP addresses) and analyze the lack of format validation constraints (Regex/Domain validation). Detail Data Poisoning scenarios that could arise from over-reliance on frontend validation."
        },
        new AnalysisTopic
        {
            Title = "AI Model Collapse and Synthetic Data Pollution Detection",
            Category = "Security",
            Prompt = "Act as an AI Synthetic Data Specialist. Linguistically and structurally analyze the random data samples to test for synthetic data pollution generated by LLMs and assess their suitability for model training. Scan the sample data for characteristic patterns of LLM-generated text, such as excessive perfection or cliché phrasing."
        },
        new AnalysisTopic
        {
            Title = "Stored Code Quality Review (Procedures, Functions, Triggers, Views)",
            Category = "Security",
            Prompt = "Act as a Senior Database Developer performing a code review. Review the SQL definitions of the stored procedures, functions, triggers and views in the schema (the 'Definition' fields). Look for row-by-row (cursor/loop) processing, missing error handling and transactions, SELECT *, unbounded result sets, non-sargable predicates, missing SET NOCOUNT or equivalent, unsafe dynamic SQL and hard-coded values. Quote the offending statement briefly, explain the impact and show a corrected version. If no definitions are present, say that the code could not be reviewed."
        },
        new AnalysisTopic
        {
            Title = "In-Memory and Horizontal Scalability Bottleneck Detection",
            Category = "Scalability",
            Prompt = "Act as a Distributed Systems Architect. Evaluate the database's horizontal scalability capacity by analyzing table structures and foreign key bindings. Given that heavily concentrated foreign keys prevent migration to a microservices architecture, identify which tables possess columns suitable for use as partition keys."
        },
        new AnalysisTopic
        {
            Title = "Improper Storage Architecture for Time-Series Data",
            Category = "Scalability",
            Prompt = "As a Database Design Auditor, identify metric or log tables prone to receiving thousands of inserts per second with timestamps. Explain how storing these tables with B-Tree indexes within a relational schema will cripple write performance, and provide suitable data store migration recommendations."
        },
        new AnalysisTopic
        {
            Title = "Platform Migration Risk Assessment",
            Category = "Scalability",
            Prompt = "Act as a Cloud Database Migration Architect. Assess the risk of migrating this schema to open-source platforms like PostgreSQL or cloud-native DBs. Identify specific proprietary dependencies (e.g., specialized data types, unique trigger logic, or engine-specific constraints) that would complicate a cross-platform migration."
        },
        new AnalysisTopic
        {
            Title = "Business Domain and Microservice Boundary Grouping",
            Category = "Scalability",
            Prompt = "Act as a Domain-Driven Design (DDD) Architect. Analyze the entire JSON schema and conceptually group the tables into logical business domains (e.g., Core, Sales, Identity, Billing). Based on table clusters and relationship density, suggest logical data boundaries for migrating this monolithic schema into a Microservices architecture."
        },
        new AnalysisTopic
        {
            Title = "Partitioning and Archiving Strategy",
            Category = "Scalability",
            Prompt = "Act as a Database Capacity Planner. Using the estimated row counts ('Rows'), column names and data types, identify the tables that will dominate storage and I/O. For each, recommend whether to use table partitioning (name the partition key, e.g. a date or tenant column, and the partition scheme), archiving/purging, or compression. Explain the trade-offs for this specific database engine and state clearly when the row counts are too small to justify any change."
        },
    };
}
