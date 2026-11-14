using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace SunsetMP.Controls;

/// <summary>Круглая кнопка с векторной иконкой (SVG path, viewBox 24x24) — как в index.html.</summary>
public partial class IconButton : ContentView
{
    static readonly PathGeometryConverter Converter = new();

    public static readonly BindableProperty IconDataProperty = BindableProperty.Create(
        nameof(IconData), typeof(string), typeof(IconButton), "", propertyChanged: (b, _, _) => ((IconButton)b).Apply());
    public static readonly BindableProperty ButtonSizeProperty = BindableProperty.Create(
        nameof(ButtonSize), typeof(double), typeof(IconButton), 52.0, propertyChanged: (b, _, _) => ((IconButton)b).Apply());
    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(IconButton), 20.0, propertyChanged: (b, _, _) => ((IconButton)b).Apply());
    /// <summary>Glass — полупрозрачный круг, Accent — градиент, None — без фона</summary>
    public static readonly BindableProperty KindProperty = BindableProperty.Create(
        nameof(Kind), typeof(string), typeof(IconButton), "Glass", propertyChanged: (b, _, _) => ((IconButton)b).Apply());
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(IconButton), null);

    public string IconData { get => (string)GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }
    public double ButtonSize { get => (double)GetValue(ButtonSizeProperty); set => SetValue(ButtonSizeProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    public event EventHandler? Clicked;

    public IconButton()
    {
        InitializeComponent();
        Apply();
    }

    void Apply()
    {
        if (Circle == null) return;

        var s = ButtonSize;
        Circle.WidthRequest = s;
        Circle.HeightRequest = s;
        Circle.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(s / 2) };

        Circle.Background = Kind switch
        {
            "Accent" => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb("#667eea"), 0f),
                    new GradientStop(Color.FromArgb("#764ba2"), 1f)
                },
                new Point(0, 0), new Point(1, 1)),
            "Glass" => new SolidColorBrush(Color.FromArgb("#33FFFFFF")),
            _ => new SolidColorBrush(Colors.Transparent)
        };

        Icon.WidthRequest = IconSize;
        Icon.HeightRequest = IconSize;
        if (!string.IsNullOrEmpty(IconData))
            Icon.Data = (Geometry?)Converter.ConvertFromInvariantString(IconData);
    }

    async void OnTapped(object? sender, TappedEventArgs e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
        var cmd = Command;
        if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);

        try
        {
            await Circle.ScaleTo(0.9, 60);
            await Circle.ScaleTo(1.0, 80);
        }
        catch { /* анимация не критична */ }
    }
}
