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

        builder.Services.AddHttpClient<ILlmService, LlmService>();


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