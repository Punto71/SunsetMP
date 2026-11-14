using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace SunsetMP;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    readonly SwipeDetector _swipe = new();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Android 13+: без этого разрешения уведомление с управлением плеером не покажется
        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
        {
            RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, 1001);
        }
    }

    // Свайпы по всему окну: переключение вкладок и смена трека
    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev != null)
        {
            try { _swipe.OnTouch(ev, Resources?.DisplayMetrics?.Density ?? 2f); }
            catch { /* жесты не должны ронять приложение */ }
        }
        return base.DispatchTouchEvent(ev);
    }
}
