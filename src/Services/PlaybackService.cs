using System.Collections.ObjectModel;
using System.Windows.Input;
using SunsetMP.Models;

namespace SunsetMP.Services;

public enum RepeatMode { Off, All, One }

public class PlaybackService : BaseViewModel
{
    const int HistoryLimit = 200;

    // Material-иконки (viewBox 24x24)
    const string IconPlay = "M8 5v14l11-7z";
    const string IconPause = "M6 19h4V5H6v14zm8-14v14h4V5h-4z";
    const string IconRepeat = "M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4z";
    const string IconRepeatOne = "M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4zm-4-2V9h-1l-2 1v1h1.5v4H13z";

    readonly IAudioEngine _engine;
    readonly IDispatcherTimer _timer;
    readonly Random _rng = new();
    readonly HashSet<Track> _played = new();   // для режима «случайно»
    readonly List<Track> _history = new();     // для кнопки «назад»
    int _errorStreak;
    bool _loaded;                              // трек загружен в движок

    public ObservableCollection<Track> Playlist { get; } = new();

    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ToggleShuffleCommand { get; }
    public ICommand CycleRepeatCommand { get; }

    public PlaybackService(IAudioEngine engine)
    {
        _engine = engine;
        _engine.Completed += () => Next(auto: true);
        _engine.Error += OnEngineError;
        _engine.Prepared += () => { _errorStreak = 0; var d = _engine.DurationMs; if (d > 0) Duration = d; };
        _engine.FocusLost += () => { if (IsPlaying) Pause(); };

        // Внешние команды: гарнитура, шторка уведомлений, экран блокировки
        _engine.RemotePlay += () => { if (!IsPlaying) PlayPause(); };
        _engine.RemotePause += () => { if (IsPlaying) Pause(); };
        _engine.RemoteNext += () => Next(auto: false);
        _engine.RemotePrevious += () => Previous();
        _engine.RemoteSeek += ms => SeekTo(ms);

        var dispatcher = Dispatcher.GetForCurrentThread() ?? Application.Current!.Dispatcher;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Playlist.CollectionChanged += (_, _) =>
        {
            if (IsFiltering) RebuildFiltered();
            OnPropertyChanged(nameof(PlaylistTitle));
            OnPropertyChanged(nameof(EmptyText));
        };

        _shuffle = Preferences.Get("shuffle", false);
        _repeat = (RepeatMode)Preferences.Get("repeat", 0);

        PlayPauseCommand = new Command(PlayPause);
        NextCommand = new Command(() => Next(auto: false));
        PreviousCommand = new Command(() => Previous());
        ToggleShuffleCommand = new Command(() => Shuffle = !Shuffle);
        CycleRepeatCommand = new Command(() => Repeat = (RepeatMode)(((int)Repeat + 1) % 3));
    }

