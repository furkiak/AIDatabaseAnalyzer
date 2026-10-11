namespace AIDatabaseAnalyzer.Services.Infrastructure;

/// <summary>
/// Pages shown in a WebView (diagram, report, chat) talk to the app by navigating to "aida://command/argument".
/// The view cancels that navigation and hands the command to its view model.
/// </summary>
public static class HostLinks
{
    public const string Scheme = "aida://";

    public static bool TryParse(string url, out string command, out string argument)
    {
        command = argument = "";
        if (!url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return false;

        string rest = url[Scheme.Length..];
        int slash = rest.IndexOf('/');
        command = (slash < 0 ? rest : rest[..slash]).ToLowerInvariant();
        argument = slash < 0 ? "" : Uri.UnescapeDataString(rest[(slash + 1)..]);
        return true;
    }

    /// <summary>Cancels host links and forwards them; opens web links in the browser instead of inside the page.</summary>
    public static void Attach(WebView webView, Func<string, string, Task> handler)
    {
        webView.Navigating += (_, e) =>
        {
            if (TryParse(e.Url, out string command, out string argument))
            {
                e.Cancel = true;
                MainThread.BeginInvokeOnMainThread(async () => await handler(command, argument));
                return;
            }

            if (e.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || e.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || e.Url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                _ = Launcher.Default.TryOpenAsync(new Uri(e.Url));
            }
        };
    }
}
