using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Llm;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>AI provider, models, appearance and privacy defaults.</summary>
public partial class SettingsViewModel : ObservableObject
{
    /// <summary>Unsaved values of one provider; kept while the user switches between providers.</summary>
    private sealed class Draft
    {
        public string Model = "";
        public string ApiKey = "";
        public string BaseUrl = "";
        public string Context = "";
    }

    private static readonly int[] ParallelChoices = { 1, 2, 3, 4, 6, 8 };

    private readonly ISettingsService _settings;
    private readonly ILlmService _llm;
    private readonly IThemeService _theme;
    private readonly INoticeService _notices;
    private readonly Dictionary<LlmProvider, Draft> _drafts = new();
    private CancellationTokenSource? _testCts;
    private LlmProvider _provider;
    private bool _loaded;

    public SettingsViewModel(ISettingsService settings, ILlmService llm, IThemeService theme, INoticeService notices)
    {
        _settings = settings;
        _llm = llm;
        _theme = theme;
        _notices = notices;

        foreach (var info in LlmProviders.All)
            ProviderTiles.Add(new ChoiceTile((int)info.Provider, info.Label, info.Provider switch
            {
                LlmProvider.Ollama => "Local · private",
                LlmProvider.OpenAICompatible => "Local or hosted",
                _ => "Cloud · API key"
            }, SelectProvider));

        _ = LoadAsync();
    }

    public ObservableCollection<ChoiceTile> ProviderTiles { get; } = new();
    public IReadOnlyList<string> Themes { get; } = new[] { "System", "Light", "Dark" };
    public IReadOnlyList<string> ParallelOptions { get; } = ParallelChoices.Select(n => n.ToString()).ToList();

    public ObservableCollection<string> AvailableModels { get; } = new();
    public ObservableCollection<FilterChip> SuggestedModels { get; } = new();

    // ------------------------------------------------------------------ provider fields

    [ObservableProperty] private string providerTitle = "";
    [ObservableProperty] private string privacyNote = "";
    [ObservableProperty] private bool showApiKey;
    [ObservableProperty] private bool showBaseUrl;
    [ObservableProperty] private bool showContext;
    [ObservableProperty] private bool showParallel;
    [ObservableProperty] private bool hasSignupLink;
    [ObservableProperty] private string signupText = "";
    [ObservableProperty] private string apiKeyLabel = "API key";
    [ObservableProperty] private string apiKeyPlaceholder = "";
    [ObservableProperty] private string modelPlaceholder = "";
    [ObservableProperty] private string baseUrlPlaceholder = "";

    [ObservableProperty] private string model = "";
    [ObservableProperty] private string apiKey = "";
    [ObservableProperty] private string baseUrl = "";
    [ObservableProperty] private string contextWindow = "32768";
    [ObservableProperty] private int parallelIndex = 2;
    [ObservableProperty] private bool isKeyHidden = true;

    [ObservableProperty] private int themeIndex;
    [ObservableProperty] private bool maskSampleData = true;

    [ObservableProperty] private string? selectedModel;

    [ObservableProperty] private bool isTesting;
    [ObservableProperty] private string testMessage = "";

    [NotifyPropertyChangedFor(nameof(TestOk))]
    [NotifyPropertyChangedFor(nameof(TestFailed))]
    [ObservableProperty] private bool hasTestMessage;

    [NotifyPropertyChangedFor(nameof(TestOk))]
    [NotifyPropertyChangedFor(nameof(TestFailed))]
    [ObservableProperty] private bool testSucceeded;

    public bool TestOk => HasTestMessage && TestSucceeded;
    public bool TestFailed => HasTestMessage && !TestSucceeded;

    [ObservableProperty] private string secretsNotice = "";
    [ObservableProperty] private bool hasSecretsNotice;

    public bool HasModels => AvailableModels.Count > 0;
    public bool HasSuggestions => SuggestedModels.Count > 0;

    public string VersionText => $"Version {AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})";

    private async Task LoadAsync()
    {
        await _settings.InitializeAsync();

        foreach (var info in LlmProviders.All)
        {
            var p = info.Provider;
            _drafts[p] = new Draft
            {
                Model = _settings.GetModel(p),
                ApiKey = _settings.GetApiKey(p),
                BaseUrl = _settings.GetBaseUrl(p),
                Context = _settings.GetContextWindow(p).ToString()
            };
        }

        int parallel = Array.IndexOf(ParallelChoices, _settings.MaxParallelRequests);
        ParallelIndex = parallel >= 0 ? parallel : 2;
        ThemeIndex = (int)_settings.Theme;
        MaskSampleData = _settings.MaskSampleData;
        HasSecretsNotice = !_settings.SecretsArePersistent;
        SecretsNotice = "Secure storage is not available on this system, so API keys are kept only until you close the app.";

        ShowProvider(_settings.Provider);
        _loaded = true;
    }

    private void SelectProvider(ChoiceTile tile)
    {
        if (_drafts.Count == 0) return;
        StoreDraft();
        ShowProvider((LlmProvider)tile.Key);
    }

    private void StoreDraft()
    {
        if (!_drafts.TryGetValue(_provider, out var draft)) return;
        draft.Model = Model.Trim();
        draft.ApiKey = ApiKey.Trim();
        draft.BaseUrl = BaseUrl.Trim();
        draft.Context = ContextWindow.Trim();
    }

