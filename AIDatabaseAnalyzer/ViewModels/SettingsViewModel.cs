using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Llm;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>AI provider, models, appearance and privacy defaults.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILlmService _llm;
    private readonly IThemeService _theme;
    private readonly INoticeService _notices;
    private CancellationTokenSource? _testCts;
    private bool _loaded;

    public SettingsViewModel(ISettingsService settings, ILlmService llm, IThemeService theme, INoticeService notices)
    {
        _settings = settings;
        _llm = llm;
        _theme = theme;
        _notices = notices;
        _ = LoadAsync();
    }

    public IReadOnlyList<string> Providers { get; } = new[] { "Local · Ollama", "Cloud · Gemini" };
    public IReadOnlyList<string> Themes { get; } = new[] { "System", "Light", "Dark" };

    public ObservableCollection<string> AvailableModels { get; } = new();

    [NotifyPropertyChangedFor(nameof(IsOllama))]
    [NotifyPropertyChangedFor(nameof(IsGemini))]
    [ObservableProperty] private int providerIndex;

    [ObservableProperty] private string ollamaUrl = "";
    [ObservableProperty] private string ollamaModel = "";
    [ObservableProperty] private string ollamaContext = "32768";
    [ObservableProperty] private string geminiModel = "";
    [ObservableProperty] private string geminiApiKey = "";
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
    public bool IsOllama => ProviderIndex == 0;
    public bool IsGemini => ProviderIndex == 1;

    public string VersionText => $"Version {AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})";

    private async Task LoadAsync()
    {
        await _settings.InitializeAsync();

        ProviderIndex = _settings.Provider == LlmProvider.Gemini ? 1 : 0;
        OllamaUrl = _settings.OllamaUrl;
        OllamaModel = _settings.OllamaModel;
        OllamaContext = _settings.OllamaContext.ToString();
        GeminiModel = _settings.GeminiModel;
        GeminiApiKey = _settings.GeminiApiKey;
        ThemeIndex = (int)_settings.Theme;
        MaskSampleData = _settings.MaskSampleData;

        HasSecretsNotice = !_settings.SecretsArePersistent;
        SecretsNotice = "Secure storage is not available on this system, so the API key is kept only until you close the app.";
        _loaded = true;
    }

    partial void OnProviderIndexChanged(int value)
    {
        AvailableModels.Clear();
        OnPropertyChanged(nameof(HasModels));
        HasTestMessage = false;
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
        if (string.IsNullOrEmpty(value)) return;
        if (IsGemini) GeminiModel = value;
        else OllamaModel = value;
    }

    private LlmSettings Draft()
    {
        int context = int.TryParse(OllamaContext.Trim(), out int c) ? Math.Clamp(c, 2048, 1_000_000) : 32768;
        return new LlmSettings
        {
            Provider = IsGemini ? LlmProvider.Gemini : LlmProvider.Ollama,
            OllamaUrl = OllamaUrl.Trim(),
            OllamaModel = OllamaModel.Trim(),
            OllamaContext = context,
            GeminiModel = GeminiModel.Trim(),
            GeminiApiKey = GeminiApiKey.Trim()
        };
    }

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private async Task SaveAsync()
    {
        var draft = Draft();
        _settings.Provider = draft.Provider;
        _settings.OllamaUrl = draft.OllamaUrl;
        _settings.OllamaModel = draft.OllamaModel;
        _settings.OllamaContext = draft.OllamaContext;
        _settings.GeminiModel = draft.GeminiModel;
        await _settings.SetGeminiApiKeyAsync(draft.GeminiApiKey);

        OllamaContext = draft.OllamaContext.ToString();
        HasSecretsNotice = !_settings.SecretsArePersistent;
        _notices.Show("Settings saved.", NoticeKind.Success);
    }

    [RelayCommand]
    private void ToggleKeyVisibility() => IsKeyHidden = !IsKeyHidden;

    [RelayCommand]
    private async Task TestAsync()
    {
        if (IsTesting) return;
        _testCts?.Cancel();
        var cts = _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        IsTesting = true;
        HasTestMessage = false;
        try
        {
            var result = await _llm.TestAsync(Draft(), cts.Token);
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var models = await _llm.ListModelsAsync(Draft(), cts.Token);
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
        foreach (string model in models) AvailableModels.Add(model);
        OnPropertyChanged(nameof(HasModels));
    }

    [RelayCommand]
    private async Task OpenKeyPageAsync() =>
        await Launcher.Default.TryOpenAsync(new Uri("https://aistudio.google.com/apikey"));

    [RelayCommand]
    private async Task OpenOllamaPageAsync() =>
        await Launcher.Default.TryOpenAsync(new Uri("https://ollama.com/download"));
}
