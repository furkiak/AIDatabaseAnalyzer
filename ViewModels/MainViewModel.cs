using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services;
using static AIDatabaseAnalyzer.Services.DbSchemaService;

namespace AIDatabaseAnalyzer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DbSchemaService _dbService;
    private readonly ILlmService _llmService;
    private readonly PdfReportService _pdfService;

    [ObservableProperty] private string serverAddress = @"DESKTOP-PC\SQLEXPRESS";
    [ObservableProperty] private string port = "";
    [ObservableProperty] private string username = "sa";
    [ObservableProperty] private string password = "123456";
    [ObservableProperty] private int selectedEngineIndex = 0;


    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isConnectionCardVisible = true;
    [ObservableProperty] private bool isFiltersCardVisible = false;
    [ObservableProperty] private bool isSchemaPreviewCardVisible = false;
    [ObservableProperty] private bool isQuestionsCardVisible = false;
    [ObservableProperty] private bool isAnalysisProgressCardVisible = false;
    [ObservableProperty] private bool isAnalysisRunning;
    [ObservableProperty] private string currentTaskText = "Starting...";

    public ObservableCollection<string> Databases { get; } = new();
    public ObservableCollection<ComponentSelection> Components { get; } = new();

    public ObservableCollection<TableSelection> Tables { get; } = new();
    public ObservableCollection<TableSelection> FilteredTables { get; } = new();
    [ObservableProperty] private string searchQuery;
    [ObservableProperty] private string selectedTableText = "0 / 0 tables selected";

    [ObservableProperty] private ComponentSelection sampleDataSelection;

    [ObservableProperty] private string extractedJsonSchema;
    [ObservableProperty] private string schemaStatsText;
    [ObservableProperty] private Color schemaStatsColor = Color.FromArgb("#212529");
    [ObservableProperty] private string customQuestionText;

    public ObservableCollection<AnalysisTopic> Topics { get; } = new();
    public ObservableCollection<AnalysisTopic> SelectedTopicsForAnalysis { get; } = new();
    [ObservableProperty] private string selectedTopicText = "0 / 0 topics selected";

    private string _selectedDatabase;
    public string SelectedDatabase
    {
        get => _selectedDatabase;
        set
        {
            if (SetProperty(ref _selectedDatabase, value) && !string.IsNullOrEmpty(value))
                Task.Run(async () => await LoadTablesAsync(value));
        }
    }

    public string BaseConnectionString { get; private set; }
    public DbEngine SelectedEngine { get; private set; }

    public MainViewModel(DbSchemaService dbService, ILlmService llmService, PdfReportService pdfService)
    {
        _dbService = dbService;
        _llmService = llmService;
        _pdfService = pdfService;
        InitializeComponents();
        LoadDefaultTopics();
    }

    private void InitializeComponents()
    {
        var comps = new[] { "Views", "Indexes", "Triggers", "Stored Procedures", "Constraints" };
        foreach (var c in comps) Components.Add(new ComponentSelection { Name = c, IsSelected = true });
        SampleDataSelection = new ComponentSelection { Name = "Fetch 3 Random Rows per Table", IsSelected = false };
    }

    private void LoadDefaultTopics()
    {
        Topics.Add(new AnalysisTopic
        {
            Title = "Non-SARGable Query Predicates and Missing Index Detection",
            Prompt = "Act as a Senior Database Performance Expert. Analyze the provided JSON schema to find missing index vulnerabilities and potentially non-SARGable structural designs. Contextualize by evaluating all table relationships, primary key assignments, and existing indexes; specifically identify columns with JOIN potential that lack indexes. As a strict constraint, only list high-cardinality columns suitable for B-Tree indexing and avoid unnecessary recommendations."
        });

        Topics.Add(new AnalysisTopic
        {
            Title = "Over-indexing and Overlapping Index Costs",
            Prompt = "Assume the role of a Database Architecture Auditor. Identify over-indexed tables and overlapping, redundant composite indexes within the JSON schema. Estimate data volatility by examining random data samples and evaluate the storage waste caused by redundant indexes. Output the results in a structured table format clearly reporting which indexes should be dropped or merged."
        });

  
        Topics.Add(new AnalysisTopic
        {
            Title = "Trigger-Induced Performance Bottlenecks and Deadlocks",
            Prompt = "Acting as an SQL Performance Tuning Engineer, analyze the trigger logic and business rules within the schema. Focus on concurrent updates to external tables by triggers executing during DML operations. Identify structures causing logical loops and extended processing times, and pinpoint business logic that should be shifted to asynchronous processes or the application layer."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Bloated Records and Data Type Wastage",
            Prompt = "As a Data Modeling Expert, examine the data schema to identify columns with unnecessarily large data types and bloated record architectures. Compare existing column data types against three random data samples to find wastage (e.g., a 255-character type for a boolean value). Develop recommendations while considering the potential risks of data truncation caused by data type narrowing operations."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Hidden Complexity in View Layers and Transient Data Load",
            Prompt = "Assuming the role of an Enterprise Data Warehouse Architect, parse the View definitions to audit excessive nesting and complex join structures. Considering that subqueries or cross-view invocations cause performance degradation, analyze whether these logics can be replaced with more efficient Common Table Expressions (CTEs) or materialized views."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "In-Memory and Horizontal Scalability Bottleneck Detection",
            Prompt = "Act as a Distributed Systems Architect. Evaluate the database's horizontal scalability capacity by analyzing table structures and foreign key bindings. Given that heavily concentrated foreign keys prevent migration to a microservices architecture, identify which tables possess columns suitable for use as partition keys."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Foreign Key Index Deficiency and Hidden Table Scans",
            Prompt = "As a Database Performance Expert, scan all foreign key constraints and verify the existence of matching indexes. Considering that unindexed foreign keys are one of the most common hidden bottlenecks causing full table scans during cascading deletes, report which foreign keys urgently require indexing."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Execution Plan Simulation and Cost Estimation",
            Prompt = "Acting as a Query Optimization Engineer, construct three critical query scenarios assumed to fetch the most data, based on table relationships and random data samples. Mentally simulate how the database engine will execute them (e.g., Hash Match, Nested Loops, Table Scan). Predict execution bottlenecks and provide query rewrite or structural configuration recommendations to reduce costs."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Collation Mismatches and Implicit Conversion",
            Prompt = "As a Database Internationalization and Performance Expert, examine the column properties to detect Collation mismatches between joined tables. Acknowledging that mismatched structures cause implicit conversions and invalidate indexes, focus solely on character-based data bindings to generate standardization strategies."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Connection Pool Exhaustion and Metadata Impacts",
            Prompt = "In the role of a Site Reliability Engineer, analyze the metadata configurations, session controls in triggers, and temporary table creation commands. Identify architectural flaws that could bloat the connection pool or cause data residue across sessions."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Normalization Violations and Data Redundancy",
            Prompt = "Act as a Database Design Auditor. Using the schema and sample data, detect violations of fundamental normalization forms, particularly the Third Normal Form (3NF). Find anomalies related to repeating data in columns and check for transitive dependencies within rows using the sample data. Constrain your analysis to deduce from structural relationships whether the violation is a design flaw or a conscious, read-optimized choice."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Poorly Designed Denormalization and Loss of Data Integrity",
            Prompt = "Assume the role of a Data Modeling Architect. Examine the denormalized structures and investigate how these duplicated data points are kept synchronized (e.g., via triggers or application layer). Scenario-test data discrepancies that would occur if the duplicated data is not updated. List the lack of database-level consistency guarantees for the denormalized fields."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Orphan Records and Missing Relational Integrity",
            Prompt = "Acting as a Data Integrity Specialist, identify tables that should be logically related but lack physical foreign key constraints. Find missing links by analyzing similarities in column names and matches in data samples. Constrain your output to report only high-confidence relationship matches."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Circular Dependency and Infinite Loops",
            Prompt = "As a Solutions Architect, map out circular dependency routing in the database architecture based on relationships and triggers. Analyze whether hierarchical or relational designs create a loop and evaluate the possibility of recursive trigger execution. Use graph-based network analysis logic to list the routes step-by-step."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Multi-Tenant Isolation and Schema Leakage Analysis",
            Prompt = "Act as a SaaS Systems Architect. Determine whether the database uses a multi-tenant shared data pool approach and audit its isolation logic. Examine the presence of tenant identifier columns (e.g., tenant_id); report tables lacking these columns or isolation policies as risks for cross-tenant data leakage."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "ORM Anti-Patterns: Schema Designs Prone to N+1 Queries",
            Prompt = "Assuming the role of a Backend Integration Expert, examine the relational integrity and data fragmentation to identify table designs that could cause the N+1 query problem in ORM usage. By calculating JOIN costs from the sample data structure, determine if frequently co-queried data is illogically fragmented into sub-tables."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Recursive CTE Misusage and Depth Overflows",
            Prompt = "In the role of a Site Reliability Engineer, identify self-referencing columns in the schema and analyze whether there are hierarchy limitations. Investigate the absence of depth constraints that would prevent recursive queries from running uncontrollably, and determine performance boundaries by classifying the tree structures."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Siloed and Fragmented Schema Modules",
            Prompt = "Act as an Enterprise Architecture Lead. Group all tables into modules and identify isolated, non-integrated database silos. Constrain your analysis to a Domain-Driven Design (DDD) context to determine if similar data is copied in an unrelated manner using different logic."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Improper Storage Architecture for Time-Series Data",
            Prompt = "As a Database Design Auditor, identify metric or log tables prone to receiving thousands of inserts per second with timestamps. Explain how storing these tables with B-Tree indexes within a relational schema will cripple write performance, and provide suitable data store migration recommendations."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Polymorphic Relationship Flaws and Loss of Referential Integrity",
            Prompt = "Acting as a Data Modeling Expert, examine structures created with dual-column combinations like entity_id and entity_type (polymorphic relationships). Demonstrate how these structures can lead to orphan records due to the lack of classic foreign key constraints, and design solutions using database-level CHECK constraints or triggers to ensure integrity."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "RBAC and Principle of Least Privilege Violations",
            Prompt = "Act as a Cybersecurity and Database Auditor. Analyze the privilege, role, and permission tables to find vulnerabilities in the Role-Based Access Control (RBAC) logic. Detect erroneous associations granting standard users direct access to critical system tables, analyze the delegation logic from sample data, and verify the implementation of the Principle of Least Privilege."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Sensitive Data (PII/PHI) Encryption at Rest Deficiencies",
            Prompt = "Assuming the role of a Data Privacy Compliance Specialist, analyze column names and sample data to identify unmasked identification numbers, health codes, passwords, or financial data. Utilizing Named Entity Recognition (NER) logic, focus on fields that expose the company to regulatory risks and propose dynamic data masking and encryption strategies."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Over-reliance on Client-Side Controls (Business Logic Flaws)",
            Prompt = "As a Penetration Testing Expert, identify the lack of CHECK constraints in numeric and status-based columns to analyze 'Over-reliance on Client Controls' vulnerabilities. Question the database controls against scenarios like entering negative balances or bypassing valid statuses in e-commerce workflows."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Privilege Escalation and Insecure Direct Object Reference (IDOR)",
            Prompt = "Act as a Security Architecture Analyst. Examine the primary keys used in the schema to identify enumeration strategies. Evaluate whether externally accessible services utilize sequential integers (auto-increment) and assess the risk posed by the absence of unpredictable structures like UUIDs or GUIDs."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Dynamic Procedures Leading to SQL Injection",
            Prompt = "In the role of a Vulnerability Researcher, inspect stored procedures, dynamic query constructs, and view designs for Second-Order SQL Injection risks. Identify structures that allow unsanitized input to be executed directly, and report on how these processes can be refactored using parameterized queries."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Double Spending and Race Condition Vulnerabilities",
            Prompt = "Acting as a Financial Security and Fraud Investigation Expert, analyze wallet, coupon, or inventory tables to pinpoint exposures to Double Spending and Race Condition vulnerabilities. Constrain the model to specifically audit for the presence of Optimistic or Pessimistic Locking mechanisms, such as version number columns."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Missing Audit Trails and Logging Losses",
            Prompt = "As an IT Auditor, identify tables housing sensitive data and analyze the absence of Audit Trail histories tracking their modifications. Check for auditing columns like created_at or updated_by, and propose a Change Data Capture (CDC) architecture for entities that only retain the most current state."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Data Retention (Storage Limitation) Violations under GDPR and HIPAA",
            Prompt = "Act as an IT Law and Regulatory Auditor. Audit the schema against data limitation principles (Storage Limitation). Check for the absence of mechanisms (e.g., Time to Live, soft_delete markers) dictating when data like user logs or canceled transactions should be deleted or anonymized, and contextualize with regulatory penalty risks."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Unexpected Input Manipulation and Format Bypass (Data Poisoning)",
            Prompt = "Assuming the role of a Business Logic Vulnerability Researcher, locate tables storing data requiring specific formats (e.g., email, phone, IP addresses) and analyze the lack of format validation constraints (Regex/Domain validation). Detail Data Poisoning scenarios that could arise from over-reliance on frontend validation."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "AI Model Collapse and Synthetic Data Pollution Detection",
            Prompt = "Act as an AI Synthetic Data Specialist. Linguistically and structurally analyze the random data samples to test for synthetic data pollution generated by LLMs and assess their suitability for model training. Scan the sample data for characteristic patterns of LLM-generated text, such as excessive perfection or cliché phrasing."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Heap Table and Primary Key Analysis",
            Prompt = "Act as a Database Architecture Expert. Identify tables in the schema that lack a 'Primary Key' or 'Clustered Index' (Heap tables). Explain the performance and fragmentation risks of Heap tables, and recommend appropriate columns to serve as primary keys based on their naming patterns and data types."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Data Type Inconsistency Analysis",
            Prompt = "Assume the role of a Data Quality Analyst. Search for columns with the identical conceptual names across different tables (e.g., 'id', 'user_id', 'status') that have conflicting 'DataType' or 'MaxLen' properties. Explain the performance degradation risks, such as 'implicit conversion' during JOIN operations, caused by these mismatches."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Table Width and Page Fragmentation Risk",
            Prompt = "Act as a SQL Server Storage Specialist. Analyze the 'MaxLen' properties of columns per table to identify potential row-chaining and page-splitting risks. Flag tables with multiple wide columns (e.g., several VARCHAR(4000) or heavily sized fields) where the combined row size is highly likely to exceed the 8KB physical page limit."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Identity Column Capacity Audit",
            Prompt = "In the role of a Database Capacity Planner, check for 'Identity' or auto-increment patterns within 'DataType' definitions. Identify tables using structurally small numeric types like 'TINYINT' or 'SMALLINT' for primary keys or highly active sequential fields. Warn about the risk of capacity exhaustion and application downtime."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Platform Migration Risk Assessment",
            Prompt = "Act as a Cloud Database Migration Architect. Assess the risk of migrating this schema to open-source platforms like PostgreSQL or cloud-native DBs. Identify specific proprietary dependencies (e.g., specialized data types, unique trigger logic, or engine-specific constraints) that would complicate a cross-platform migration."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Naming Standards and Consistency",
            Prompt = "Assume the role of a Database Code Reviewer. Review the naming conventions across all Tables, Columns, and Indexes within the JSON. Identify inconsistencies (e.g., mixing CamelCase, PascalCase, and snake_case) and suggest a unified, standardized naming strategy to improve schema maintainability."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Automated Data Dictionary Documentation",
            Prompt = "Act as a Technical Writer and Database Administrator. Generate a professional Data Dictionary in Markdown format based on the schema. For each table, describe its business purpose based on its naming context. Briefly explain the role, data type, and logical constraints of critical columns to serve as immediate developer documentation."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Business Domain and Microservice Boundary Grouping",
            Prompt = "Act as a Domain-Driven Design (DDD) Architect. Analyze the entire JSON schema and conceptually group the tables into logical business domains (e.g., Core, Sales, Identity, Billing). Based on table clusters and relationship density, suggest logical data boundaries for migrating this monolithic schema into a Microservices architecture."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Soft Delete Pattern Consistency",
            Prompt = "Assume the role of a Software Architecture Auditor. Search for 'Soft Delete' patterns in column metadata (e.g., 'IsDeleted', 'IsActive', 'StatusID'). Identify inconsistencies where some core business tables implement this pattern while others do not. Detail the risks of reporting inaccuracies and data state conflicts caused by non-uniform deletion logic."
        });


        Topics.Add(new AnalysisTopic
        {
            Title = "Excessive Nullability and Constraint Audit",
            Prompt = "Act as a Data Integrity Specialist. Review the nullability configuration of the schema's columns. Identify tables where an excessive percentage of business-critical columns allow NULL values without corresponding 'Check Constraints' or 'Default Values'. Explain how this architectural leniency degrades data quality and pushes validation burdens entirely to the frontend."
        });


        UpdateTopicCount();
    }
 

    private void AddTopic(AnalysisTopic topic)
    {
        topic.PropertyChanged += OnTopicSelectionChanged;
        Topics.Add(topic);
    }

    private void OnTableSelectionChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableSelection.IsSelected)) UpdateTableCount();
    }

    private void OnTopicSelectionChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnalysisTopic.IsSelected)) UpdateTopicCount();
    }

    private void UpdateTableCount() => SelectedTableText = $"{Tables.Count(t => t.IsSelected)} / {Tables.Count} tables selected";
    private void UpdateTopicCount() => SelectedTopicText = $"{Topics.Count(t => t.IsSelected)} / {Topics.Count} topics selected";

    partial void OnSearchQueryChanged(string value)
    {
        FilteredTables.Clear();
        var lowerQuery = value?.ToLower() ?? "";
        foreach (var t in Tables)
        {
            if (t.Name.ToLower().Contains(lowerQuery)) FilteredTables.Add(t);
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            SelectedEngine = SelectedEngineIndex switch { 1 => DbEngine.PostgreSql, 2 => DbEngine.MySql, _ => DbEngine.SqlServer };
            string portString = string.IsNullOrWhiteSpace(Port) ? "" : $"Port={Port};";
            BaseConnectionString = SelectedEngine switch
            {
                DbEngine.SqlServer => $"Server={ServerAddress};{portString}User Id={Username};Password={Password};TrustServerCertificate=True;",
                DbEngine.PostgreSql => $"Host={ServerAddress};{portString}Username={Username};Password={Password};",
                DbEngine.MySql => $"Server={ServerAddress};{portString}Uid={Username};Pwd={Password};"
            };

            var dbs = await _dbService.GetDatabasesAsync(SelectedEngine, BaseConnectionString);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Databases.Clear();
                foreach (var db in dbs) Databases.Add(db);
                IsConnectionCardVisible = false;
                IsFiltersCardVisible = true;
            });
        }
        catch (Exception ex) { await Application.Current.MainPage.DisplayAlert("Error", ex.Message, "OK"); }
        finally { IsBusy = false; }
    }

    private async Task LoadTablesAsync(string dbName)
    {
        MainThread.BeginInvokeOnMainThread(() => IsBusy = true);
        try
        {
            string fullConnString = BaseConnectionString + (SelectedEngine == DbEngine.SqlServer ? $"Database={dbName};" : $"Database={dbName};");
            var tables = await _dbService.GetTablesAsync(SelectedEngine, fullConnString);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var t in Tables) t.PropertyChanged -= OnTableSelectionChanged;
                Tables.Clear();
                FilteredTables.Clear();

                foreach (var t in tables)
                {
                    var sel = new TableSelection { Name = t, IsSelected = true };
                    sel.PropertyChanged += OnTableSelectionChanged;
                    Tables.Add(sel);
                    FilteredTables.Add(sel);
                }
                UpdateTableCount();
            });
        }
        catch (Exception ex) { MainThread.BeginInvokeOnMainThread(async () => await Application.Current.MainPage.DisplayAlert("Error", ex.Message, "OK")); }
        finally { MainThread.BeginInvokeOnMainThread(() => IsBusy = false); }
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (string.IsNullOrEmpty(SelectedDatabase)) return;
        IsBusy = true;
        try
        {
            var filter = new SchemaFilterConfig
            {
                SelectedTables = Tables.Where(t => t.IsSelected).Select(t => t.Name).ToList(),
                IncludeViews = Components.First(c => c.Name == "Views").IsSelected,
                IncludeIndexes = Components.First(c => c.Name == "Indexes").IsSelected,
                IncludeTriggers = Components.First(c => c.Name == "Triggers").IsSelected,
                IncludeSps = Components.First(c => c.Name == "Stored Procedures").IsSelected,
                IncludeConstraints = Components.First(c => c.Name == "Constraints").IsSelected,
                IncludeSampleData = SampleDataSelection.IsSelected
            };

            string fullConnString = BaseConnectionString + (SelectedEngine == DbEngine.SqlServer ? $"Database={SelectedDatabase};" : $"Database={SelectedDatabase};");
            ExtractedJsonSchema = await _dbService.ExtractSchemaAsJsonAsync(SelectedEngine, fullConnString, filter);

            int charCount = ExtractedJsonSchema.Length;
            double kbSize = charCount / 1024.0;
            int estimatedTokens = charCount / 4;

            SchemaStatsText = $"Size: {kbSize:N2} KB | Chars: {charCount:N0} | Est. Tokens: ~{estimatedTokens:N0}";
            SchemaStatsColor = estimatedTokens > 100000 ? Colors.DarkRed : Color.FromArgb("#212529");

            IsFiltersCardVisible = false;
            IsSchemaPreviewCardVisible = true;
        }
        catch (Exception ex) { await Application.Current.MainPage.DisplayAlert("Error", ex.Message, "OK"); }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void BackToConnection() { IsFiltersCardVisible = false; IsConnectionCardVisible = true; }
    [RelayCommand] private void ApproveSchema() { IsSchemaPreviewCardVisible = false; IsQuestionsCardVisible = true; }
    [RelayCommand] private void BackToSchema() { IsQuestionsCardVisible = false; IsSchemaPreviewCardVisible = true; }

    [RelayCommand]
    private void StartOver()
    {
        IsSchemaPreviewCardVisible = false;
        IsFiltersCardVisible = false;
        IsConnectionCardVisible = true;
        SelectedDatabase = null;
        Databases.Clear();
        Tables.Clear();
        FilteredTables.Clear();
        ExtractedJsonSchema = string.Empty;
        UpdateTableCount();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Schema.txt");
        await File.WriteAllTextAsync(path, ExtractedJsonSchema);
        await Application.Current.MainPage.DisplayAlert("Exported", "Saved to Desktop.", "OK");
    }

    [RelayCommand]
    private async Task VisualizeAsync()
    {
        if (string.IsNullOrEmpty(ExtractedJsonSchema)) return;
        try
        {
            string mermaidCode = _dbService.ConvertJsonToMermaid(ExtractedJsonSchema);
            var visualPage = new VisualizerPage(mermaidCode, ExtractedJsonSchema);
            await Shell.Current.Navigation.PushModalAsync(visualPage);
        }
        catch (Exception ex) { await Application.Current.MainPage.DisplayAlert("Error", "Visualization failed: " + ex.Message, "OK"); }
    }

    [RelayCommand]
    private void ToggleAllTables()
    {
        bool target = !Tables.All(t => t.IsSelected);
        foreach (var t in Tables) t.IsSelected = target;
    }

    [RelayCommand]
    private void ToggleAllTopics()
    {
        bool target = !Topics.All(t => t.IsSelected);
        foreach (var t in Topics) t.IsSelected = target;
    }

    [RelayCommand]
    private void AddCustomQuestion()
    {
        if (string.IsNullOrWhiteSpace(CustomQuestionText)) return;
        AddTopic(new AnalysisTopic { Title = "Custom Prompt", Prompt = CustomQuestionText, IsCustom = true, IsSelected = true });
        CustomQuestionText = string.Empty;
    }

    [RelayCommand]
    private void RemoveTopic(AnalysisTopic topic)
    {
        if (topic != null && Topics.Contains(topic))
        {
            topic.PropertyChanged -= OnTopicSelectionChanged;
            Topics.Remove(topic);
            UpdateTopicCount();
        }
    }

    [RelayCommand]
    private async Task StartAnalysisAsync()
    {
        var selected = Topics.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0) return;

        SelectedTopicsForAnalysis.Clear();
        foreach (var t in selected)
        {
            t.Status = AnalysisStatus.Pending;
            SelectedTopicsForAnalysis.Add(t);
        }

        IsQuestionsCardVisible = false;
        IsAnalysisProgressCardVisible = true;
        IsAnalysisRunning = true;

        bool isLocal = Preferences.Get("SelectedEngine", "Ollama") == "Ollama";
        var reportSections = new List<ReportSection>();
        StringBuilder fullAnalysisContent = new StringBuilder();

        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var topic = selected[i];
                topic.Status = AnalysisStatus.Processing;
                CurrentTaskText = $"Processing: {topic.Title} ({i + 1}/{selected.Count})";

                try
                {
                    string promptWithSchema = $"{topic.Prompt}\n\nSchema:\n{ExtractedJsonSchema}";
                    string response = isLocal ? await _llmService.AskOllamaAsync(promptWithSchema) : await _llmService.AskGeminiAsync(promptWithSchema);

                    reportSections.Add(new ReportSection { Title = topic.Title, Content = response });
                    fullAnalysisContent.AppendLine($"--- Section: {topic.Title} ---").AppendLine(response).AppendLine();
                    topic.Status = AnalysisStatus.Completed;
                }
                catch (Exception ex)
                {
                    topic.Status = AnalysisStatus.Error;
                    reportSections.Add(new ReportSection { Title = topic.Title, Content = "Error: " + ex.Message });
                }
            }

            CurrentTaskText = "Generating Executive Summary & Action Plan...";
            string aggregatedResults = fullAnalysisContent.ToString();
            string summaryPrompt = $"Write an Executive Summary based on these results:\n{aggregatedResults}";
            string actionPrompt = $"Provide a Prioritized Action Plan based on these results:\n{aggregatedResults}";

            string summaryResponse = isLocal ? await _llmService.AskOllamaAsync(summaryPrompt) : await _llmService.AskGeminiAsync(summaryPrompt);
            await Task.Delay(2000); 
            string actionResponse = isLocal ? await _llmService.AskOllamaAsync(actionPrompt) : await _llmService.AskGeminiAsync(actionPrompt);

            reportSections.Insert(0, new ReportSection { Title = "1. Executive Summary", Content = summaryResponse });
            reportSections.Insert(1, new ReportSection { Title = "2. Prioritized Action Plan", Content = actionResponse });

            CurrentTaskText = "Finalizing PDF Report...";
            string pdfPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{SelectedDatabase}_Comprehensive_Analysis.pdf");
            _pdfService.GenerateReport(reportSections, pdfPath, SelectedDatabase);

            await Application.Current.MainPage.DisplayAlert("Success", "Comprehensive Report saved to Desktop.", "OK");
        }
        catch (Exception ex) { await Application.Current.MainPage.DisplayAlert("System Error", ex.Message, "OK"); }
        finally
        {
            IsAnalysisRunning = false;
            CurrentTaskText = "Analysis Finished.";
        }
    }
}