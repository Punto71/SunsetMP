using SunsetMP.Pages;
using SunsetMP.Services;
using SunsetMP.ViewModels;

namespace SunsetMP;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Платформенные реализации (Platforms/Android)
        builder.Services.AddSingleton<IAudioEngine, AndroidAudioEngine>();
        builder.Services.AddSingleton<ILocalMusicScanner, MediaStoreScanner>();
        builder.Services.AddSingleton<IFileSaver, AndroidFileSaver>();
        builder.Services.AddSingleton<IDownloadHost, AndroidDownloadHost>();

        // Сервисы и view-model
        builder.Services.AddSingleton<PlaybackService>();
        builder.Services.AddSingleton<ServerClient>();
        builder.Services.AddSingleton<DownloadService>();
        builder.Services.AddSingleton<LocalLibraryViewModel>();
        builder.Services.AddSingleton<ServerViewModel>();

        // Страницы
        builder.Services.AddSingleton<PlayerPage>();
        builder.Services.AddSingleton<LocalPage>();
        builder.Services.AddSingleton<ServerPage>();

        return builder.Build();
    }
}
