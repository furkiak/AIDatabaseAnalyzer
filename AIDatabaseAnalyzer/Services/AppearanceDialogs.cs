namespace AIDatabaseAnalyzer.Services;

public static class AppearanceDialogs
{
    public static async Task DisplayAppAlertAsync(this Page page, string title, string message, string cancel)
    {
#if WINDOWS
        await MainThread.InvokeOnMainThreadAsync(() => Platforms.Windows.NativeDialogs.ShowAsync(page, title, message, null, cancel));
#else
        await page.DisplayAlert(title, message, cancel);
#endif
    }

    public static Task<bool> DisplayAppAlertAsync(this Page page, string title, string message, string accept, string cancel)
    {
#if WINDOWS
        return MainThread.InvokeOnMainThreadAsync(() => Platforms.Windows.NativeDialogs.ShowAsync(page, title, message, accept, cancel));
#else
        return page.DisplayAlert(title, message, accept, cancel);
#endif
    }
}
