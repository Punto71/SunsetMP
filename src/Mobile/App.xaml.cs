using Microsoft.Extensions.DependencyInjection;
using SunsetMP.Services;

namespace SunsetMP;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark; // дизайн тёмный: диалоги и системные элементы тоже
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

        // Приложение ушло в фон — сохраняем плейлист, текущий трек и позицию
        window.Stopped += (_, _) => IPlatformApplication.Current?.Services.GetService<PlaybackService>()?.SaveNow();
        return window;
    }
}
