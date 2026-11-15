using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AGD = Android.Graphics.Drawables;

namespace SunsetMP;

/// <summary>Держит процесс живым во время скачивания и показывает прогресс в шторке уведомлений.</summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public class DownloadForegroundService : Service
{
    const string ChannelId = "downloads";
    const int NotificationId = 1002;
    const string ActCancel = "sunsetmp.DL_CANCEL";

    public static DownloadForegroundService? Instance { get; private set; }

    public static string NowText = "";
    public static int NowPercent;

    /// <summary>Нажата кнопка «Отмена» в уведомлении</summary>
    public static event Action? CancelRequested;

    bool _foreground;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        Instance = this;

        var channel = new NotificationChannel(ChannelId, "Скачивание", NotificationImportance.Low);
        channel.SetShowBadge(false);
        (GetSystemService(NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActCancel) CancelRequested?.Invoke();
        Refresh();
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public void Shutdown()
    {
        if (Instance == this) Instance = null;
        _foreground = false;
        try { StopForeground(StopForegroundFlags.Remove); } catch { }
        StopSelf();
    }

    public void Refresh()
    {
        try
        {
            var notification = BuildNotification();
            if (!_foreground)
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                    StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
                else
                    StartForeground(NotificationId, notification);
                _foreground = true;
            }
            else
            {
                (GetSystemService(NotificationService) as NotificationManager)?.Notify(NotificationId, notification);
            }
        }
        catch { }
    }

    Notification BuildNotification()
    {
        var cancelIntent = new Intent(this, typeof(DownloadForegroundService));
        cancelIntent.SetAction(ActCancel);
        var cancel = PendingIntent.GetService(this, 10, cancelIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var open = PendingIntent.GetActivity(this, 11, launch,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var cancelAction = new Notification.Action.Builder(
            AGD.Icon.CreateWithResource(this, Android.Resource.Drawable.IcMenuCloseClearCancel), "Отмена", cancel).Build()!;

        return new Notification.Builder(this, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownload)!
            .SetContentTitle("Скачивание в Music/SunsetMP")!
            .SetContentText(NowText)!
            .SetProgress(100, NowPercent, false)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetShowWhen(false)!
            .SetContentIntent(open)!
            .AddAction(cancelAction)!
            .Build()!;
    }
}
