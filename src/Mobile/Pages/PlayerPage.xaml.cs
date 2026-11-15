using Microsoft.Maui.Controls.Shapes;
using SunsetMP.Models;
using SunsetMP.Services;

namespace SunsetMP.Pages;

public partial class PlayerPage : ContentPage
{
    /// <summary>Ширина (dp), начиная с которой включается двухколоночный режим (планшет, телефон в ландшафте)</summary>
    const double WideBreakpointDp = 640;

    readonly PlaybackService _svc;
    bool? _wide;
    double _coverSize;

    public PlayerPage(PlaybackService svc)
    {
        InitializeComponent();
        BindingContext = _svc = svc;

        // Свайп по обложке и названию меняет трек (вместо переключения вкладки)
        SwipeNavigation.Handle(TrackInfo, OnTrackSwipe);

        SizeChanged += (_, _) => ApplyLayout();
    }

    // ---------- Адаптивная раскладка ----------
    void ApplyLayout()
    {
        if (Width <= 0 || Height <= 0) return;

        var wide = Width >= WideBreakpointDp;

        // Размер миниатюры в широком режиме: влезает в левую колонку и по высоте вместе с элементами управления
        var cover = 64.0;
        if (wide)
        {
            var leftInner = (Width - 48) * 0.4 - 40;
            cover = Math.Clamp(Math.Min(leftInner, Height - 320), 96, 340);
            cover = Math.Round(cover / 4) * 4;   // не перестраиваем разметку из-за долей пикселя
        }

        if (_wide == wide && Math.Abs(_coverSize - cover) < 1) return;
        _wide = wide;
        _coverSize = cover;

        if (wide)
        {
            // слева — миниатюра и управление, справа — поиск и плейлист
            Root.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Star) };
            Root.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star))
            };
            Grid.SetRow(CardHost, 0);
            Grid.SetColumn(CardHost, 0);
            Grid.SetRow(ListPane, 0);
            Grid.SetColumn(ListPane, 1);
            CardHost.VerticalOptions = LayoutOptions.Center;
        }
        else
        {
            // список сверху, плеер снизу
            Root.ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star) };
            Root.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            };
            Grid.SetRow(ListPane, 0);
            Grid.SetColumn(ListPane, 0);
            Grid.SetRow(CardHost, 1);
            Grid.SetColumn(CardHost, 0);
            CardHost.VerticalOptions = LayoutOptions.Fill;
        }

        ApplyTrackInfo(wide, cover);
    }

    void ApplyTrackInfo(bool wide, double cover)
    {
        if (wide)
        {
            // большая миниатюра сверху, название под ней по центру
            TrackInfo.ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star) };
            TrackInfo.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            };
            TrackInfo.RowSpacing = 14;
            Grid.SetRow(Cover, 0);
            Grid.SetColumn(Cover, 0);
            Grid.SetRow(TrackText, 1);
            Grid.SetColumn(TrackText, 0);

            Cover.HorizontalOptions = LayoutOptions.Center;
            Cover.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28) };
            TitleLabel.FontSize = 22;
            TitleLabel.HorizontalTextAlignment = TextAlignment.Center;
            ArtistLabel.FontSize = 16;
            ArtistLabel.HorizontalTextAlignment = TextAlignment.Center;
        }
        else
        {
            // компактно: маленькая миниатюра слева от названия
            TrackInfo.RowDefinitions = new RowDefinitionCollection();
            TrackInfo.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(64)),
                new ColumnDefinition(GridLength.Star)
            };
            TrackInfo.RowSpacing = 0;
            Grid.SetRow(Cover, 0);
            Grid.SetColumn(Cover, 0);
            Grid.SetRow(TrackText, 0);
            Grid.SetColumn(TrackText, 1);

            Cover.HorizontalOptions = LayoutOptions.Start;
            Cover.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) };
            TitleLabel.FontSize = 18;
            TitleLabel.HorizontalTextAlignment = TextAlignment.Start;
            ArtistLabel.FontSize = 14;
            ArtistLabel.HorizontalTextAlignment = TextAlignment.Start;
        }

        Cover.WidthRequest = cover;
        Cover.HeightRequest = cover;
        CoverNote.FontSize = cover * 0.53;
    }

    // ---------- Список ----------
    void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Track t) _svc.PlayTrack(t);
    }

    void OnRemoveClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Track t) _svc.Remove(t);
    }

    void OnClearClicked(object? sender, EventArgs e) => _svc.Clear();

    /// <param name="direction">+1 — свайп влево (следующий трек), -1 — вправо (предыдущий)</param>
    async void OnTrackSwipe(int direction)
    {
        if (_svc.Playlist.Count == 0) return;
        try
        {
            await TrackInfo.TranslateTo(-direction * 40, 0, 80, Easing.CubicIn);   // название «уезжает»
            if (direction > 0) _svc.Next(auto: false);
            else _svc.Previous(restartIfPlayed: false);
            TrackInfo.TranslationX = direction * 40;
            await TrackInfo.TranslateTo(0, 0, 140, Easing.CubicOut);               // новое «приезжает»
        }
        catch
        {
            TrackInfo.TranslationX = 0;
        }
    }

    // Перемотка: пока тянем — обновляем только время, сама перемотка при отпускании
    void OnSeekStarted(object? sender, double fraction) => _svc.IsSeeking = true;

    void OnSeekChanged(object? sender, double fraction) => _svc.Position = fraction * _svc.Duration;

    void OnSeekCompleted(object? sender, double fraction)
    {
        _svc.SeekTo(fraction * _svc.Duration);
        _svc.IsSeeking = false;
    }
}
