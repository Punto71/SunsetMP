using SunsetMP.Services;

namespace SunsetMP.Controls;

/// <summary>
/// Тонкая полоса перемотки как в index.html. Тап — переход в точку, перетаскивание — перемотка.
/// Progress — доля 0..1.
/// </summary>
public partial class SeekBar : ContentView
{
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress), typeof(double), typeof(SeekBar), 0.0,
        propertyChanged: (b, _, _) => ((SeekBar)b).UpdateVisual());

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Пользователь начал тянуть (значение — доля)</summary>
    public event EventHandler<double>? SeekStarted;
    /// <summary>Пользователь тянет (значение — доля)</summary>
    public event EventHandler<double>? SeekChanged;
    /// <summary>Пользователь отпустил или тапнул (значение — доля)</summary>
    public event EventHandler<double>? SeekCompleted;

    bool _dragging;
    double _dragStart, _dragValue;

    public SeekBar()
    {
        InitializeComponent();
        SwipeNavigation.Block(this);
        Root.SizeChanged += (_, _) => UpdateVisual();
    }

    void UpdateVisual()
    {
        var w = Root.Width;
        if (w <= 0) return;
        var p = Math.Clamp(_dragging ? _dragValue : Progress, 0, 1);
        Fill.WidthRequest = w * p;
        Thumb.TranslationX = w * p - 7;
    }

    void OnTapped(object? sender, TappedEventArgs e)
    {
        var pos = e.GetPosition(Root);
        if (pos == null || Root.Width <= 0) return;
        var f = Math.Clamp(pos.Value.X / Root.Width, 0, 1);
        SeekCompleted?.Invoke(this, f);
    }

    void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        if (Root.Width <= 0) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _dragging = true;
                _dragStart = Progress;
                _dragValue = _dragStart;
                SeekStarted?.Invoke(this, _dragValue);
                break;

            case GestureStatus.Running:
                if (!_dragging) break;
                _dragValue = Math.Clamp(_dragStart + e.TotalX / Root.Width, 0, 1);
                UpdateVisual();
                SeekChanged?.Invoke(this, _dragValue);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (!_dragging) break;
                _dragging = false;
                SeekCompleted?.Invoke(this, _dragValue);
                break;
        }
    }
}
