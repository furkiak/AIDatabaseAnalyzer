using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services;

namespace AIDatabaseAnalyzer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DbSchemaService _dbService;
    private readonly ILlmService _llmService;
    private readonly PdfReportService _pdfService;
    private CancellationTokenSource? _analysisCts;

    [ObservableProperty] private string serverAddress = "localhost";
    [ObservableProperty] private string port = "";
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private bool allowUntrustedCertificate;
    [ObservableProperty] private int selectedEngineIndex = 0;


    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isDemo;
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
    [ObservableProperty] private string cloudPrivacyText = "AI requests exclude sample rows. Sending metadata to Gemini or a non-local Ollama endpoint requires confirmation.";
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
            if (SetProperty(ref _selectedDatabase, value) && !IsDemo && !string.IsNullOrEmpty(value))
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
        SampleDataSelection = new ComponentSelection { Name = "Read up to 3 rows per table (local only; may contain sensitive data)", IsSelected = false };
    }

    private void LoadDefaultTopics()
    {
        // These topics use only fields actually emitted by DbSchemaService.
        AddTopic(new AnalysisTopic
        {
            Title = "Schema inventory and data dictionary",
            Prompt = "List selected tables, their columns, types and declared maximum lengths. Cite the exact Tables property path for each statement. Derive no business meaning from a name without marking it as a hypothesis."
        });
        AddTopic(new AnalysisTopic
        {
            Title = "Declared relationship map",
            Prompt = "Summarize the foreign keys present in ForeignKeys. Cite each constraint name and From/To table. Suggest possible missing relationships only as questions for the database owner, never as verified defects."
        });
        AddTopic(new AnalysisTopic
        {
            Title = "Index metadata review",
            Prompt = "Inventory the indexes present in Indexes, including key column order, included columns, uniqueness and any filter or definition. Cite exact index paths. Workload, query plans, cardinality and costs are absent; do not recommend dropping or adding an index as a verified fix."
        });
        AddTopic(new AnalysisTopic
        {
            Title = "Constraint and nullability review questions",
            Prompt = "List available nullability, primary-key flags and constraint metadata, including definitions when present. Cite exact source paths. Explain that business criticality and full enforcement cannot be established from this snapshot; ask the DBA to verify possible gaps."
        });
        AddTopic(new AnalysisTopic
        {
            Title = "Object inventory and verification plan",
            Prompt = "Inventory view, trigger and stored-procedure names from the payload. Their definitions, permissions and execution behavior are absent. Identify the precise read-only evidence a DBA would need before drawing performance or security conclusions."
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
        IsDemo = false;
        IsBusy = true;
        try
        {
            SelectedEngine = SelectedEngineIndex switch { 1 => DbEngine.PostgreSql, 2 => DbEngine.MySql, _ => DbEngine.SqlServer };
            BaseConnectionString = DatabaseConnectionStrings.Build(
                SelectedEngine, ServerAddress, Port, Username, Password, AllowUntrustedCertificate);

            var dbs = await _dbService.GetDatabasesAsync(SelectedEngine, BaseConnectionString);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Databases.Clear();
                foreach (var db in dbs) Databases.Add(db);
                IsConnectionCardVisible = false;
                IsFiltersCardVisible = true;
            });
        }
        catch (Exception) { await Application.Current.MainPage.DisplayAlert("Connection failed", "Check the server, port (1–65535), credentials and certificate. Untrusted certificates require the local-development option.", "OK"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadDemoAsync()
    {
        if (IsBusy || IsAnalysisRunning) return;
        IsBusy = true;
        try
        {
            await using var stream = await FileSystem.Current.OpenAppPackageFileAsync("demo-schema.json");
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            using var document = System.Text.Json.JsonDocument.Parse(json);
            IsDemo = true;
            SelectedEngine = DbEngine.SqlServer;
            SelectedDatabase = "Offline demo";
            foreach (var table in Tables) table.PropertyChanged -= OnTableSelectionChanged;
            Tables.Clear();
            FilteredTables.Clear();
            foreach (var property in document.RootElement.GetProperty("Tables").EnumerateObject())
            {
                var table = new TableSelection { Name = property.Name, IsSelected = true };
                table.PropertyChanged += OnTableSelectionChanged;
                Tables.Add(table);
                FilteredTables.Add(table);
            }
            UpdateTableCount();
            ExtractedJsonSchema = json;
            SchemaStatsText = $"Offline demo: {json.Length:N0} characters | {Tables.Count} synthetic tables";
            SchemaStatsColor = Color.FromArgb("#245CD6");
            CloudPrivacyText = "Synthetic demo. Diagram and JSON preview need no database, model or network. Running AI review uses your selected model destination.";
            IsConnectionCardVisible = false;
            IsFiltersCardVisible = false;
            IsSchemaPreviewCardVisible = true;
        }
        catch (Exception)
        {
            await Application.Current.MainPage.DisplayAlert("Demo unavailable", "The packaged demo schema could not be opened.", "OK");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadTablesAsync(string dbName)
    {
        MainThread.BeginInvokeOnMainThread(() => IsBusy = true);
        try
        {
            string fullConnString = DatabaseConnectionStrings.WithDatabase(SelectedEngine, BaseConnectionString, dbName);
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
        catch (Exception) { MainThread.BeginInvokeOnMainThread(async () => await Application.Current.MainPage.DisplayAlert("Connection failed", "Could not list tables. Check access and connection settings.", "OK")); }
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

            string fullConnString = DatabaseConnectionStrings.WithDatabase(SelectedEngine, BaseConnectionString, SelectedDatabase);
            ExtractedJsonSchema = await _dbService.ExtractSchemaAsJsonAsync(SelectedEngine, fullConnString, filter);

            int charCount = ExtractedJsonSchema.Length;
            double kbSize = charCount / 1024.0;
            int estimatedTokens = charCount / 4;

            SchemaStatsText = $"Local snapshot: {kbSize:N2} KB | {charCount:N0} chars | ~{estimatedTokens:N0} tokens";
            CloudPrivacyText = filter.IncludeSampleData
                ? "This preview includes sample rows for local visualization and export. AI requests exclude them. Sending metadata to a remote model requires confirmation."
                : "AI requests exclude sample rows. Sending metadata to Gemini or a non-local Ollama endpoint requires confirmation.";
            SchemaStatsColor = estimatedTokens > 100000 ? Colors.DarkRed : Color.FromArgb("#212529");

            IsFiltersCardVisible = false;
            IsSchemaPreviewCardVisible = true;
        }
        catch (Exception) { await Application.Current.MainPage.DisplayAlert("Schema extraction failed", "Check database permissions and the selected scope. No password or connection string is shown.", "OK"); }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void BackToConnection() { IsFiltersCardVisible = false; IsConnectionCardVisible = true; }
    [RelayCommand] private void ApproveSchema() { IsSchemaPreviewCardVisible = false; IsQuestionsCardVisible = true; }
    [RelayCommand] private void BackToSchema() { IsQuestionsCardVisible = false; IsSchemaPreviewCardVisible = true; }

    [RelayCommand]
    private void StartOver()
    {
        IsDemo = false;
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
        if (string.IsNullOrWhiteSpace(ExtractedJsonSchema)) return;
        try
        {
            string path = CreateOutputPath("Schema", ".json");
            await File.WriteAllTextAsync(path, ExtractedJsonSchema);
            await PresentOutputAsync(path, "Schema snapshot");
        }
        catch (Exception) { await Application.Current.MainPage.DisplayAlert("Export failed", "The local file could not be saved. Check the output folder and available space. No connection string is shown.", "OK"); }
    }

    [RelayCommand]
    private async Task ExportEvidencePdfAsync()
    {
        if (string.IsNullOrWhiteSpace(ExtractedJsonSchema)) return;
        try
        {
            var metadata = SchemaPrivacy.WithoutSamples(ExtractedJsonSchema);
            var sections = new List<ReportSection>
            {
                new() { Title = IsDemo ? "Synthetic offline demo" : "Local schema snapshot", Content = "This export calls no model and excludes sample rows. It records available catalog metadata, not performance, permission or compliance findings. Empty or omitted fields do not establish absence. Review object names and definitions before sharing." },
                new() { Title = "Source field manifest", Content = SchemaEvidenceSummary.Create(metadata) },
                new() { Title = "Metadata JSON", Content = metadata }
            };
            var path = CreateOutputPath("Evidence", ".pdf");
            _pdfService.GenerateReport(sections, path, SelectedDatabase, modelAssisted: false);
            await PresentOutputAsync(path, "Local evidence PDF (no model, no sample rows)");
        }
        catch (Exception) { await Application.Current.MainPage.DisplayAlert("Export failed", "The local evidence PDF could not be saved. Check the output folder and available space. No model request was sent.", "OK"); }
    }

    [RelayCommand]
    private async Task VisualizeAsync()
    {
        if (string.IsNullOrEmpty(ExtractedJsonSchema)) return;
        try
        {
            var visualPage = new VisualizerPage(ExtractedJsonSchema);
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
        if (IsAnalysisRunning || selected.Count == 0 || string.IsNullOrWhiteSpace(ExtractedJsonSchema)) return;

        bool isLocal = Preferences.Get("SelectedEngine", "Ollama") == "Ollama";
        string analysisSchema = SchemaPrivacy.WithoutSamples(ExtractedJsonSchema);
        if (selected.Any(topic => topic.Prompt.Length > 3_000))
        {
            await Application.Current.MainPage.DisplayAlert("Prompt too long", "Shorten the custom prompt to 3,000 characters or fewer.", "OK");
            return;
        }
        IReadOnlyList<string> schemaChunks;
        try { schemaChunks = SchemaChunker.Split(analysisSchema); }
        catch (InvalidOperationException)
        {
            await Application.Current.MainPage.DisplayAlert("Schema too large", "Exclude optional metadata or select fewer tables. No model request was sent.", "OK");
            return;
        }
        string ollamaUrl = Preferences.Get("OllamaUrl", "http://localhost:11434");
        bool needsConsent = !isLocal || !ModelDestination.IsLoopbackOllama(ollamaUrl);
        if (needsConsent)
        {
            var destination = isLocal
                ? $"Ollama endpoint {ollamaUrl}"
                : $"Google Gemini ({Preferences.Get("GeminiModel", "gemini-3.1-flash-lite-preview")})";
            bool consent = await Application.Current.MainPage.DisplayAlert(
                "Send database metadata to a model?",
                $"Destination: {destination}. Snapshot: {analysisSchema.Length:N0} characters; {schemaChunks.Count} sections repeated for {selected.Count} selected prompts. Sample rows are excluded. Model responses may also be sent for the summary. Review metadata and custom prompts for sensitive names first.",
                "Send and analyze", "Cancel");
            if (!consent) return;
        }

        SelectedTopicsForAnalysis.Clear();
        foreach (var t in selected)
        {
            t.Status = AnalysisStatus.Pending;
            SelectedTopicsForAnalysis.Add(t);
        }

        IsQuestionsCardVisible = false;
        IsAnalysisProgressCardVisible = true;
        IsAnalysisRunning = true;
        _analysisCts = new CancellationTokenSource();
        var cancellationToken = _analysisCts.Token;

        var reportSections = new List<ReportSection>();
        reportSections.Add(new ReportSection
        {
            Title = "Scope and evidence limitations",
            Content = $"AI-assisted hypotheses from a {(IsDemo ? "synthetic offline demo" : "database")} schema snapshot. Engine: {SelectedEngine}; database: {SelectedDatabase}; selected tables: {Tables.Count(t => t.IsSelected)}; model destination: {(isLocal ? ollamaUrl : "Google Gemini")}. No queries, execution plans, permissions, row statistics, trigger/view/routine bodies or production behavior were measured. Verify every recommendation against the live workload before making a change."
        });
        reportSections.Add(new ReportSection
        {
            Title = "Source field manifest",
            Content = SchemaEvidenceSummary.Create(analysisSchema)
        });
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
                    var topicResponses = new StringBuilder();
                    for (int chunk = 0; chunk < schemaChunks.Count; chunk++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        CurrentTaskText = $"{topic.Title}: section {chunk + 1}/{schemaChunks.Count}";
                        string prompt = AnalysisPromptPolicy.Topic(
                            $"{topic.Prompt}\nReview only section {chunk + 1} of {schemaChunks.Count}; do not infer missing objects from this partial scope.",
                            schemaChunks[chunk]);
                        string response = isLocal
                            ? await _llmService.AskOllamaAsync(prompt, cancellationToken)
                            : await _llmService.AskGeminiAsync(prompt, cancellationToken);
                        topicResponses.AppendLine($"Section {chunk + 1}/{schemaChunks.Count}:").AppendLine(response).AppendLine();
                    }

                    var completedResponse = topicResponses.ToString();
                    reportSections.Add(new ReportSection { Title = $"Hypotheses: {topic.Title}", Content = "Unverified model output. Match each claim to the source field manifest and independent database evidence before acting.\n\n" + completedResponse });
                    fullAnalysisContent.AppendLine($"--- Section: {topic.Title} ---").AppendLine(completedResponse).AppendLine();
                    topic.Status = AnalysisStatus.Completed;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    topic.Status = AnalysisStatus.Error;
                    reportSections.Add(new ReportSection { Title = topic.Title, Content = "Analysis failed for this topic. No finding was established." });
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!selected.Any(topic => topic.Status == AnalysisStatus.Completed))
                throw new InvalidOperationException("No model response was completed.");
            CurrentTaskText = "Preparing evidence summary and verification plan...";
            string aggregatedResults = fullAnalysisContent.ToString();
            string summaryPrompt = AnalysisPromptPolicy.Synthesis("Write a concise evidence-limited summary.", aggregatedResults);
            string actionPrompt = AnalysisPromptPolicy.Synthesis("Create a prioritized verification plan, not an automatic remediation plan.", aggregatedResults);

            string summaryResponse;
            string actionResponse;
            if (summaryPrompt.Length <= SchemaChunker.DefaultMaxCharacters && actionPrompt.Length <= SchemaChunker.DefaultMaxCharacters)
            {
                summaryResponse = isLocal
                    ? await _llmService.AskOllamaAsync(summaryPrompt, cancellationToken)
                    : await _llmService.AskGeminiAsync(summaryPrompt, cancellationToken);
                actionResponse = isLocal
                    ? await _llmService.AskOllamaAsync(actionPrompt, cancellationToken)
                    : await _llmService.AskGeminiAsync(actionPrompt, cancellationToken);
            }
            else
            {
                summaryResponse = $"{selected.Count(topic => topic.Status == AnalysisStatus.Completed)} of {selected.Count} selected topics produced unverified observations. Results exceeded the synthesis request budget; inspect each section and its source path.";
                actionResponse = "Review each hypothesis against the listed schema field and a live read-only query or execution plan. Record owner, measured impact, change and rollback before prioritizing remediation.";
            }

            reportSections.Insert(1, new ReportSection { Title = "Executive summary (unverified)", Content = summaryResponse });
            reportSections.Insert(2, new ReportSection { Title = "Prioritized verification plan", Content = actionResponse });

            cancellationToken.ThrowIfCancellationRequested();
            CurrentTaskText = "Finalizing PDF Report...";
            string pdfPath = CreateOutputPath("Schema_Review", ".pdf");
            _pdfService.GenerateReport(reportSections, pdfPath, SelectedDatabase);

            await PresentOutputAsync(pdfPath, "Schema review PDF");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            foreach (var topic in selected.Where(topic => topic.Status == AnalysisStatus.Processing))
                topic.Status = AnalysisStatus.Pending;
            IsAnalysisProgressCardVisible = false;
            IsQuestionsCardVisible = true;
            CurrentTaskText = "Analysis canceled; no PDF was created.";
        }
        catch (Exception)
        {
            IsAnalysisProgressCardVisible = false;
            IsQuestionsCardVisible = true;
            await Application.Current.MainPage.DisplayAlert("Analysis failed", "The report could not be completed. Check the selected model and connection settings.", "OK");
        }
        finally
        {
            _analysisCts?.Dispose();
            _analysisCts = null;
            IsAnalysisRunning = false;
            if (IsAnalysisProgressCardVisible) CurrentTaskText = "Analysis finished.";
        }
    }

    [RelayCommand]
    private void CancelAnalysis() => _analysisCts?.Cancel();

    private string CreateOutputPath(string purpose, string extension)
    {
        var directory = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : FileSystem.Current.AppDataDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            directory = FileSystem.Current.AppDataDirectory;
        var database = string.IsNullOrWhiteSpace(SelectedDatabase) ? "Database" : SelectedDatabase;
        var safeName = string.Concat(database.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\' or ':' ? '_' : character));
        return Path.Combine(directory, $"{safeName}_{purpose}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}");
    }

    private async Task PresentOutputAsync(string path, string title)
    {
        if (OperatingSystem.IsWindows())
            await Application.Current.MainPage.DisplayAlert("Saved", $"{title} saved to {path}. Review sensitive metadata before sharing.", "OK");
        else
            await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(
                new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFileRequest
                {
                    Title = title,
                    File = new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFile(path)
                });
    }
}
