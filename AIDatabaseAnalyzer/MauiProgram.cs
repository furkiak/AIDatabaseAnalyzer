using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Infrastructure;
using AIDatabaseAnalyzer.Services.Llm;
using AIDatabaseAnalyzer.Services.Reporting;
using AIDatabaseAnalyzer.ViewModels;
using Microsoft.Extensions.Logging;

namespace AIDatabaseAnalyzer;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        ConfigurePlatformControls();

        // --- infrastructure (singletons: they hold state or are stateless)
        builder.Services.AddSingleton<ISecretStore, SecretStore>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<ILlmSettingsProvider>(sp => sp.GetRequiredService<ISettingsService>());
        builder.Services.AddSingleton<IConnectionProfileStore, ConnectionProfileStore>();
        builder.Services.AddSingleton<IReportStore, ReportStore>();
        builder.Services.AddSingleton<IFileExportService, FileExportService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<INoticeService, NoticeService>();
        builder.Services.AddSingleton<IThemeService, ThemeService>();

        // --- domain services
        builder.Services.AddSingleton<IDbSchemaService, DbSchemaService>();
        builder.Services.AddSingleton<IPdfReportService>(_ => new PdfReportService(OpenMonoFont));
        builder.Services.AddHttpClient<ILlmService, LlmService>()
            // the client lives as long as the app: recycle pooled connections so DNS changes are picked up
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        builder.Services.AddSingleton<IAnalysisRunner, AnalysisRunner>();

        // --- view models: one instance per window, shared through the session
        builder.Services.AddSingleton<WorkspaceSession>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<ConnectViewModel>();
        builder.Services.AddSingleton<ScopeViewModel>();
        builder.Services.AddSingleton<InsightsViewModel>();
        builder.Services.AddSingleton<AnalysisViewModel>();
        builder.Services.AddSingleton<ReportsViewModel>();
        builder.Services.AddSingleton<DiagramViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>Code blocks in PDF reports use a bundled monospace font so the output looks the same on every machine.</summary>
    private static Stream? OpenMonoFont()
    {
        try
        {
            return FileSystem.OpenAppPackageFileAsync("DejaVuSansMono.ttf").GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Removes the native borders of text inputs so they blend into the glass fields drawn by the styles.</summary>
    private static void ConfigurePlatformControls()
    {
#if WINDOWS
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("GlassEntry", (handler, _) =>
        {
            var box = handler.PlatformView;
            var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            foreach (string key in new[]
            {
                "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused"
            })
            {
                box.Resources[key] = clear;
            }
            box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        });

        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("GlassEditor", (handler, _) =>
        {
            var box = handler.PlatformView;
            var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            foreach (string key in new[]
            {
                "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused"
            })
            {
                box.Resources[key] = clear;
            }
            box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        });
#endif
    }
}
