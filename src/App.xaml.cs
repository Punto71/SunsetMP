namespace SunsetMP;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark; // дизайн тёмный: диалоги и системные элементы тоже
    }

    protected override Window CreateWindow(IActivationState? activationState) => new Window(new AppShell());
}
