using System.Text.RegularExpressions;
using System.Windows.Input;
using SunsetMP.Models;
using SunsetMP.Services;

namespace SunsetMP.ViewModels;

public class LocalLibraryViewModel : TreeViewModelBase
{
    readonly ILocalMusicScanner _scanner;
    bool _loaded;
    Dictionary<TreeNode, bool>? _snapshot;   // состояние раскрытия папок до начала поиска

    public ICommand RefreshCommand { get; }

    string _message = "";
    public string Message { get => _message; private set => Set(ref _message, value); }

    public LocalLibraryViewModel(PlaybackService player, ILocalMusicScanner scanner) : base(player)
    {
        _scanner = scanner;
        RefreshCommand = new Command(async () => await RefreshAsync());
    }

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : RefreshAsync();

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!await _scanner.RequestPermissionAsync())
            {
                Message = "Нет доступа к музыке. Разрешите доступ в настройках приложения и нажмите «Обновить».";
                return;
            }
            var files = await _scanner.ScanAsync();
            var roots = await Task.Run(() => BuildTree(files));
            _snapshot = null;
            SetRoots(roots);
            _loaded = true;
            Message = files.Count == 0 ? "Музыка на устройстве не найдена" : $"Найдено треков: {files.Count}";

            if (IsSearching) await OnSearchAsync(SearchText.Trim(), CancellationToken.None);
        }
        catch (Exception ex) { Message = "Ошибка: " + ex.Message; }
        finally { IsBusy = false; }
    }

    // ---------- Поиск по локальному дереву ----------
    protected override Task OnSearchAsync(string query, CancellationToken ct)
    {
        var terms = SearchHelper.Terms(query);
        if (terms.Length == 0)
        {
            if (_snapshot != null)
            {
                foreach (var kv in _snapshot) kv.Key.IsExpanded = kv.Value;
                _snapshot = null;
            }
            ShowRoots();
            NoResults = false;
            return Task.CompletedTask;
        }

        _snapshot ??= Snapshot(Roots);
        var result = new List<TreeNode>();
        Filter(Roots, terms, "", result);
        ShowFlat(result);
        NoResults = result.Count == 0;
        return Task.CompletedTask;
    }

    static Dictionary<TreeNode, bool> Snapshot(IEnumerable<TreeNode> nodes)
    {
        var map = new Dictionary<TreeNode, bool>();
        void Walk(IEnumerable<TreeNode> list)
        {
            foreach (var n in list)
            {
                if (!n.IsFolder) continue;
                map[n] = n.IsExpanded;
                Walk(n.Children);
            }
        }
        Walk(nodes);
        return map;
    }

    /// <summary>
    /// Оставляет совпавшие треки вместе с цепочкой папок (раскрытых). Имена папок тоже участвуют
    /// в поиске, поэтому по названию папки/альбома находятся все её треки.
    /// </summary>
    static bool Filter(IEnumerable<TreeNode> nodes, string[] terms, string ancestors, List<TreeNode> output)
    {
        var any = false;
        foreach (var n in nodes)
        {
            if (n.IsFolder)
            {
                var sub = new List<TreeNode>();
                if (Filter(n.Children, terms, ancestors + " " + n.SearchText, sub))
                {
                    n.IsExpanded = true;
                    output.Add(n);
                    output.AddRange(sub);
                    any = true;
                }
            }
            else if (SearchHelper.Matches(terms, ancestors + " " + n.SearchText))
            {
                output.Add(n);
                any = true;
            }
        }
        return any;
    }

    // ---------- Построение дерева ----------
    class DirNode
    {
        public string Name = "";
        public Dictionary<string, DirNode> Dirs = new();
        public List<LocalFile> Files = new();
    }

    static List<TreeNode> BuildTree(List<LocalFile> files)
    {
        var root = new DirNode();
        foreach (var f in files)
        {
            var dir = Path.GetDirectoryName(f.Path) ?? "";
            var cur = root;
            foreach (var seg in dir.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!cur.Dirs.TryGetValue(seg, out var next))
                    cur.Dirs[seg] = next = new DirNode { Name = seg };
                cur = next;
            }
            cur.Files.Add(f);
        }

        // /storage/... → показываем сразу тома: emulated/0, XXXX-XXXX
        var start = root;
        if (start.Files.Count == 0 && start.Dirs.Count == 1 && start.Dirs.TryGetValue("storage", out var st))
            start = st;

        var result = new List<TreeNode>();
        foreach (var d in start.Dirs.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            result.Add(ToNode(d, 0));
        foreach (var f in start.Files.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            result.Add(TrackNode(f, 0));
        return result;
    }

    static TreeNode ToNode(DirNode d, int depth)
    {
        var name = d.Name;
        // схлопываем цепочки пустых папок: emulated/0/Music
        while (d.Files.Count == 0 && d.Dirs.Count == 1)
        {
            d = d.Dirs.Values.First();
            name += "/" + d.Name;
        }
        if (depth == 0)
        {
            name = Regex.Replace(name, @"^emulated/0", "Внутренняя память");
            name = Regex.Replace(name, @"^([0-9A-Fa-f]{4}-[0-9A-Fa-f]{4})", "SD-карта ($1)");
        }

        var node = new TreeNode
        {
            Name = name,
            SearchText = name,
            Depth = depth,
            IsFolder = true,
            ChildrenLoaded = true
        };
        foreach (var sub in d.Dirs.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            node.Children.Add(ToNode(sub, depth + 1));
        foreach (var f in d.Files.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            node.Children.Add(TrackNode(f, depth + 1));

        node.Subtitle = $"треков: {node.CollectLoadedTracks().Count}";
        node.TracksProvider = () => Task.FromResult(node.CollectLoadedTracks());
        return node;
    }

    static TreeNode TrackNode(LocalFile f, int depth)
    {
        var t = f.Track;
        return new TreeNode
        {
            Name = t.Title,
            Subtitle = t.Artist,
            SearchText = $"{t.Title} {t.Artist} {Path.GetFileName(f.Path)}",
            Depth = depth,
            IsFolder = false,
            Track = t,
            TracksProvider = () => Task.FromResult(new List<Track> { t })
        };
    }
}
