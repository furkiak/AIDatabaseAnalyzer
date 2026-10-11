namespace AIDatabaseAnalyzer.Services.Reporting;

/// <summary>Design tokens shared by every HTML surface (report viewer, ER diagram) so they match the app.</summary>
public static class HtmlTheme
{
    public const string FontStack =
        "-apple-system, BlinkMacSystemFont, 'SF Pro Text', 'Segoe UI Variable Text', 'Segoe UI', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif";

    public const string MonoStack = "ui-monospace, 'SF Mono', Menlo, Consolas, 'Cascadia Mono', monospace";

    private const string LightTokens = """
        --bg-a:#E9F0FF; --bg-b:#F4ECFF; --bg-c:#E4F8F1;
        --text:#1D1D1F; --text-2:#55555E; --text-3:#8A8A93;
        --glass:rgba(255,255,255,.64); --glass-strong:rgba(255,255,255,.86); --stroke:rgba(255,255,255,.9);
        --line:rgba(60,60,67,.14); --accent:#0A6CFF; --accent-2:#5E5CE6; --accent-soft:rgba(10,108,255,.12);
        --good:#1F9D55; --warn:#C77700; --bad:#D92D20; --info:#2E7CF6;
        --code-bg:rgba(60,60,67,.07); --shadow:0 10px 40px rgba(31,41,90,.12); --surface:#F7F8FF;
        --blob-1:rgba(10,132,255,.22); --blob-2:rgba(191,90,242,.17); --blob-3:rgba(48,209,88,.13);
        """;

    private const string DarkTokens = """
        --bg-a:#090D1B; --bg-b:#15122F; --bg-c:#0A2130;
        --text:#F5F5F7; --text-2:#B7B7C2; --text-3:#85858F;
        --glass:rgba(255,255,255,.07); --glass-strong:rgba(255,255,255,.12); --stroke:rgba(255,255,255,.17);
        --line:rgba(255,255,255,.12); --accent:#4C9DFF; --accent-2:#8B88FF; --accent-soft:rgba(76,157,255,.18);
        --good:#43D17A; --warn:#FFB340; --bad:#FF6B61; --info:#64B5FF;
        --code-bg:rgba(255,255,255,.08); --shadow:0 12px 40px rgba(0,0,0,.5); --surface:#1B1A3B;
        --blob-1:rgba(10,132,255,.30); --blob-2:rgba(125,122,255,.24); --blob-3:rgba(48,209,200,.16);
        """;

    /// <summary>CSS custom properties for light, dark and automatic (prefers-color-scheme) themes.</summary>
    public static string Variables =>
        ":root{" + LightTokens + "color-scheme:light;}\n" +
        "@media (prefers-color-scheme: dark){:root:not([data-theme=light]){" + DarkTokens + "color-scheme:dark;}}\n" +
        ":root[data-theme=dark]{" + DarkTokens + "color-scheme:dark;}\n";

    /// <summary>The aurora background used behind glass surfaces.</summary>
    public const string AuroraBackground =
        "background:radial-gradient(1100px 700px at 6% -8%, var(--blob-1), transparent 60%)," +
        "radial-gradient(900px 600px at 102% 2%, var(--blob-2), transparent 55%)," +
        "radial-gradient(900px 700px at 42% 112%, var(--blob-3), transparent 60%)," +
        "linear-gradient(135deg, var(--bg-a), var(--bg-b) 60%, var(--bg-c)) fixed;";
}
