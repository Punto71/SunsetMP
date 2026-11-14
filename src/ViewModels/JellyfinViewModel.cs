using System.Windows.Input;
using SunsetMP.Models;
using SunsetMP.Services;

namespace SunsetMP.ViewModels;

public class JellyfinViewModel : TreeViewModelBase
{
    readonly JellyfinClient _client;
    bool _restoreTried;

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    string _serverUrl = Preferences.Get("jf_server", "http://");
    public string ServerUrl { get => _serverUrl; set => Set(ref _serverUrl, value); }

    string _userName = Preferences.Get("jf_user", "");
    public string UserName { get => _userName; set => Set(ref _userName, value); }

    string _password = "";
    public string Password { get => _password; set => Set(ref _password, value); }

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

    public string ConnectedTo => $"{UserName} @ {ServerUrl}";

    public JellyfinViewModel(PlaybackService player, JellyfinClient client) : base(player)
    {
        _client = client;
        ConnectCommand = new Command(async () => await ConnectAsync());
        DisconnectCommand = new Command(Disconnect);
    }

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
            await _client.LoginAsync(ServerUrl, UserName, Password);
            Password = "";
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
            var items = await _client.GetChildrenAsync(null);
            var roots = FolderMode
                ? items.Where(i => i.CollectionType != "playlists").Select(CreateLibraryNode).ToList()
                : items.Select(i => CreateNode(i, 0, false)).ToList();
            SetRoots(roots);
            ServerUrl = _client.ServerUrl;
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

    // ---------- Режим «по папкам на диске» ----------
    bool _folderMode = Preferences.Get("jf_folder_mode", false);

    /// <summary>true — дерево строится по реальным путям файлов на сервере, а не по структуре Jellyfin</summary>
    public bool FolderMode
    {
        get => _folderMode;
        set
        {
            if (!Set(ref _folderMode, value)) return;
            Preferences.Set("jf_folder_mode", value);
            if (IsConnected) _ = LoadRootsAsync();     // перестроить корни в новом режиме
        }
    }

    TreeNode CreateLibraryNode(JfItem library)
    {
        TreeNode? node = null;
        node = new TreeNode
        {
            Name = library.Name,
            Depth = 0,
            IsFolder = true,
            ChildLoader = _ => BuildLibraryChildrenAsync(library, 1),
            TracksProvider = async () =>
            {
                if (!node!.ChildrenLoaded)
                {
                    node.Children.AddRange(await BuildLibraryChildrenAsync(library, 1));
                    node.ChildrenLoaded = true;
                }
                return node.CollectLoadedTracks();
            }
        };
        return node;
    }

    /// <summary>Одним проходом скачивает список треков библиотеки с путями и строит дерево папок.</summary>
    async Task<List<TreeNode>> BuildLibraryChildrenAsync(JfItem library, int depth)
    {
        var items = await _client.GetAudioWithPathAsync(library.Id);
        return await Task.Run(() =>
            FolderTreeBuilder.Build(items.Select(i => (i.Path, _client.ToTrack(i))), depth));
    }

    void Disconnect()
    {
        _client.Disconnect();
        ResetSearch();
        SetRoots(Array.Empty<TreeNode>());
        IsConnected = false;
    }

    // ---------- Поиск по серверу ----------
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
            var items = await _client.SearchAsync(query, ct);
            ct.ThrowIfCancellationRequested();
            ShowFlat(items.Select(i => CreateNode(i, 0, true)).ToList());
            NoResults = items.Count == 0;
            Error = "";
        }
        catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (Exception ex) { Error = ex.Message; }
        finally { if (!ct.IsCancellationRequested) IsBusy = false; }
    }

    static string TypeLabel(JfItem item) => item.Type switch
    {
        "MusicArtist" => "Исполнитель",
        "MusicAlbum" => string.IsNullOrEmpty(item.Artists) ? "Альбом" : $"Альбом · {item.Artists}",
        "Playlist" => "Плейлист",
        _ => item.Artists
    };

    TreeNode CreateNode(JfItem item, int depth, bool searchResult)
    {
        if (item.IsFolder)
        {
            return new TreeNode
            {
                Name = item.Name,
                Subtitle = searchResult ? TypeLabel(item) : (item.Type == "MusicAlbum" ? item.Artists : ""),
                Depth = depth,
                IsFolder = true,
                ChildLoader = async parent =>
                    (await _client.GetChildrenAsync(item)).Select(c => CreateNode(c, parent.Depth + 1, false)).ToList(),
                TracksProvider = () => _client.GetTracksAsync(item)
            };
        }

        var track = _client.ToTrack(item);
        return new TreeNode
        {
            Name = item.Name,
            Subtitle = item.Artists,
            Depth = depth,
            IsFolder = false,
            Track = track,
            TracksProvider = () => Task.FromResult(new List<Track> { track })
        };
    }
}
