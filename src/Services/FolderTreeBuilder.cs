using SunsetMP.Models;

namespace SunsetMP.Services;

/// <summary>Строит дерево папок по путям файлов (режим Jellyfin «по папкам на диске»).</summary>
public static class FolderTreeBuilder
{
    sealed class Dir
    {
        public string Name = "";
        public Dictionary<string, Dir> Dirs = new();
        public List<(string File, Track Track)> Files = new();
    }

    public static List<TreeNode> Build(IEnumerable<(string? Path, Track Track)> items, int depth)
    {
        var root = new Dir();
        foreach (var (path, track) in items)
        {
            // сервер может быть на Windows (\) или Linux (/)
            var segments = (path ?? "").Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var cur = root;
            var file = track.Title;
            if (segments.Length > 0)
            {
                file = segments[^1];
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    if (!cur.Dirs.TryGetValue(segments[i], out var next))
                        cur.Dirs[segments[i]] = next = new Dir { Name = segments[i] };
                    cur = next;
                }
            }
            cur.Files.Add((file, track));
        }

        // общий путь до корня библиотеки (/mnt/media/music/...) не показываем
        var top = root;
        while (top.Files.Count == 0 && top.Dirs.Count == 1) top = top.Dirs.Values.First();

        var result = new List<TreeNode>();
        foreach (var d in top.Dirs.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            result.Add(ToNode(d, depth));
        foreach (var f in top.Files.OrderBy(x => x.File, StringComparer.OrdinalIgnoreCase))
            result.Add(TrackNode(f.Track, depth));
        return result;
    }

    static TreeNode ToNode(Dir d, int depth)
    {
        var name = d.Name;
        // схлопываем цепочки одиночных папок: Rock/Queen → «Rock/Queen»
        while (d.Files.Count == 0 && d.Dirs.Count == 1)
        {
            d = d.Dirs.Values.First();
            name += "/" + d.Name;
        }

        var node = new TreeNode { Name = name, Depth = depth, IsFolder = true, ChildrenLoaded = true };
        foreach (var sub in d.Dirs.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            node.Children.Add(ToNode(sub, depth + 1));
        foreach (var f in d.Files.OrderBy(x => x.File, StringComparer.OrdinalIgnoreCase))
            node.Children.Add(TrackNode(f.Track, depth + 1));

        node.Subtitle = $"треков: {node.CollectLoadedTracks().Count}";
        node.TracksProvider = () => Task.FromResult(node.CollectLoadedTracks());
        return node;
    }

    static TreeNode TrackNode(Track t, int depth) => new()
    {
        Name = t.Title,
        Subtitle = t.Artist,
        Depth = depth,
        IsFolder = false,
        Track = t,
        TracksProvider = () => Task.FromResult(new List<Track> { t })
    };
}
