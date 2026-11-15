using Android.Content;
using Android.Media;
using Android.OS;
using SunsetMP.Services;

namespace SunsetMP;

class FocusListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
{
    public Action<AudioFocus>? Changed;
    public void OnAudioFocusChange(AudioFocus focusChange) => Changed?.Invoke(focusChange);
}

public class AndroidAudioEngine : IAudioEngine
{
    /// <summary>Если трек не скачался в память за это время (медленная сеть) — играем потоком.</summary>
    const int StreamFallbackAfterMs = 6000;

    readonly TrackCache _cache = new();
    readonly FocusListener _focus = new();

    MediaPlayer? _player;
    ByteArrayMediaDataSource? _source;
    bool _prepared;
    bool _wantPlay;
    bool _loading;                 // трек качается в память, плеер ещё не создан
    int _playToken;                // отличает актуальный запуск от устаревшего
    string _title = "", _artist = "", _currentUrl = "";
    string? _nextUrl;
    long _trackDuration;

    public event Action? Prepared;
    public event Action? Completed;
    public event Action? Error;
    public event Action? FocusLost;
    public event Action? RemotePlay;
    public event Action? RemotePause;
    public event Action? RemoteNext;
    public event Action? RemotePrevious;
    public event Action<long>? RemoteSeek;

    public AndroidAudioEngine()
    {
        _focus.Changed = f =>
        {
            if (f == AudioFocus.Loss || f == AudioFocus.LossTransient)
                MainThread.BeginInvokeOnMainThread(() => FocusLost?.Invoke());
        };
        PlaybackForegroundService.Command += OnCommand;
    }

    public int PositionMs
    {
        get { try { return _prepared && _player != null ? _player.CurrentPosition : 0; } catch { return 0; } }
    }

    public int DurationMs
    {
        get { try { return _prepared && _player != null ? _player.Duration : 0; } catch { return 0; } }
    }

    AudioManager? Audio => Android.App.Application.Context.GetSystemService(Context.AudioService) as AudioManager;

