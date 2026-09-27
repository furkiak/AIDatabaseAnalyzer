using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using AIDatabaseAnalyzer.Services;

namespace AIDatabaseAnalyzer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly HttpClient _httpClient;
    private readonly IAppSettingsStore _settings;

    [ObservableProperty] public partial bool IsOllamaSelected { get; set; }
    [ObservableProperty] public partial bool IsGeminiSelected { get; set; }
    [ObservableProperty] public partial bool IsOllamaConfigVisible { get; set; }
    [ObservableProperty] public partial bool IsGeminiConfigVisible { get; set; }
    [ObservableProperty] public partial string OllamaUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial string OllamaModel { get; set; } = string.Empty;
    [ObservableProperty] public partial string GeminiApiKey { get; set; } = string.Empty;
    [ObservableProperty] public partial string GeminiModel { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsTesting { get; set; }
    private bool _canSaveSettings;
    public bool CanSaveSettings
    {
        get => _canSaveSettings;
        private set
        {
            if (!SetProperty(ref _canSaveSettings, value)) return;
            SaveSettingsCommand.NotifyCanExecuteChanged();
            TestConnectionCommand.NotifyCanExecuteChanged();
        }
    }
    private string _settingsLoadStatus = "Loading model settings…";
    public string SettingsLoadStatus { get => _settingsLoadStatus; private set => SetProperty(ref _settingsLoadStatus, value); }

    public SettingsViewModel(HttpClient httpClient, IAppSettingsStore settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            string engine = _settings.Get("SelectedEngine", "Ollama");
            IsGeminiSelected = engine == "Gemini";
            IsOllamaSelected = !IsGeminiSelected;

            OllamaUrl = _settings.Get("OllamaUrl", "http://localhost:11434");
            OllamaModel = _settings.Get("OllamaModel", "qwen2.5-coder");
            GeminiModel = _settings.Get("GeminiModel", "gemini-3.1-flash-lite-preview");

            GeminiApiKey = await _settings.GetSecretAsync("GeminiApiKey") ?? "";

            UpdateVisibility();
            CanSaveSettings = true;
            SettingsLoadStatus = "Model settings loaded from the selected profile.";
        }
        catch (Exception)
        {
            CanSaveSettings = false;
            SettingsLoadStatus = "Model settings could not be read. Existing files were preserved. Check this profile before saving or testing a connection.";
        }
    }

    partial void OnIsOllamaSelectedChanged(bool value) => UpdateVisibility();
    private void UpdateVisibility()
    {
        IsOllamaConfigVisible = IsOllamaSelected;
        IsGeminiConfigVisible = !IsOllamaSelected;
    }

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task SaveSettingsAsync()
    {
        try
        {
            _settings.Set("SelectedEngine", IsOllamaSelected ? "Ollama" : "Gemini");
            _settings.Set("OllamaUrl", OllamaUrl);
            _settings.Set("OllamaModel", OllamaModel);
            _settings.Set("GeminiModel", GeminiModel);

            if (!string.IsNullOrWhiteSpace(GeminiApiKey))
                await _settings.SetSecretAsync("GeminiApiKey", GeminiApiKey);
            else
                _settings.RemoveSecret("GeminiApiKey");

            await Shell.Current.DisplayAppAlertAsync("Success", "Settings saved securely.", "OK");
        }
        catch (Exception)
        {
            await Shell.Current.DisplayAppAlertAsync("Settings not fully saved", "Check this profile's file format, permissions and credential storage. No credential details are shown. Earlier individual model fields may already have been saved.", "OK");
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task TestConnectionAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        try
        {
            if (IsOllamaSelected)
            {
                var res = await _httpClient.GetAsync($"{OllamaUrl?.TrimEnd('/')}/api/tags");
                res.EnsureSuccessStatusCode();
                await Shell.Current.DisplayAppAlertAsync("Success", "Ollama is active!", "OK");
            }
            else
            {
                using var request = GeminiApiRequest.Create(GeminiModel, GeminiApiKey, "Connection test");
                using var res = await _httpClient.SendAsync(request);
                if (!res.IsSuccessStatusCode) throw new Exception($"Gemini returned {(int)res.StatusCode}.");
                await Shell.Current.DisplayAppAlertAsync("Success", "Gemini connection OK!", "OK");
            }
        }
        catch (Exception) { await Shell.Current.DisplayAppAlertAsync("Connection test failed", "Check the endpoint, model, network and API key. No credential details are shown.", "OK"); }
        finally { IsTesting = false; }
    }
}
