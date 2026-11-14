using SunsetMP.Models;
using SunsetMP.Services;

namespace SunsetMP.Pages;

public partial class PlayerPage : ContentPage
{
    readonly PlaybackService _svc;

    public PlayerPage(PlaybackService svc)
    {
        InitializeComponent();
        BindingContext = _svc = svc;

        // Свайп по обложке и названию меняет трек (вместо переключения вкладки)
        SwipeNavigation.Handle(TrackInfo, OnTrackSwipe);
    }

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
