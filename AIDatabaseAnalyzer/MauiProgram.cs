using AIDatabaseAnalyzer.Services;
using AIDatabaseAnalyzer.ViewModels;

namespace AIDatabaseAnalyzer;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        var profile = AppProfile.Resolve(Environment.GetCommandLineArgs().Skip(1).ToArray(), FileSystem.Current.AppDataDirectory);
        builder.Services.AddSingleton(profile);
        builder.Services.AddSingleton<IAppSettingsStore>(AppSettingsStoreFactory.Create(profile, () => new MauiSettingsStore()));
        builder.Services.AddSingleton<AppearanceController>();
        builder.Services.AddHttpClient<ILlmService, LlmService>();
#if WINDOWS
        Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(nameof(Button.Text), (handler, _) =>
        {
            if (Microsoft.Maui.Platform.ButtonExtensions.GetContent<Microsoft.UI.Xaml.Controls.TextBlock>(handler.PlatformView) is { } text)
                text.TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap;
        });
#endif


        builder.Services.AddSingleton<DbSchemaService>(); 
        builder.Services.AddSingleton<PdfReportService>();
        builder.Services.AddSingleton<IDbSchemaService, DbSchemaService>();
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<MainPage>(); 
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();
        return builder.Build();
    }
}