    static bool IsRemote(string? uri) =>
        uri != null && (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    // Команды из MediaSession (гарнитура, шторка, экран блокировки) и из уведомления
    void OnCommand(string cmd, long arg) => MainThread.BeginInvokeOnMainThread(() =>
    {
        switch (cmd)
        {
            case "play": RemotePlay?.Invoke(); break;
            case "pause": RemotePause?.Invoke(); break;
            case "toggle": if (_wantPlay) RemotePause?.Invoke(); else RemotePlay?.Invoke(); break;
            case "next": RemoteNext?.Invoke(); break;
            case "prev": RemotePrevious?.Invoke(); break;
            case "seek": RemoteSeek?.Invoke(arg); break;
        }
    });

    public void Play(string uri, string title, string artist, long durationMs)
    {
        ReleasePlayer();
        _title = title; _artist = artist; _trackDuration = durationMs;
        _currentUrl = uri;
        _wantPlay = true;
        var token = ++_playToken;
        Audio?.RequestAudioFocus(_focus, Android.Media.Stream.Music, AudioFocus.Gain);
        Push(true);

        if (!IsRemote(uri))
        {
            _loading = false;
            StartPlayer(token, uri, null);
            return;
        }

        _cache.SetWanted(uri, _nextUrl);
        if (_cache.TryGet(uri, out var cached))      // был предзагружен — стартуем сразу
        {
            _loading = false;
            StartPlayer(token, uri, cached);
            return;
        }

        _loading = true;
        _ = LoadAndStartAsync(token, uri);
    }

    async Task LoadAndStartAsync(int token, string uri)
    {
        byte[]? data = null;
        try
        {
            var download = _cache.GetAsync(uri);
            var winner = await Task.WhenAny(download, Task.Delay(StreamFallbackAfterMs));
            if (winner == download) data = await download;
            else _cache.Cancel(uri);                 // медленная сеть — не ждём, играем потоком
        }
        catch { /* data == null → поток */ }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (token != _playToken) return;         // за это время выбрали другой трек
            _loading = false;
            StartPlayer(token, uri, data);
        });
    }

    void StartPlayer(int token, string uri, byte[]? data)
    {
        if (token != _playToken) return;

        var ctx = Android.App.Application.Context;
        var p = new MediaPlayer();
        _player = p;
        _prepared = false;

        p.SetAudioAttributes(new AudioAttributes.Builder()!
            .SetContentType(AudioContentType.Music)!
            .SetUsage(AudioUsageKind.Media)!
            .Build()!);
        p.SetWakeMode(ctx, WakeLockFlags.Partial);

        p.Prepared += (s, e) =>
        {
            if (_player != p) return;
            _prepared = true;
            if (_wantPlay) p.Start();
            Push(_wantPlay);          // обновить длительность в уведомлении
            Prepared?.Invoke();
        };
        p.Completion += (s, e) => { if (_player == p) Completed?.Invoke(); };
        p.Error += (s, e) =>
        {
            e.Handled = true;
            if (_player == p) MainThread.BeginInvokeOnMainThread(() => Error?.Invoke());
        };

        try
        {
            if (data != null)
            {
                _source = new ByteArrayMediaDataSource(data);
                p.SetDataSource(_source);
            }
            else
            {
                p.SetDataSource(ctx, Android.Net.Uri.Parse(uri)!);
            }
            p.PrepareAsync();
        }
        catch
        {
            MainThread.BeginInvokeOnMainThread(() => Error?.Invoke());
        }
    }

    public void Preload(string? uri)
    {
        if (!IsRemote(uri))
        {
            _nextUrl = null;
            _cache.SetWanted(_currentUrl, null);
            return;
        }
        _nextUrl = uri;
        _cache.SetWanted(_currentUrl, uri);
        _cache.Start(uri!);
    }

    public void Pause()
    {
        _wantPlay = false;
        if (_player == null && !_loading) return;
        try { if (_prepared) _player?.Pause(); } catch { }
        Push(false);
    }

    public void Resume()
    {
        _wantPlay = true;
        if (_player == null && !_loading) return;
        Audio?.RequestAudioFocus(_focus, Android.Media.Stream.Music, AudioFocus.Gain);
        try { if (_prepared) _player?.Start(); } catch { }
        Push(true);
    }

    public void Stop()
    {
        _playToken++;
        _loading = false;
        _wantPlay = false;
        ReleasePlayer();
        _currentUrl = "";
        _nextUrl = null;
        _cache.SetWanted(null, null);       // освободить память
        try { Audio?.AbandonAudioFocus(_focus); } catch { }
        try
        {
            PlaybackForegroundService.Instance?.Shutdown();
            var ctx = Android.App.Application.Context;
            ctx.StopService(new Intent(ctx, typeof(PlaybackForegroundService)));
        }
        catch { }
    }

    public void SeekTo(int ms)
    {
        if (_player == null) return;
        try { if (_prepared) _player.SeekTo(ms); } catch { }
        Push(_wantPlay);
    }

    void ReleasePlayer()
    {
        var p = _player;
        _player = null;
        _prepared = false;
        if (p == null) { _source = null; return; }
        try { p.Reset(); p.Release(); } catch { }
        _source = null;
    }

    /// <summary>Передаёт текущее состояние в foreground-сервис (MediaSession + уведомление).</summary>
    void Push(bool playing)
    {
        PlaybackForegroundService.NowTitle = _title;
        PlaybackForegroundService.NowArtist = _artist;
        PlaybackForegroundService.NowDurationMs = DurationMs > 0 ? DurationMs : _trackDuration;
        PlaybackForegroundService.NowPositionMs = PositionMs;
        PlaybackForegroundService.NowIsPlaying = playing;

        var svc = PlaybackForegroundService.Instance;
        if (svc != null) { svc.Refresh(); return; }
        try
        {
            var ctx = Android.App.Application.Context;
            ctx.StartForegroundService(new Intent(ctx, typeof(PlaybackForegroundService)));
        }
        catch { }
    }
}
