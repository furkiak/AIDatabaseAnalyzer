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

    [ObservableProperty] private bool isOllamaSelected;
    [ObservableProperty] private bool isGeminiSelected;
    [ObservableProperty] private bool isOllamaConfigVisible;
    [ObservableProperty] private bool isGeminiConfigVisible;
    [ObservableProperty] private string ollamaUrl;
    [ObservableProperty] private string ollamaModel;
    [ObservableProperty] private string geminiApiKey;
    [ObservableProperty] private string geminiModel;
    [ObservableProperty] private bool isTesting;

    public SettingsViewModel(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        string engine = Preferences.Get("SelectedEngine", "Ollama");
        IsGeminiSelected = engine == "Gemini";
        IsOllamaSelected = !IsGeminiSelected;

        OllamaUrl = Preferences.Get("OllamaUrl", "http://localhost:11434");
        OllamaModel = Preferences.Get("OllamaModel", "qwen2.5-coder");
        GeminiModel = Preferences.Get("GeminiModel", "gemini-3.1-flash-lite-preview");

        GeminiApiKey = await SecureStorage.Default.GetAsync("GeminiApiKey") ?? "";

        UpdateVisibility();
    }

    partial void OnIsOllamaSelectedChanged(bool value) => UpdateVisibility();
    private void UpdateVisibility()
    {
        IsOllamaConfigVisible = IsOllamaSelected;
        IsGeminiConfigVisible = !IsOllamaSelected;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        Preferences.Set("SelectedEngine", IsOllamaSelected ? "Ollama" : "Gemini");
        Preferences.Set("OllamaUrl", OllamaUrl);
        Preferences.Set("OllamaModel", OllamaModel);
        Preferences.Set("GeminiModel", GeminiModel);

        if (!string.IsNullOrWhiteSpace(GeminiApiKey))
            await SecureStorage.Default.SetAsync("GeminiApiKey", GeminiApiKey);
        else
            SecureStorage.Default.Remove("GeminiApiKey");

        await Application.Current.MainPage.DisplayAlert("Success", "Settings saved securely.", "OK");
    }

    [RelayCommand]
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
                await Application.Current.MainPage.DisplayAlert("Success", "Ollama is active!", "OK");
            }
            else
            {
                using var request = GeminiApiRequest.Create(GeminiModel, GeminiApiKey, "Connection test");
                using var res = await _httpClient.SendAsync(request);
                if (!res.IsSuccessStatusCode) throw new Exception($"Gemini returned {(int)res.StatusCode}.");
                await Application.Current.MainPage.DisplayAlert("Success", "Gemini connection OK!", "OK");
            }
        }
        catch (Exception) { await Application.Current.MainPage.DisplayAlert("Connection test failed", "Check the endpoint, model, network and API key. No credential details are shown.", "OK"); }
        finally { IsTesting = false; }
    }
}
