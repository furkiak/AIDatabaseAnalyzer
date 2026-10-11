namespace AIDatabaseAnalyzer.Services.Llm;

/// <summary>Static description of a provider: defaults, what the settings screen needs to ask for and where data goes.</summary>
public sealed class LlmProviderInfo
{
    public required LlmProvider Provider { get; init; }
    public required string Name { get; init; }

    /// <summary>Short label for the provider picker.</summary>
    public required string Label { get; init; }

    public required string DefaultModel { get; init; }
    public string DefaultBaseUrl { get; init; } = "";

    public bool NeedsApiKey { get; init; }

    /// <summary>The key is optional (some OpenAI-compatible servers need one, local ones usually do not).</summary>
    public bool ApiKeyOptional { get; init; }

    public bool ShowsBaseUrl { get; init; }
    public bool UserDefinedContext { get; init; }
    public int DefaultContextTokens { get; init; } = 128_000;

    public string ApiKeyPlaceholder { get; init; } = "";
    public string ModelPlaceholder { get; init; } = "";
    public string SignupUrl { get; init; } = "";
    public string SignupText { get; init; } = "";

    /// <summary>Where the schema is sent; shown on the settings screen and in the privacy confirmation.</summary>
    public required string PrivacyNote { get; init; }

    /// <summary>Suggested models shown before the live list has been loaded.</summary>
    public IReadOnlyList<string> SuggestedModels { get; init; } = Array.Empty<string>();

    public int ContextTokensFor(string model)
    {
        string m = model.ToLowerInvariant();
        return Provider switch
        {
            LlmProvider.Claude => ClaudeContext(m),
            LlmProvider.OpenAI => m.StartsWith("gpt-4.1", StringComparison.Ordinal) ? 1_000_000
                                  : m.StartsWith("gpt-4o", StringComparison.Ordinal) ? 128_000
                                  : DefaultContextTokens,
            _ => DefaultContextTokens
        };
    }

    /// <summary>Claude 4.6 and later read 1M tokens; older generations 200K.</summary>
    private static int ClaudeContext(string model)
    {
        var match = System.Text.RegularExpressions.Regex.Match(model, @"claude-[a-z]+-(\d+)(?:-(\d+))?");
        if (!match.Success) return 1_000_000;
        int major = int.Parse(match.Groups[1].Value);
        int minor = match.Groups[2].Success && match.Groups[2].Value.Length <= 2 ? int.Parse(match.Groups[2].Value) : 0;
        return major > 4 || (major == 4 && minor >= 6) ? 1_000_000 : 200_000;
    }

    public override string ToString() => Label;
}

public static class LlmProviders
{
    public static IReadOnlyList<LlmProviderInfo> All { get; } = new[]
    {
        new LlmProviderInfo
        {
            Provider = LlmProvider.Ollama,
            Name = "Ollama",
            Label = "Ollama (local)",
            DefaultModel = "qwen2.5-coder",
            DefaultBaseUrl = "http://localhost:11434",
            ShowsBaseUrl = true,
            UserDefinedContext = true,
            ModelPlaceholder = "qwen2.5-coder",
            SignupUrl = "https://ollama.com/download",
            SignupText = "Get Ollama",
            PrivacyNote = "Runs on your computer: nothing leaves this machine.",
            SuggestedModels = new[] { "qwen2.5-coder", "qwen3", "llama3.1", "gpt-oss", "deepseek-r1" }
        },
        new LlmProviderInfo
        {
            Provider = LlmProvider.Claude,
            Name = "Claude",
            Label = "Anthropic Claude",
            DefaultModel = "claude-opus-5-5",
            DefaultBaseUrl = "https://api.anthropic.com",
            NeedsApiKey = true,
            DefaultContextTokens = 1_000_000,
            ApiKeyPlaceholder = "sk-ant-…",
            ModelPlaceholder = "claude-opus-5-5",
            SignupUrl = "https://console.anthropic.com/settings/keys",
            SignupText = "Create an Anthropic API key",
            PrivacyNote = "Runs in Anthropic's cloud: schema metadata is sent to Claude. The schema is prompt-cached, so every further topic reads it at a fraction of the cost.",
            SuggestedModels = new[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1" }
        },
        new LlmProviderInfo
        {
            Provider = LlmProvider.OpenAI,
            Name = "OpenAI",
            Label = "OpenAI",
            DefaultModel = "gpt-5-mini",
            DefaultBaseUrl = "https://api.openai.com/v1",
            NeedsApiKey = true,
            DefaultContextTokens = 400_000,
            ApiKeyPlaceholder = "sk-…",
            ModelPlaceholder = "gpt-5-mini",
            SignupUrl = "https://platform.openai.com/api-keys",
            SignupText = "Create an OpenAI API key",
            PrivacyNote = "Runs in OpenAI's cloud: schema metadata is sent to OpenAI.",
            SuggestedModels = new[] { "gpt-5", "gpt-5-mini", "gpt-5-nano", "gpt-4.1" }
        },
        new LlmProviderInfo
        {
            Provider = LlmProvider.Gemini,
            Name = "Gemini",
            Label = "Google Gemini",
            DefaultModel = "gemini-3.1-flash-lite-preview",
            DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            NeedsApiKey = true,
            DefaultContextTokens = 1_000_000,
            ApiKeyPlaceholder = "AIza…",
            ModelPlaceholder = "gemini-3.1-flash-lite-preview",
            SignupUrl = "https://aistudio.google.com/apikey",
            SignupText = "Get a free API key",
            PrivacyNote = "Runs in Google's cloud: schema metadata is sent to Google Gemini.",
            SuggestedModels = new[] { "gemini-3.1-flash-lite-preview", "gemini-2.5-flash", "gemini-2.5-pro" }
        },
        new LlmProviderInfo
        {
            Provider = LlmProvider.DeepSeek,
            Name = "DeepSeek",
            Label = "DeepSeek",
            DefaultModel = "deepseek-chat",
            DefaultBaseUrl = "https://api.deepseek.com",
            NeedsApiKey = true,
            DefaultContextTokens = 128_000,
            ApiKeyPlaceholder = "sk-…",
            ModelPlaceholder = "deepseek-chat",
            SignupUrl = "https://platform.deepseek.com/api_keys",
            SignupText = "Create a DeepSeek API key",
            PrivacyNote = "Runs in DeepSeek's cloud: schema metadata is sent to DeepSeek. Repeated schema prefixes are served from its context cache at a lower price.",
            SuggestedModels = new[] { "deepseek-chat", "deepseek-reasoner" }
        },
        new LlmProviderInfo
        {
            Provider = LlmProvider.OpenAICompatible,
            Name = "OpenAI-compatible",
            Label = "OpenAI-compatible",
            DefaultModel = "",
            DefaultBaseUrl = "http://localhost:1234/v1",
            NeedsApiKey = true,
            ApiKeyOptional = true,
            ShowsBaseUrl = true,
            UserDefinedContext = true,
            ApiKeyPlaceholder = "optional",
            ModelPlaceholder = "model id",
            PrivacyNote = "Any server that speaks the OpenAI chat API: LM Studio, vLLM, llama.cpp, OpenRouter, Groq, Mistral, Together… Local addresses stay on your network.",
        }
    };

    public static LlmProviderInfo Get(LlmProvider provider) =>
        All.FirstOrDefault(p => p.Provider == provider) ?? All[0];
}
