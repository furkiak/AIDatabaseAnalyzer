using System.Runtime.CompilerServices;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using NativeStyle = Microsoft.UI.Xaml.Style;
using NativeSetter = Microsoft.UI.Xaml.Setter;
using NativeApplication = Microsoft.UI.Xaml.Application;
using NativeButton = Microsoft.UI.Xaml.Controls.Button;
using NativeScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using NativeScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode;

namespace AIDatabaseAnalyzer.Platforms.Windows;

internal static class NativeDialogs
{
    private static readonly ConditionalWeakTable<Microsoft.UI.Xaml.Window, SemaphoreSlim> Gates = new();

    public static async Task<bool> ShowAsync(Microsoft.Maui.Controls.Page page, string title, string message, string? accept, string cancel)
    {
        if (page.Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            throw new InvalidOperationException("The alert's page must belong to an open app window.");

        var gate = Gates.GetValue(nativeWindow, _ => new(1, 1));
        await gate.WaitAsync();
        try
        {
            var root = nativeWindow.Content?.XamlRoot
                ?? throw new InvalidOperationException("The alert's app window has no native content root.");
            var app = Microsoft.Maui.Controls.Application.Current!;
            var bodySize = (double)app.Resources["Font14"];
            var titleSize = (double)app.Resources["Font20"];
            var maxHeight = Math.Max(0, root.Size.Height - 40);
            var messageText = new TextBlock
            {
                Text = message ?? string.Empty,
                FontSize = bodySize,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                MaxLines = 0,
                Foreground = ((Color)app.Resources["TextColor"]).ToPlatform()
            };
            var content = new ScrollViewer
            {
                Content = messageText,
                MaxHeight = Math.Max(0, maxHeight - titleSize * 2 - bodySize * 3 - 120),
                HorizontalScrollBarVisibility = NativeScrollBarVisibility.Disabled,
                HorizontalScrollMode = NativeScrollMode.Disabled,
                VerticalScrollBarVisibility = NativeScrollBarVisibility.Auto,
                VerticalScrollMode = NativeScrollMode.Auto,
                IsTabStop = true
            };
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                RequestedTheme = app.UserAppTheme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light,
                Title = new TextBlock
                {
                    Text = title ?? string.Empty,
                    FontSize = titleSize,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    Foreground = ((Color)app.Resources["TextColor"]).ToPlatform()
                },
                Content = content,
                PrimaryButtonText = accept ?? string.Empty,
                SecondaryButtonText = cancel,
                DefaultButton = ContentDialogButton.None,
                FontSize = bodySize,
                Background = ((Color)app.Resources["SurfaceColor"]).ToPlatform(),
                FlowDirection = page.FlowDirection == Microsoft.Maui.FlowDirection.RightToLeft
                    ? Microsoft.UI.Xaml.FlowDirection.RightToLeft : Microsoft.UI.Xaml.FlowDirection.LeftToRight
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dialog, title ?? string.Empty);
            dialog.Resources["ContentDialogMaxHeight"] = maxHeight;
            dialog.Resources["ContentDialogMaxWidth"] = Math.Max(0, Math.Min(560, root.Size.Width - 40));
            dialog.PrimaryButtonStyle = ButtonStyle(dialog.PrimaryButtonStyle, bodySize);
            dialog.SecondaryButtonStyle = ButtonStyle(dialog.SecondaryButtonStyle, bodySize);
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally { gate.Release(); }
    }

    private static NativeStyle ButtonStyle(NativeStyle? original, double fontSize)
    {
        if (original is null && NativeApplication.Current.Resources.TryGetValue("DefaultButtonStyle", out var resource))
            original = resource as NativeStyle;
        var style = new NativeStyle(typeof(NativeButton)) { BasedOn = original };
        style.Setters.Add(new NativeSetter(Control.FontSizeProperty, fontSize));
        style.Setters.Add(new NativeSetter(FrameworkElement.MinHeightProperty, 48d));
        style.Setters.Add(new NativeSetter(Control.PaddingProperty, new Microsoft.UI.Xaml.Thickness(14, 10, 14, 10)));
        style.Setters.Add(new NativeSetter(Control.UseSystemFocusVisualsProperty, true));
        style.Setters.Add(new NativeSetter(ContentControl.ContentTemplateProperty, (Microsoft.UI.Xaml.DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding}' TextWrapping='Wrap' TextAlignment='Center' MaxLines='0'/></DataTemplate>")));
        return style;
    }
}
