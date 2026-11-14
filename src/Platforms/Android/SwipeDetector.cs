using Android.Views;
using SunsetMP.Services;

namespace SunsetMP;

/// <summary>
/// Смотрит на касания всего окна до того, как их заберёт список или другой элемент,
/// поэтому свайп работает и поверх прокручиваемых списков.
/// </summary>
class SwipeDetector
{
    const float MinDistanceDp = 80f;     // минимальная длина свайпа
    const float HorizontalRatio = 1.8f;  // насколько движение по X должно преобладать над Y
    const long MaxDurationMs = 800;

    float _downX, _downY;
    long _downTime;
    bool _tracking;
    SwipeNavigation.Zone? _zone;
    readonly int[] _loc = new int[2];

    public void OnTouch(MotionEvent ev, float density)
    {
        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                _tracking = true;
                _downX = ev.RawX;
                _downY = ev.RawY;
                _downTime = ev.EventTime;
                _zone = FindZone(ev.RawX, ev.RawY);
                break;

            case MotionEventActions.PointerDown:   // второй палец — это не свайп
            case MotionEventActions.Cancel:
                _tracking = false;
                break;

            case MotionEventActions.Up:
                if (!_tracking) break;
                _tracking = false;

                var dx = ev.RawX - _downX;
                var dy = ev.RawY - _downY;
                if (Math.Abs(dx) < MinDistanceDp * density) break;
                if (Math.Abs(dx) < HorizontalRatio * Math.Abs(dy)) break;
                if (ev.EventTime - _downTime > MaxDurationMs) break;

                var direction = dx < 0 ? 1 : -1;   // палец влево → «вперёд»
                if (_zone == null) SwipeNavigation.SwitchTab(direction);
                else _zone.OnSwipe?.Invoke(direction);
                break;
        }
    }

    SwipeNavigation.Zone? FindZone(float x, float y)
    {
        foreach (var zone in SwipeNavigation.Zones)
        {
            if (!zone.View.TryGetTarget(out var element)) continue;
            if (element.Handler?.PlatformView is not Android.Views.View view) continue;
            if (!view.IsShown || view.Width == 0) continue;   // элемент на другой вкладке

            view.GetLocationOnScreen(_loc);
            if (x >= _loc[0] && x <= _loc[0] + view.Width &&
                y >= _loc[1] && y <= _loc[1] + view.Height)
                return zone;
        }
        return null;
    }
}
