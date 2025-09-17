using Microsoft.Extensions.Logging;
using MyMetronom.Services;
using MyMetronom.Utils;
using CommunityToolkit.Maui;

namespace MyMetronom
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Services
            builder.Services.AddSingleton<IBeepService, BeepService>();
#if ANDROID
            builder.Services.AddSingleton<IMetronomeService, AndroidMetronomeService>();
#else
            builder.Services.AddSingleton<IMetronomeService, MetronomeService>();
#endif

#if DEBUG
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

            var app = builder.Build();
            ServiceHelper.Services = app.Services;
            return app;
        }
    }
}