    // ---------- Состояние ----------
    Track? _current;
    public Track? Current
    {
        get => _current;
        private set
        {
            if (_current == value) return;
            if (_current != null) _current.IsCurrent = false;
            _current = value;
            if (_current != null) _current.IsCurrent = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NowTitle));
            OnPropertyChanged(nameof(NowArtist));
        }
    }

    public string NowTitle => Current?.Title ?? "Ничего не играет";
    public string NowArtist => Current?.Artist ?? "";
    public string PlaylistTitle => IsFiltering
        ? $"🔎 Найдено: {_filtered.Count} из {Playlist.Count}"
        : $"🎵 Треков в плейлисте: {Playlist.Count}";

    // ---------- Поиск по плейлисту ----------
    string _searchText = "";
    ObservableCollection<Track> _filtered = new();

    public bool IsFiltering => SearchHelper.Terms(_searchText).Length > 0;

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value ?? "")) RebuildFiltered(); }
    }

    /// <summary>Что показывать в списке: весь плейлист или результаты поиска</summary>
    public IEnumerable<Track> DisplayPlaylist => IsFiltering ? _filtered : Playlist;

    public string EmptyText => IsFiltering && Playlist.Count > 0
        ? "Ничего не найдено"
        : "Плейлист пуст. Добавьте треки на вкладках «Локальные» или «Jellyfin».";

    void RebuildFiltered()
    {
        var terms = SearchHelper.Terms(_searchText);
        _filtered = new ObservableCollection<Track>(terms.Length == 0
            ? Enumerable.Empty<Track>()
            : Playlist.Where(t => SearchHelper.Matches(terms, t.Title + " " + t.Artist)));
        OnPropertyChanged(nameof(IsFiltering));
        OnPropertyChanged(nameof(DisplayPlaylist));
        OnPropertyChanged(nameof(PlaylistTitle));
        OnPropertyChanged(nameof(EmptyText));
    }

    bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (!Set(ref _isPlaying, value)) return;
            OnPropertyChanged(nameof(PlayPauseIcon));
        }
    }
    public string PlayPauseIcon => IsPlaying ? IconPause : IconPlay;

    double _position, _duration;
    public double Position
    {
        get => _position;
        set
        {
            if (!Set(ref _position, value)) return;
            OnPropertyChanged(nameof(PositionText));
            OnPropertyChanged(nameof(Progress));
        }
    }
    public double Duration
    {
        get => _duration;
        private set
        {
            if (!Set(ref _duration, value)) return;
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(Progress));
        }
    }
    /// <summary>Доля воспроизведённого, 0..1</summary>
    public double Progress => _duration > 0 ? Math.Clamp(_position / _duration, 0, 1) : 0;
    public string PositionText => Track.FormatTime(_position);
    public string DurationText => Track.FormatTime(_duration);
    public bool IsSeeking { get; set; }

    bool _shuffle;
    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (!Set(ref _shuffle, value)) return;
            Preferences.Set("shuffle", value);
            _played.Clear();
            if (Current != null) _played.Add(Current);
            OnPropertyChanged(nameof(ShuffleOpacity));
            _shuffleNext = null;
            SchedulePreload();
        }
    }
    public double ShuffleOpacity => Shuffle ? 1.0 : 0.4;

    RepeatMode _repeat;
    public RepeatMode Repeat
    {
        get => _repeat;
        set
        {
            if (!Set(ref _repeat, value)) return;
            Preferences.Set("repeat", (int)value);
            OnPropertyChanged(nameof(RepeatIcon));
            OnPropertyChanged(nameof(RepeatOpacity));
            SchedulePreload();
        }
    }
    public string RepeatIcon => Repeat == RepeatMode.One ? IconRepeatOne : IconRepeat;
    public double RepeatOpacity => Repeat == RepeatMode.Off ? 0.4 : 1.0;

    // ---------- Управление плейлистом ----------
    public void Add(IEnumerable<Track> tracks)
    {
        foreach (var t in tracks) Playlist.Add(t.Clone());
        if (Current == null && Playlist.Count > 0)
        {
            Current = Playlist[0];
            Duration = Current.DurationMs;
            Position = 0;
        }
        SchedulePreload();
    }

    public void Replace(IEnumerable<Track> tracks)
    {
        Clear();
        Add(tracks);
        StartPlayback();
    }

    public void Clear()
    {
        Stop();
        Playlist.Clear();
        _played.Clear();
        _history.Clear();
        Current = null;
        Duration = 0;
    }

    public void Remove(Track t)
    {
        var idx = Playlist.IndexOf(t);
        if (idx < 0) return;
        var wasCurrent = t == Current;
        var wasPlaying = IsPlaying;
        Playlist.Remove(t);
        _played.Remove(t);
        _history.RemoveAll(h => h == t);
        if (!wasCurrent) { SchedulePreload(); return; }

        if (Playlist.Count == 0) { Stop(); Current = null; Duration = 0; return; }
        var next = Playlist[Math.Min(idx, Playlist.Count - 1)];
        if (wasPlaying) PlayTrack(next, pushHistory: false);
        else { Stop(); Current = next; Duration = next.DurationMs; }
    }

    // ---------- Воспроизведение ----------
    public void StartPlayback()
    {
        if (Playlist.Count == 0) return;
        PlayTrack(Shuffle ? Playlist[_rng.Next(Playlist.Count)] : Playlist[0]);
    }

    public void PlayTrack(Track t, bool pushHistory = true)
    {
        if (pushHistory && Current != null && Current != t)
        {
            _history.Add(Current);
            if (_history.Count > HistoryLimit) _history.RemoveAt(0);
        }
        Current = t;
        _played.Add(t);
        Position = 0;
        Duration = t.DurationMs;
        _loaded = true;
        IsPlaying = true;
        _engine.Play(t.Uri, t.Title, t.Artist, t.DurationMs);
        SchedulePreload();
    }

    public void PlayPause()
    {
        if (IsPlaying) Pause();
        else if (_loaded) Resume();
        else if (Current != null) PlayTrack(Current, pushHistory: false);
        else StartPlayback();
    }

    public void Pause() { _engine.Pause(); IsPlaying = false; }
    void Resume() { _engine.Resume(); IsPlaying = true; }

    public void Stop()
    {
        _engine.Stop();
        _loaded = false;
        IsPlaying = false;
        Position = 0;
    }

    public void SeekTo(double ms)
    {
        if (!_loaded) return;
        _engine.SeekTo((int)ms);
        Position = ms;
    }

    Track? _shuffleNext;   // заранее выбранный следующий трек в режиме «случайно» (нужен для предзагрузки)

    /// <summary>Какой трек будет следующим. Состояние воспроизведения не меняет. null — плейлист закончился.</summary>
    Track? PeekNext(bool auto)
    {
        if (Playlist.Count == 0) return null;
        if (auto && Repeat == RepeatMode.One && Current != null) return Current;

        if (Shuffle)
        {
            if (_shuffleNext != null && Playlist.Contains(_shuffleNext) && !_played.Contains(_shuffleNext))
                return _shuffleNext;

            var candidates = Playlist.Where(p => !_played.Contains(p)).ToList();
            if (candidates.Count == 0)
            {
                if (auto && Repeat != RepeatMode.All) return null;
                candidates = Playlist.Where(p => p != Current).ToList();   // новый круг
                if (candidates.Count == 0) candidates = Playlist.ToList();
            }
            return _shuffleNext = candidates[_rng.Next(candidates.Count)];
        }

        var idx = Current == null ? -1 : Playlist.IndexOf(Current);
        if (idx + 1 < Playlist.Count) return Playlist[idx + 1];
        return !auto || Repeat == RepeatMode.All ? Playlist[0] : null;
    }

    public void Next(bool auto)
    {
        var next = PeekNext(auto);
        if (next == null)
        {
            if (Playlist.Count > 0) FinishPlaylist();
            return;
        }

        // «случайно»: все треки уже прозвучали — начинаем новый круг
        if (Shuffle && Playlist.All(_played.Contains))
        {
            _played.Clear();
            if (Current != null) _played.Add(Current);
        }
        _shuffleNext = null;
        PlayTrack(next);
    }

    /// <summary>Просит движок заранее загрузить в память трек, который сыграет следующим.</summary>
    void SchedulePreload()
    {
        if (!_loaded) return;
        var next = PeekNext(auto: true);
        _engine.Preload(next != null && next != Current ? next.Uri : null);
    }

    void FinishPlaylist()
    {
        Stop();
        _played.Clear();
        Current = Playlist.Count > 0 ? Playlist[0] : null;
        Duration = Current?.DurationMs ?? 0;
        if (Current != null) _played.Add(Current);
    }

    /// <param name="restartIfPlayed">true — если трек играет больше 3 с, кнопка «назад» начинает его заново</param>
    public void Previous(bool restartIfPlayed = true)
    {
        if (Playlist.Count == 0) return;
        if (restartIfPlayed && Position > 3000) { SeekTo(0); return; }

        if (_history.Count > 0)
        {
            var prev = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            if (Playlist.Contains(prev)) { PlayTrack(prev, pushHistory: false); return; }
        }
        var idx = Current == null ? 0 : Playlist.IndexOf(Current);
        if (idx > 0) PlayTrack(Playlist[idx - 1], pushHistory: false);
        else SeekTo(0);
    }

    void OnEngineError()
    {
        _errorStreak++;
        if (_errorStreak >= Math.Max(Playlist.Count, 1) || _errorStreak > 20)
        {
            _errorStreak = 0;
            Stop();
            return;
        }
        Next(auto: true);
    }

    void Tick()
    {
        if (!IsPlaying || IsSeeking || !_loaded) return;
        Position = _engine.PositionMs;
        var d = _engine.DurationMs;
        if (d > 0 && Math.Abs(d - Duration) > 1) Duration = d;
    }
}
