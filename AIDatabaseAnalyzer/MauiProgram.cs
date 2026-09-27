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
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Button, Platforms.Windows.WrappingButtonHandler>());
        Microsoft.Maui.Controls.Handlers.ShellItemHandler.Mapper.AppendToMapping(nameof(ShellItem.CurrentItem), (handler, _) =>
        {
            if (handler.PlatformView is Microsoft.UI.Xaml.Controls.NavigationView navigation)
                Platforms.Windows.NativeAppearance.AttachNavigation(navigation);
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
