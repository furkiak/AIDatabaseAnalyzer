namespace AIDatabaseAnalyzer.Services.Infrastructure;

/// <summary>Dialogs and notifications, kept behind an interface so view models never touch the UI directly.</summary>
public interface IDialogService
{
    Task AlertAsync(string title, string message, string accept = "OK");
    Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel");
}

public sealed class DialogService : IDialogService
{
    public async Task AlertAsync(string title, string message, string accept = "OK")
    {
        var page = CurrentPage();
        if (page is null) return;
        await MainThread.InvokeOnMainThreadAsync(() => page.DisplayAlert(title, message, accept));
    }

    public async Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
    {
        var page = CurrentPage();
        if (page is null) return false;
        return await MainThread.InvokeOnMainThreadAsync(() => page.DisplayAlert(title, message, accept, cancel));
    }

    private static Page? CurrentPage() => Application.Current?.Windows.FirstOrDefault()?.Page;
}

public enum NoticeKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Non-blocking toast messages shown at the bottom of the window.</summary>
public interface INoticeService
{
    event Action<string, NoticeKind>? NoticeRaised;
    void Show(string message, NoticeKind kind = NoticeKind.Info);
}

public sealed class NoticeService : INoticeService
{
    public event Action<string, NoticeKind>? NoticeRaised;

    public void Show(string message, NoticeKind kind = NoticeKind.Info) => NoticeRaised?.Invoke(message, kind);
}

/// <summary>Applies the user's theme choice (system / light / dark).</summary>
public interface IThemeService
{
    void Apply(ThemeMode mode);

    /// <summary>"light" or "dark": what is currently shown, for HTML surfaces.</summary>
    string EffectiveTheme { get; }
}

public sealed class ThemeService : IThemeService
{
    public void Apply(ThemeMode mode)
    {
        var app = Application.Current;
        if (app is null) return;
        app.UserAppTheme = mode switch
        {
            ThemeMode.Light => AppTheme.Light,
            ThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }

    public string EffectiveTheme => Application.Current?.RequestedTheme == AppTheme.Dark ? "dark" : "light";
}
