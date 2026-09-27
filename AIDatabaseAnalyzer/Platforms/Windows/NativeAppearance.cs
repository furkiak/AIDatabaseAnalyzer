using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NativeStyle = Microsoft.UI.Xaml.Style;
using NativeSetter = Microsoft.UI.Xaml.Setter;

namespace AIDatabaseAnalyzer.Platforms.Windows;

internal static class NativeAppearance
{
    private static readonly ConditionalWeakTable<NavigationView, NavigationState> NavigationStates = new();
    private static readonly ConditionalWeakTable<ItemsRepeater, object> Repeaters = new();
    private static readonly ConditionalWeakTable<NavigationViewItem, object> Items = new();

    public static void ApplyWindow(Microsoft.Maui.Controls.Window window)
    {
        if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow
            || nativeWindow.Content is not FrameworkElement root) return;
        root.RequestedTheme = Theme;
        foreach (var navigation in Descendants(root).OfType<NavigationView>())
            AttachNavigation(navigation);
    }

    public static void AttachNavigation(NavigationView navigation)
    {
        if (!NavigationStates.TryGetValue(navigation, out var state))
        {
            state = new(navigation.MenuItemContainerStyle);
            NavigationStates.Add(navigation, state);
            navigation.Loaded += (_, _) => ApplyNavigation(navigation);
        }
        ApplyNavigation(navigation);
    }

    private static ElementTheme Theme => Microsoft.Maui.Controls.Application.Current?.UserAppTheme == AppTheme.Dark
        ? ElementTheme.Dark : ElementTheme.Light;
    private static double FontSize => Microsoft.Maui.Controls.Application.Current?.Resources["Font14"] is double size ? size : 14;

    private static void ApplyNavigation(NavigationView navigation)
    {
        var size = FontSize;
        navigation.RequestedTheme = Theme;
        navigation.FontSize = size;
        navigation.Resources["ControlContentThemeFontSize"] = size;
        // WinUI 1.7's TopNavGrid otherwise fixes the entire tab row at 48px.
        navigation.Resources["NavigationViewTopPaneHeight"] = Math.Max(48, size * 1.5 + 16);
        if (NavigationStates.TryGetValue(navigation, out var state) && state.FontSize != size)
        {
            var style = new NativeStyle(typeof(NavigationViewItem)) { BasedOn = state.BaseStyle };
            style.Setters.Add(new NativeSetter(Control.FontSizeProperty, size));
            style.Setters.Add(new NativeSetter(FrameworkElement.MinHeightProperty, Math.Max(48, size * 1.5 + 16)));
            style.Setters.Add(new NativeSetter(Control.UseSystemFocusVisualsProperty, true));
            navigation.MenuItemContainerStyle = style;
            state.FontSize = size;
        }
        foreach (var item in Descendants(navigation).OfType<NavigationViewItem>()) ApplyItem(item);
        foreach (var repeater in Descendants(navigation).OfType<ItemsRepeater>()
            .Where(repeater => repeater.Name is "TopNavMenuItemsHost" or "TopNavMenuItemsOverflowHost" or "MenuItemsHost"))
        {
            if (Repeaters.TryGetValue(repeater, out _)) continue;
            Repeaters.Add(repeater, new());
            repeater.ElementPrepared += (_, args) =>
            {
                if (args.Element is NavigationViewItem item) ApplyItem(item);
            };
        }
        navigation.InvalidateMeasure();
    }

    private static void ApplyItem(NavigationViewItem item)
    {
        if (!Items.TryGetValue(item, out _))
        {
            Items.Add(item, new());
            item.Loaded += (_, _) => ApplyItem(item);
        }
        item.FontSize = FontSize;
        item.MinHeight = Math.Max(48, FontSize * 1.5 + 16);
        foreach (var text in Descendants(item).OfType<TextBlock>())
        {
            text.FontSize = FontSize;
            text.MaxLines = 0;
            text.TextTrimming = Microsoft.UI.Xaml.TextTrimming.None;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class NavigationState(NativeStyle? baseStyle)
    {
        public NativeStyle? BaseStyle { get; } = baseStyle;
        public double FontSize { get; set; }
    }
}
