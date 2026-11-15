using System.Windows.Input;
using SunsetMP.Models;
using SunsetMP.Services;

namespace SunsetMP.ViewModels;

public class ServerViewModel : TreeViewModelBase
{
    readonly ServerClient _client;
    readonly DownloadService _downloads;
    bool _restoreTried;

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshCommand { get; }

    string _serverUrl = Preferences.Get("srv_url", "http://");
    public string ServerUrl { get => _serverUrl; set => Set(ref _serverUrl, value); }

    string _token = "";
    public string Token { get => _token; set => Set(ref _token, value); }

    bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set { if (Set(ref _isConnected, value)) OnPropertyChanged(nameof(IsDisconnected)); }
    }
    public bool IsDisconnected => !_isConnected;

    string _error = "";
    public string Error
    {
        get => _error;
        private set { if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_error);

    public string ConnectedTo => ServerUrl;

    /// <summary>Состояние скачивания (панель прогресса на странице)</summary>
    public DownloadService Downloads => _downloads;

    public ServerViewModel(PlaybackService player, ServerClient client, DownloadService downloads) : base(player)
    {
        _client = client;
        _downloads = downloads;
        _downloads.Finished += message => ShowStatus(message);
        ConnectCommand = new Command(async () => await ConnectAsync());
        DisconnectCommand = new Command(Disconnect);
        RefreshCommand = new Command(async () => { if (IsConnected) await LoadRootsAsync(); });
    }

    // ---------- Подключение ----------
    public async Task EnsureRestoredAsync()
    {
        if (_restoreTried) return;
        _restoreTried = true;
        if (_client.TryRestore()) await LoadRootsAsync();
    }

    async Task ConnectAsync()
    {
        if (IsBusy) return;
        Error = "";
        IsBusy = true;
        try
        {
            await _client.ConnectAsync(ServerUrl, Token);
            Token = "";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            IsBusy = false;
            return;
        }
        IsBusy = false;
        await LoadRootsAsync();
    }

    async Task LoadRootsAsync()
    {
        IsBusy = true;
        try
        {
            var root = await _client.BrowseAsync("");
            SetRoots(BuildChildren(root, 0));
            ServerUrl = _client.BaseUrl;
            OnPropertyChanged(nameof(ConnectedTo));
            IsConnected = true;
            Error = "";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            IsConnected = false;
        }
        finally { IsBusy = false; }
    }

    void Disconnect()
    {
        _client.Disconnect();
        ResetSearch();
        SetRoots(Array.Empty<TreeNode>());
        IsConnected = false;
    }

    // ---------- Дерево: на каждую папку — отдельный запрос ----------
    List<TreeNode> BuildChildren(ServerBrowse browse, int depth)
    {
        var nodes = new List<TreeNode>(browse.Folders.Count + browse.Tracks.Count);
        foreach (var f in browse.Folders) nodes.Add(CreateFolderNode(f, depth, null));
        foreach (var t in browse.Tracks)
        {
            var track = _client.ToTrack(t);
            nodes.Add(CreateTrackNode(track, depth, track.Artist));
        }
        return nodes;
    }

    TreeNode CreateFolderNode(ServerFolder folder, int depth, string? subtitle) => new()
    {
        Name = folder.Name,
        Subtitle = subtitle ?? $"треков: {folder.TrackCount}",
        Depth = depth,
        IsFolder = true,
        PathSegments = folder.Path.Split('/', StringSplitOptions.RemoveEmptyEntries),
        // раскрыли папку — запросили у сервера только её содержимое
        ChildLoader = async parent => BuildChildren(await _client.BrowseAsync(folder.Path), parent.Depth + 1),
        TracksProvider = () => _client.TracksAsync(folder.Path)
    };

    static TreeNode CreateTrackNode(Track track, int depth, string subtitle) => new()
    {
        Name = track.Title,
        Subtitle = subtitle,
        Depth = depth,
        IsFolder = false,
        Track = track,
        TracksProvider = () => Task.FromResult(new List<Track> { track })
    };

    // ---------- Поиск (на сервере) ----------
    protected override async Task OnSearchAsync(string query, CancellationToken ct)
    {
        if (!IsConnected) return;

        if (query.Length == 0)
        {
            IsBusy = false;          // отменённый поиск сам индикатор не гасит
            ShowRoots();
            NoResults = false;
            Error = "";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _client.SearchAsync(query, ct);
            ct.ThrowIfCancellationRequested();

            var nodes = new List<TreeNode>();
            foreach (var f in result.Folders)
            {
                var parent = ParentOf(f.Path);
                nodes.Add(CreateFolderNode(f, 0,
                    parent.Length == 0 ? $"треков: {f.TrackCount}" : $"{parent} · треков: {f.TrackCount}"));
            }
            foreach (var t in result.Tracks)
            {
                var track = _client.ToTrack(t);
                var subtitle = string.Join(" · ", new[] { track.Artist, ParentOf(t.Path) }.Where(s => !string.IsNullOrEmpty(s)));
                nodes.Add(CreateTrackNode(track, 0, subtitle));
            }

            ShowFlat(nodes);
            NoResults = nodes.Count == 0;
            Error = "";
            if (result.Truncated) ShowStatus("Показаны первые результаты — уточните запрос");
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (Exception ex) { Error = ex.Message; }
        finally { if (!ct.IsCancellationRequested) IsBusy = false; }
    }

    static string ParentOf(string path)
    {
        var i = path.LastIndexOf('/');
        return i < 0 ? "" : path[..i];
    }

    // ---------- Скачивание в Music/SunsetMP ----------
    protected override string? ExtraActionLabel(TreeNode n) => "Скачать в Music/SunsetMP";

    protected override async Task OnExtraActionAsync(TreeNode n)
    {
        if (n.TracksProvider == null) return;

        List<Track> tracks;
        n.IsBusy = true;
        try { tracks = await n.TracksProvider(); }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Ошибка", ex.Message, "OK");
            return;
        }
        finally { n.IsBusy = false; }

        if (tracks.Count == 0) { ShowStatus("Треков не найдено"); return; }

        // Отдельный трек — в корень Music/SunsetMP. Папка — Music/SunsetMP/<папка>/..., вложенные папки
        // повторяются так, как они лежат на сервере (отсчитываем от выбранной папки).
        var segments = n.PathSegments;
        var root = segments is { Length: > 0 } ? new[] { segments[^1] } : new[] { n.Name };
        var skip = segments?.Length ?? -1;

        var items = DownloadPlanner.Plan(root, singleTrack: !n.IsFolder, tracks, skip);

        if (items.Count > 200)
        {
            var ok = await Shell.Current.DisplayAlert("Скачать", $"Будет скачано файлов: {items.Count}. Продолжить?", "Да", "Нет");
            if (!ok) return;
        }

        if (await _downloads.EnqueueAsync(n.Name, items))
            ShowStatus($"Добавлено в очередь скачивания: {items.Count}");
    }
}
