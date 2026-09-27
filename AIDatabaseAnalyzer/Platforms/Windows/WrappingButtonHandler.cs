using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Maui.Graphics;
using NativeButton = Microsoft.UI.Xaml.Controls.Button;

namespace AIDatabaseAnalyzer.Platforms.Windows;

public sealed class WrappingButtonHandler : ButtonHandler
{
    public static new readonly PropertyMapper<IButton, IButtonHandler> Mapper = new(ButtonHandler.Mapper)
    {
        [nameof(IText.Text)] = (handler, button) =>
        {
            if (button is IText text) ButtonHandler.MapText(handler, text);
            ConfigureText(handler.PlatformView);
        },
        [nameof(ITextStyle.Font)] = (handler, button) =>
        {
            if (button is ITextStyle text) ButtonHandler.MapFont(handler, text);
            ConfigureText(handler.PlatformView);
        },
        [nameof(IButton.Padding)] = (handler, button) =>
        {
            ButtonHandler.MapPadding(handler, button);
            ConfigureText(handler.PlatformView);
        }
    };

    public WrappingButtonHandler() : base(Mapper) { }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        ConfigureText(PlatformView);
        if (PlatformView is { } button)
        {
            // ContentPresenter can measure centered content without a width limit.
            // Bound MAUI's existing panel before its TextBlock measures wrapped text.
            var width = double.IsFinite(widthConstraint)
                ? Math.Max(0, widthConstraint - button.Padding.Left - button.Padding.Right
                    - button.BorderThickness.Left - button.BorderThickness.Right)
                : double.PositiveInfinity;
            if (button.Content is FrameworkElement content) content.MaxWidth = width;
            if (button.GetContent<TextBlock>() is { } text) text.MaxWidth = width;
        }
        return base.GetDesiredSize(widthConstraint, heightConstraint);
    }

    private static void ConfigureText(NativeButton? button)
    {
        if (button?.GetContent<TextBlock>() is not { } text) return;
        text.TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap;
        text.TextTrimming = Microsoft.UI.Xaml.TextTrimming.None;
        text.MaxLines = 0;
        text.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center;
    }
}