    private void ShowProvider(LlmProvider provider)
    {
        _provider = provider;
        var info = LlmProviders.Get(provider);
        foreach (var tile in ProviderTiles) tile.IsSelected = tile.Key == (int)provider;

        var draft = _drafts[provider];
        Model = draft.Model;
        ApiKey = draft.ApiKey;
        BaseUrl = draft.BaseUrl;
        ContextWindow = draft.Context;

        ProviderTitle = info.Label;
        PrivacyNote = info.PrivacyNote;
        ShowApiKey = info.NeedsApiKey;
        ApiKeyLabel = info.ApiKeyOptional ? "API key (optional)" : "API key";
        ApiKeyPlaceholder = info.ApiKeyPlaceholder;
        ShowBaseUrl = info.ShowsBaseUrl;
        BaseUrlPlaceholder = info.DefaultBaseUrl;
        ShowContext = info.UserDefinedContext;
        ShowParallel = provider != LlmProvider.Ollama;
        ModelPlaceholder = info.ModelPlaceholder;
        HasSignupLink = info.SignupUrl.Length > 0;
        SignupText = info.SignupText;

        SuggestedModels.Clear();
        foreach (string m in info.SuggestedModels) SuggestedModels.Add(new FilterChip(m, m, chip => Model = chip.Key));
        OnPropertyChanged(nameof(HasSuggestions));

        AvailableModels.Clear();
        OnPropertyChanged(nameof(HasModels));
        HasTestMessage = false;
        IsKeyHidden = true;
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (!_loaded) return;
        var mode = (ThemeMode)Math.Clamp(value, 0, 2);
        _settings.Theme = mode;
        _theme.Apply(mode);
    }

    partial void OnMaskSampleDataChanged(bool value)
    {
        if (_loaded) _settings.MaskSampleData = value;
    }

    partial void OnSelectedModelChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value)) Model = value;
    }

    private LlmSettings CurrentDraft()
    {
        int context = int.TryParse(ContextWindow.Trim(), out int c) ? Math.Clamp(c, 2048, 2_000_000) : 32768;
        return new LlmSettings
        {
            Provider = _provider,
            Model = Model.Trim(),
            ApiKey = ApiKey.Trim(),
            BaseUrl = ShowBaseUrl ? BaseUrl.Trim() : "",
            ContextWindow = context,
            MaxParallelRequests = ParallelChoices[Math.Clamp(ParallelIndex, 0, ParallelChoices.Length - 1)]
        };
    }

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_drafts.Count == 0) return;
        StoreDraft();

        foreach (var (provider, draft) in _drafts)
        {
            var info = LlmProviders.Get(provider);
            _settings.SetModel(provider, draft.Model);
            if (info.ShowsBaseUrl) _settings.SetBaseUrl(provider, draft.BaseUrl);
            if (info.UserDefinedContext && int.TryParse(draft.Context, out int context)) _settings.SetContextWindow(provider, context);
            if (info.NeedsApiKey && draft.ApiKey != _settings.GetApiKey(provider)) await _settings.SetApiKeyAsync(provider, draft.ApiKey);
        }

        _settings.Provider = _provider;
        _settings.MaxParallelRequests = ParallelChoices[Math.Clamp(ParallelIndex, 0, ParallelChoices.Length - 1)];
        ContextWindow = _settings.GetContextWindow(_provider).ToString();
        HasSecretsNotice = !_settings.SecretsArePersistent;
        _notices.Show($"Settings saved — analyses now use {LlmProviders.Get(_provider).Name} · {CurrentDraft().ModelName}.", NoticeKind.Success);
    }

    [RelayCommand]
    private void ToggleKeyVisibility() => IsKeyHidden = !IsKeyHidden;

    [RelayCommand]
    private async Task TestAsync()
    {
        if (IsTesting) return;
        _testCts?.Cancel();
        var cts = _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        IsTesting = true;
        HasTestMessage = false;
        try
        {
            var result = await _llm.TestAsync(CurrentDraft(), cts.Token);
            TestSucceeded = result.Success;
            TestMessage = result.Message;
            HasTestMessage = true;

            if (result.Models.Count > 0) FillModels(result.Models);
        }
        catch (OperationCanceledException)
        {
            TestSucceeded = false;
            TestMessage = "The test timed out.";
            HasTestMessage = true;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        HasTestMessage = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var models = await _llm.ListModelsAsync(CurrentDraft(), cts.Token);
            FillModels(models);
            TestSucceeded = models.Count > 0;
            TestMessage = models.Count > 0 ? $"{models.Count} models available — pick one from the list." : "No models were returned.";
            HasTestMessage = true;
        }
        catch (Exception ex) when (ex is LlmException or OperationCanceledException)
        {
            TestSucceeded = false;
            TestMessage = ex is LlmException ? ex.Message : "The request timed out.";
            HasTestMessage = true;
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void FillModels(IReadOnlyList<string> models)
    {
        AvailableModels.Clear();
        foreach (string m in models) AvailableModels.Add(m);
        OnPropertyChanged(nameof(HasModels));
    }

    [RelayCommand]
    private async Task OpenSignupPageAsync()
    {
        string url = LlmProviders.Get(_provider).SignupUrl;
        if (url.Length > 0) await Launcher.Default.TryOpenAsync(new Uri(url));
    }
}
