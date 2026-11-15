using System.Collections.ObjectModel;
using System.Text.Json;
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

    // Сохранение состояния
    const string PrefCurrent = "state_current";     // индекс текущего трека в плейлисте
    const string PrefPosition = "state_position";   // позиция в треке, мс
    const string PrefQueue = "state_queue";         // очередь режима «случайно» (индексы)
    static readonly object FileLock = new();
    static string PlaylistFile => Path.Combine(FileSystem.AppDataDirectory, "playlist.json");

    sealed record SavedTrack(string Title, string Artist, long DurationMs, string? Uri, string? ServerPath);

    readonly IAudioEngine _engine;
    readonly ServerClient _server;
    readonly IDispatcherTimer _timer;
    readonly Random _rng = new();
    readonly List<Track> _history = new();       // для кнопки «назад»
    readonly List<Track> _shuffleQueue = new();  // «случайно»: ещё не игравшие треки в случайном порядке
    int _errorStreak;
    int _tickCount;
    bool _loaded;                                // трек загружен в движок
    bool _restoring;
    bool _playlistDirty;
    long _resumePositionMs;                      // с какого места продолжить восстановленный трек
    CancellationTokenSource? _saveCts;

    public ObservableCollection<Track> Playlist { get; } = new();

    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ToggleShuffleCommand { get; }
    public ICommand CycleRepeatCommand { get; }

    public PlaybackService(IAudioEngine engine, ServerClient server)
    {
        _engine = engine;
        _server = server;
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
            if (_restoring) return;
            _playlistDirty = true;
            SaveSoon();
        };

        _shuffle = Preferences.Get("shuffle", false);
        _repeat = (RepeatMode)Preferences.Get("repeat", 0);

        PlayPauseCommand = new Command(PlayPause);
        NextCommand = new Command(() => Next(auto: false));
        PreviousCommand = new Command(() => Previous());
        ToggleShuffleCommand = new Command(() => Shuffle = !Shuffle);
        CycleRepeatCommand = new Command(() => Repeat = (RepeatMode)(((int)Repeat + 1) % 3));

        RestoreState();
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
        : "Плейлист пуст. Добавьте треки на вкладках «Локальные» или «Сервер».";

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
            RebuildShuffleQueue();      // включили — строим новую случайную очередь, выключили — очищаем
            OnPropertyChanged(nameof(ShuffleOpacity));
            SchedulePreload();
            SaveSoon();
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

            // «повтор всего» включили, когда очередь уже закончилась — начинаем новый круг
            if (Shuffle && Repeat == RepeatMode.All && _shuffleQueue.Count == 0 && Current != null)
                RebuildShuffleQueue();
            SchedulePreload();
        }
    }
    public string RepeatIcon => Repeat == RepeatMode.One ? IconRepeatOne : IconRepeat;
    public double RepeatOpacity => Repeat == RepeatMode.Off ? 0.4 : 1.0;

    // ---------- Случайный порядок без повторов ----------
    // В очереди лежат ещё не игравшие треки в случайном порядке. Как только трек начинает играть,
    // он исключается из очереди, поэтому до конца круга не повторится. Когда очередь опустела:
    // «повтор всего» — строится новая очередь, иначе воспроизведение заканчивается.

    /// <summary>Строит новую перемешанную очередь из всех треков, кроме текущего.</summary>
    void RebuildShuffleQueue()
    {
        _shuffleQueue.Clear();
        if (!Shuffle) return;

        _shuffleQueue.AddRange(Playlist.Where(t => t != Current));
        for (var i = _shuffleQueue.Count - 1; i > 0; i--)         // перемешивание Фишера — Йетса
        {
            var j = _rng.Next(i + 1);
            (_shuffleQueue[i], _shuffleQueue[j]) = (_shuffleQueue[j], _shuffleQueue[i]);
        }
    }

    // ---------- Управление плейлистом ----------
    public void Add(IEnumerable<Track> tracks)
    {
        var added = new List<Track>();
        foreach (var t in tracks)
        {
            var copy = t.Clone();
            Playlist.Add(copy);
            added.Add(copy);
        }

        // новые треки встают в случайные места ещё не сыгранной очереди
        if (Shuffle)
            foreach (var t in added)
                _shuffleQueue.Insert(_rng.Next(_shuffleQueue.Count + 1), t);

        if (Current == null && Playlist.Count > 0)
        {
            Current = Playlist[0];
            _shuffleQueue.Remove(Current);
            Duration = Current.DurationMs;
            Position = 0;
        }
        SchedulePreload();
        SaveSoon();
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
        _shuffleQueue.Clear();
        _history.Clear();
        Current = null;
        Duration = 0;
        SaveSoon();
    }

    public void Remove(Track t)
    {
        var idx = Playlist.IndexOf(t);
        if (idx < 0) return;
        var wasCurrent = t == Current;
        var wasPlaying = IsPlaying;
        Playlist.Remove(t);
        _shuffleQueue.Remove(t);
        _history.RemoveAll(h => h == t);
        if (!wasCurrent) { SchedulePreload(); SaveSoon(); return; }

        if (Playlist.Count == 0) { Stop(); Current = null; Duration = 0; SaveSoon(); return; }
        var next = Playlist[Math.Min(idx, Playlist.Count - 1)];
        if (wasPlaying) PlayTrack(next, pushHistory: false);
        else { Stop(); Current = next; _shuffleQueue.Remove(next); Duration = next.DurationMs; SaveSoon(); }
    }

    // ---------- Воспроизведение ----------
    public void StartPlayback()
    {
        if (Playlist.Count == 0) return;
        if (Shuffle)
        {
            Current = null;                 // новый круг: в очередь попадают все треки
            RebuildShuffleQueue();
            PlayTrack(_shuffleQueue.Count > 0 ? _shuffleQueue[0] : Playlist[0]);
        }
        else PlayTrack(Playlist[0]);
    }

    public void PlayTrack(Track t, bool pushHistory = true, long startMs = 0)
    {
        if (pushHistory && Current != null && Current != t)
        {
            _history.Add(Current);
            if (_history.Count > HistoryLimit) _history.RemoveAt(0);
        }
        Current = t;
        _resumePositionMs = 0;

        if (Shuffle)
        {
            _shuffleQueue.Remove(t);        // сыгранный трек больше не выпадет в этом круге
            if (_shuffleQueue.Count == 0 && Repeat == RepeatMode.All)
                RebuildShuffleQueue();      // круг закончился — начинаем следующий (без текущего трека)
        }

        Position = startMs;
        Duration = t.DurationMs;
        _loaded = true;
        IsPlaying = true;
        _engine.Play(UriFor(t), t.Title, t.Artist, t.DurationMs, startMs);
        SchedulePreload();
        SaveSoon();
    }

    /// <summary>Адрес для воспроизведения. Для треков сервера собирается заново (адрес/токен могли измениться).</summary>
    string UriFor(Track t)
        => !string.IsNullOrEmpty(t.ServerPath) && _server.HasCredentials ? _server.StreamUrl(t.ServerPath) : t.Uri;

    public void PlayPause()
    {
        if (IsPlaying) Pause();
        else if (_loaded) Resume();
        else if (Current != null)
        {
            var start = _resumePositionMs;      // после перезапуска приложения продолжаем с сохранённого места
            PlayTrack(Current, pushHistory: false, startMs: start);
        }
        else StartPlayback();
    }

    public void Pause() { _engine.Pause(); IsPlaying = false; SavePosition(); }
    void Resume() { _engine.Resume(); IsPlaying = true; }

    public void Stop()
    {
        _engine.Stop();
        _loaded = false;
        IsPlaying = false;
        Position = 0;
        SavePosition();
    }

    public void SeekTo(double ms)
    {
        if (!_loaded)
        {
            // трек ещё не запущен (восстановлен после перезапуска) — запоминаем, откуда начать
            if (Current != null) { _resumePositionMs = (long)ms; Position = ms; SavePosition(); }
            return;
        }
        _engine.SeekTo((int)ms);
        Position = ms;
        SavePosition();
    }

    /// <summary>Какой трек будет следующим (состояние не меняет). null — неизвестно или плейлист закончился.</summary>
    Track? PeekNext(bool auto)
    {
        if (Playlist.Count == 0) return null;
        if (auto && Repeat == RepeatMode.One && Current != null) return Current;

        if (Shuffle) return _shuffleQueue.Count > 0 ? _shuffleQueue[0] : null;

        var idx = Current == null ? -1 : Playlist.IndexOf(Current);
        if (idx + 1 < Playlist.Count) return Playlist[idx + 1];
        return !auto || Repeat == RepeatMode.All ? Playlist[0] : null;
    }

    public void Next(bool auto)
    {
        if (Playlist.Count == 0) return;

        if (auto && Repeat == RepeatMode.One && Current != null)
        {
            PlayTrack(Current, pushHistory: false);
            return;
        }

        Track next;
        if (Shuffle)
        {
            if (_shuffleQueue.Count == 0)
            {
                // все треки круга сыграны
                if (auto && Repeat != RepeatMode.All) { FinishPlaylist(); return; }
                RebuildShuffleQueue();      // кнопка «вперёд» или «повтор всего» — новый круг
            }
            next = _shuffleQueue.Count > 0 ? _shuffleQueue[0] : (Current ?? Playlist[0]);   // в плейлисте один трек
        }
        else
        {
            var idx = Current == null ? -1 : Playlist.IndexOf(Current);
            if (idx + 1 < Playlist.Count) next = Playlist[idx + 1];
            else if (!auto || Repeat == RepeatMode.All) next = Playlist[0];
            else { FinishPlaylist(); return; }
        }
        PlayTrack(next);
    }

    /// <summary>Просит движок заранее загрузить в память трек, который сыграет следующим.</summary>
    void SchedulePreload()
    {
        if (!_loaded) return;
        var next = PeekNext(auto: true);
        _engine.Preload(next != null && next != Current ? UriFor(next) : null);
    }

    void FinishPlaylist()
    {
        Stop();
        if (Playlist.Count == 0) { Current = null; Duration = 0; return; }

        // плейлист доиграл — готовим новый круг: следующий запуск начнётся с «первого» трека
        Current = Shuffle ? Playlist[_rng.Next(Playlist.Count)] : Playlist[0];
        Duration = Current.DurationMs;
        RebuildShuffleQueue();
        SaveSoon();
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

        if (++_tickCount % 20 == 0) SavePosition();     // раз в ~5 секунд
    }

    // ---------- Сохранение и восстановление ----------
    // Плейлист — в файле playlist.json (пишется только при изменении), остальное — в Preferences:
    // индекс текущего трека, позиция и очередь «случайно». Для треков сервера адрес не хранится:
    // он собирается заново из пути на сервере, поэтому смена токена или адреса сервера не ломает плейлист.

    void SavePosition() => Preferences.Set(PrefPosition, (long)_position);

    /// <summary>Сохранить с небольшой задержкой (изменения часто идут пачкой)</summary>
    async void SaveSoon()
    {
        if (_restoring) return;
        _saveCts?.Cancel();
        var cts = _saveCts = new CancellationTokenSource();
        try { await Task.Delay(1000, cts.Token); }
        catch (OperationCanceledException) { return; }
        WriteState();
    }

    /// <summary>Сохранить немедленно (приложение уходит в фон)</summary>
    public void SaveNow()
    {
        _saveCts?.Cancel();
        WriteState();
    }

    void WriteState()
    {
        if (_restoring) return;

        Preferences.Set(PrefCurrent, Current == null ? -1 : Playlist.IndexOf(Current));
        Preferences.Set(PrefPosition, (long)_position);
        Preferences.Set(PrefQueue, QueueToString());

        if (!_playlistDirty) return;
        _playlistDirty = false;

        var snapshot = Playlist
            .Select(t => new SavedTrack(t.Title, t.Artist, t.DurationMs,
                string.IsNullOrEmpty(t.ServerPath) ? t.Uri : null, t.ServerPath))
            .ToList();

        _ = Task.Run(() =>
        {
            try
            {
                lock (FileLock)
                {
                    var temp = PlaylistFile + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
                    File.Move(temp, PlaylistFile, true);
                }
            }
            catch { /* не критично: сохраним при следующем изменении */ }
        });
    }

    string QueueToString()
    {
        if (!Shuffle) return "";
        var index = new Dictionary<Track, int>(Playlist.Count);
        for (var i = 0; i < Playlist.Count; i++) index[Playlist[i]] = i;
        return "q:" + string.Join(",", _shuffleQueue.Select(t => index.TryGetValue(t, out var i) ? i : -1).Where(i => i >= 0));
    }

    void RestoreState()
    {
        _restoring = true;
        try
        {
            if (!File.Exists(PlaylistFile)) return;
            var saved = JsonSerializer.Deserialize<List<SavedTrack>>(File.ReadAllText(PlaylistFile));
            if (saved == null || saved.Count == 0) return;

            _server.TryRestore();       // адрес и токен сервера нужны, чтобы собрать ссылки на треки
            foreach (var s in saved)
            {
                Playlist.Add(new Track
                {
                    Title = s.Title,
                    Artist = s.Artist,
                    DurationMs = s.DurationMs,
                    ServerPath = s.ServerPath,
                    Uri = s.Uri ?? ""
                });
            }

            var index = Preferences.Get(PrefCurrent, -1);
            if (index < 0 || index >= Playlist.Count) index = 0;
            Current = Playlist[index];
            Duration = Current.DurationMs;

            // продолжим с того же места (если трек не был почти доигран)
            var position = Preferences.Get(PrefPosition, 0L);
            if (position > 0 && (Current.DurationMs <= 0 || position < Current.DurationMs - 2000))
            {
                _resumePositionMs = position;
                Position = position;
            }

            if (Shuffle) RestoreShuffleQueue(Preferences.Get(PrefQueue, ""));
        }
        catch
        {
            // повреждённое сохранение — начинаем с пустого плейлиста
            Playlist.Clear();
            Current = null;
        }
        finally { _restoring = false; }
    }

    void RestoreShuffleQueue(string saved)
    {
        if (!saved.StartsWith("q:"))
        {
            RebuildShuffleQueue();      // сохранённой очереди нет — строим новую
            return;
        }

        _shuffleQueue.Clear();
        var seen = new HashSet<int>();
        foreach (var part in saved[2..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out var i) && i >= 0 && i < Playlist.Count &&
                Playlist[i] != Current && seen.Add(i))
                _shuffleQueue.Add(Playlist[i]);
        }
    }
}
