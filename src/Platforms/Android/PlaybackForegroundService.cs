using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using AG = Android.Graphics;
using AGD = Android.Graphics.Drawables;

namespace SunsetMP;

/// <summary>Получает системное «звук становится шумным» (отключили BT/проводные наушники).</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class NoisyReceiver : BroadcastReceiver
{
    public Action? Noisy;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == AudioManager.ActionAudioBecomingNoisy) Noisy?.Invoke();
    }
}

/// <summary>
/// Foreground-сервис воспроизведения: держит процесс живым, владеет MediaSession
/// (кнопки гарнитуры, экран блокировки) и показывает media-уведомление в шторке.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public class PlaybackForegroundService : Service
{
    const string ChannelId = "playback";
    const int NotificationId = 1001;
    const string ActPrev = "SunsetMP.PREV";
    const string ActToggle = "SunsetMP.TOGGLE";
    const string ActNext = "SunsetMP.NEXT";

    public static PlaybackForegroundService? Instance { get; private set; }

    // Состояние, которое выставляет AndroidAudioEngine
    public static string NowTitle = "", NowArtist = "";
    public static long NowDurationMs, NowPositionMs;
    public static bool NowIsPlaying;

    /// <summary>"play" | "pause" | "toggle" | "next" | "prev" | "seek"</summary>
    public static event Action<string, long>? Command;
    public static void Raise(string cmd, long arg = 0) => Command?.Invoke(cmd, arg);

    MediaSession? _session;
    NoisyReceiver? _noisy;
    AG.Bitmap? _cover;
    bool _foreground;

    class SessionCallback : MediaSession.Callback
    {
        public override void OnPlay() => Raise("play");
        public override void OnPause() => Raise("pause");
        public override void OnStop() => Raise("pause");
        public override void OnSkipToNext() => Raise("next");
        public override void OnSkipToPrevious() => Raise("prev");
        public override void OnSeekTo(long pos) => Raise("seek", pos);
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        Instance = this;

        var nm = (NotificationManager?)GetSystemService(NotificationService);
        var channel = new NotificationChannel(ChannelId, "Воспроизведение", NotificationImportance.Low);
        channel.SetShowBadge(false);
        nm?.CreateNotificationChannel(channel);

        _cover = CreateCover();

        _session = new MediaSession(this, "SunsetMP");
#pragma warning disable CA1422
        _session.SetFlags(MediaSessionFlags.HandlesMediaButtons | MediaSessionFlags.HandlesTransportControls);
#pragma warning restore CA1422
        _session.SetCallback(new SessionCallback());
        _session.Active = true;

        _noisy = new NoisyReceiver { Noisy = () => Raise("pause") };
        RegisterReceiver(_noisy, new IntentFilter(AudioManager.ActionAudioBecomingNoisy));
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        switch (intent?.Action)
        {
            case ActPrev: Raise("prev"); break;
            case ActToggle: Raise("toggle"); break;
            case ActNext: Raise("next"); break;
        }
        Refresh();
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        try { if (_noisy != null) UnregisterReceiver(_noisy); } catch { }
        try { if (_session != null) { _session.Active = false; _session.Release(); } } catch { }
        _session = null;
        base.OnDestroy();
    }

    public void Shutdown()
    {
        if (Instance == this) Instance = null;
        _foreground = false;
        try { StopForeground(StopForegroundFlags.Remove); } catch { }
        StopSelf();
    }

    /// <summary>Обновляет MediaSession и уведомление по статическому состоянию.</summary>
    public void Refresh()
    {
        try
        {
            UpdateSession();
            var notification = BuildNotification();
            if (!_foreground)
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                    StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
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

    void UpdateSession()
    {
        if (_session == null) return;

        long actions = PlaybackState.ActionPlay | PlaybackState.ActionPause | PlaybackState.ActionPlayPause |
                       PlaybackState.ActionSkipToNext | PlaybackState.ActionSkipToPrevious |
                       PlaybackState.ActionSeekTo | PlaybackState.ActionStop;

        var state = new PlaybackState.Builder()
            .SetActions(actions)!
            .SetState(NowIsPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused,
                      NowPositionMs, NowIsPlaying ? 1f : 0f)!
            .Build();
        _session.SetPlaybackState(state);

        var meta = new MediaMetadata.Builder()
            .PutString(MediaMetadata.MetadataKeyTitle, NowTitle)!
            .PutString(MediaMetadata.MetadataKeyArtist, NowArtist)!
            .PutLong(MediaMetadata.MetadataKeyDuration, NowDurationMs)!
            .PutBitmap(MediaMetadata.MetadataKeyAlbumArt, _cover)!
            .Build();
        _session.SetMetadata(meta);

        if (!_session.Active) _session.Active = true;
    }

    PendingIntent CommandIntent(string action, int code)
    {
        var i = new Intent(this, typeof(PlaybackForegroundService));
        i.SetAction(action);
        return PendingIntent.GetService(this, code, i, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    Notification.Action MakeAction(int icon, string title, string action, int code)
        => new Notification.Action.Builder(AGD.Icon.CreateWithResource(this, icon), title, CommandIntent(action, code)).Build()!;

    Notification BuildNotification()
    {
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var open = PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var style = new Notification.MediaStyle()
            .SetMediaSession(_session!.SessionToken)!
            .SetShowActionsInCompactView(0, 1, 2);

        return new Notification.Builder(this, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcMediaPlay)!
            .SetContentTitle(string.IsNullOrEmpty(NowTitle) ? "SunsetMP" : NowTitle)!
            .SetContentText(NowArtist)!
            .SetLargeIcon(_cover)!
            .SetContentIntent(open)!
            .SetStyle(style)!
            .SetVisibility(NotificationVisibility.Public)!
            .SetOnlyAlertOnce(true)!
            .SetShowWhen(false)!
            .SetOngoing(NowIsPlaying)!
            .AddAction(MakeAction(Android.Resource.Drawable.IcMediaPrevious, "Назад", ActPrev, 1))!
            .AddAction(MakeAction(NowIsPlaying ? Android.Resource.Drawable.IcMediaPause : Android.Resource.Drawable.IcMediaPlay,
                                  NowIsPlaying ? "Пауза" : "Играть", ActToggle, 2))!
            .AddAction(MakeAction(Android.Resource.Drawable.IcMediaNext, "Вперёд", ActNext, 3))!
            .Build()!;
    }

    /// <summary>Обложка в стиле дизайна: градиент #667eea → #764ba2 и нота.</summary>
    static AG.Bitmap CreateCover()
    {
        const int size = 256;
        var bmp = AG.Bitmap.CreateBitmap(size, size, AG.Bitmap.Config.Argb8888!)!;
        var canvas = new AG.Canvas(bmp);
        var paint = new AG.Paint(AG.PaintFlags.AntiAlias);
        paint.SetShader(new AG.LinearGradient(0, 0, size, size,
            AG.Color.ParseColor("#667eea"), AG.Color.ParseColor("#764ba2"), AG.Shader.TileMode.Clamp!));
        canvas.DrawRect(0, 0, size, size, paint);
        paint.SetShader(null);
        paint.Color = AG.Color.White;
        paint.TextSize = 150;
        paint.TextAlign = AG.Paint.Align.Center!;
        canvas.DrawText("♪", size / 2f, size / 2f + 50, paint);
        return bmp;
    }
}
