using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace SunsetMP.Server.Library;

public sealed class FolderNode
{
    public FolderNode(string name, string path)
    {
        Name = name;
        Path = path;
        SearchName = TextSearch.Normalize(name);
        SearchPath = TextSearch.Normalize(path);
    }

    public string Name { get; }
    /// <summary>Относительный путь от корня музыкальной папки ('' — сам корень), разделитель '/'</summary>
    public string Path { get; }
    public string SearchName { get; }
    public string SearchPath { get; }

    public List<FolderNode> Folders { get; } = new();
    public List<TrackEntry> Tracks { get; } = new();

    /// <summary>Сколько треков во всём поддереве (папки без музыки в индекс не попадают)</summary>
    public int TrackCount { get; set; }
}

public sealed class TrackEntry
{
    public TrackEntry(string path, string fileName, string fullPath, long size, long modifiedTicks)
    {
        Path = path;
        FileName = fileName;
        FullPath = fullPath;
        Size = size;
        ModifiedTicks = modifiedTicks;
        SearchKey = TextSearch.Normalize(path);
    }

    /// <summary>Относительный путь файла, разделитель '/'. Он же идентификатор трека в API.</summary>
    public string Path { get; }
    public string FileName { get; }
    public string FullPath { get; }
    public long Size { get; }
    public long ModifiedTicks { get; }
    public string SearchKey { get; }
}

/// <summary>Снимок библиотеки в памяти. После создания не меняется — при пересканировании строится новый.</summary>
public sealed class LibraryIndex
{
    public LibraryIndex(FolderNode root, Dictionary<string, FolderNode> folders,
        Dictionary<string, TrackEntry> tracks, List<TrackEntry> allTracks, DateTime? scannedUtc)
    {
        Root = root;
        Folders = folders;
        Tracks = tracks;
        AllTracks = allTracks;
        ScannedUtc = scannedUtc;
    }

    public static LibraryIndex Empty { get; } =
        new(new FolderNode("", ""), new(), new(), new(), null);

    public FolderNode Root { get; }
    public Dictionary<string, FolderNode> Folders { get; }
    public Dictionary<string, TrackEntry> Tracks { get; }
    /// <summary>Все треки в порядке обхода дерева (подпапки, затем файлы папки)</summary>
    public List<TrackEntry> AllTracks { get; }
    public DateTime? ScannedUtc { get; }
}

public sealed class MusicLibrary
{
    static readonly string[] DefaultExtensions =
        { ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".wav", ".wma", ".ape", ".aiff", ".alac" };

    readonly LibraryOptions _options;
    readonly ILogger<MusicLibrary> _log;
    readonly HashSet<string> _extensions;
    readonly object _scanLock = new();

    volatile LibraryIndex _index = LibraryIndex.Empty;
    volatile bool _ready;

    public MusicLibrary(IOptions<LibraryOptions> options, ILogger<MusicLibrary> log)
    {
        _options = options.Value;
        _log = log;

        var extensions = _options.Extensions is { Length: > 0 } custom ? custom : DefaultExtensions;
        _extensions = new HashSet<string>(
            extensions.Select(e => e.StartsWith('.') ? e : "." + e), StringComparer.OrdinalIgnoreCase);
    }

    public string RootPath => _options.MusicPath;
    public bool IsReady => _ready;
    public LibraryIndex Current => _index;

    /// <summary>Сканирует папку с музыкой и атомарно подменяет индекс. Содержимое файлов не читается — только имена.</summary>
    public void Rescan()
    {
        lock (_scanLock)
        {
            var sw = Stopwatch.StartNew();
            var index = Build();
            _index = index;
            _ready = true;
            _log.LogInformation("Библиотека: {Tracks} треков в {Folders} папках, сканирование {Ms} мс",
                index.AllTracks.Count, index.Folders.Count - 1, sw.ElapsedMilliseconds);
        }
    }

    LibraryIndex Build()
    {
        var folders = new Dictionary<string, FolderNode>(StringComparer.Ordinal);
        var tracks = new Dictionary<string, TrackEntry>(StringComparer.Ordinal);
        var root = new FolderNode("", "");
        folders[""] = root;

        if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
        {
            _log.LogError("Папка с музыкой не найдена: '{Path}'. Укажите Library:MusicPath в appsettings.json", RootPath);
            return new LibraryIndex(root, folders, tracks, new List<TrackEntry>(), DateTime.UtcNow);
        }

        Scan(new DirectoryInfo(RootPath), root, folders, tracks, 0);

        var all = Collect(root);
        return new LibraryIndex(root, folders, tracks, all, DateTime.UtcNow);
    }

    void Scan(DirectoryInfo dir, FolderNode node, Dictionary<string, FolderNode> folders,
        Dictionary<string, TrackEntry> tracks, int depth)
    {
        if (depth > 32) return;   // защита от циклических ссылок

        FileSystemInfo[] entries;
        try { entries = dir.GetFileSystemInfos(); }
        catch (Exception ex)
        {
            _log.LogWarning("Не удалось прочитать папку {Dir}: {Message}", dir.FullName, ex.Message);
            return;
        }

        foreach (var entry in entries)
        {
            if (entry.Name.StartsWith('.')) continue;   // скрытые

            if (entry is DirectoryInfo sub)
            {
                var childPath = node.Path.Length == 0 ? sub.Name : node.Path + "/" + sub.Name;
                var child = new FolderNode(sub.Name, childPath);
                Scan(sub, child, folders, tracks, depth + 1);

                if (child.TrackCount > 0)               // папки без музыки не показываем
                {
                    node.Folders.Add(child);
                    folders[child.Path] = child;
                }
            }
            else if (entry is FileInfo file && _extensions.Contains(file.Extension))
            {
                try
                {
                    var relative = node.Path.Length == 0 ? file.Name : node.Path + "/" + file.Name;
                    var track = new TrackEntry(relative, file.Name, file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
                    node.Tracks.Add(track);
                    tracks[relative] = track;
                }
                catch { /* битая ссылка и т.п. */ }
            }
        }

        node.Folders.Sort((a, b) => NaturalComparer.Instance.Compare(a.Name, b.Name));
        node.Tracks.Sort((a, b) => NaturalComparer.Instance.Compare(a.FileName, b.FileName));
        node.TrackCount = node.Tracks.Count + node.Folders.Sum(f => f.TrackCount);
    }

    /// <summary>Все треки поддерева в порядке отображения: сначала подпапки, затем файлы самой папки.</summary>
    public static List<TrackEntry> Collect(FolderNode node)
    {
        var list = new List<TrackEntry>(node.TrackCount);
        void Walk(FolderNode n)
        {
            foreach (var f in n.Folders) Walk(f);
            list.AddRange(n.Tracks);
        }
        Walk(node);
        return list;
    }

    /// <summary>Приводит путь из запроса к виду индекса. null — путь недопустим (содержит . или ..).</summary>
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
            if (p == "." || p == "..") return null;
        return string.Join("/", parts);
    }

    public FolderNode? FindFolder(string? path)
    {
        var normalized = NormalizePath(path);
        return normalized != null && _index.Folders.TryGetValue(normalized, out var node) ? node : null;
    }

    /// <summary>Находит только проиндексированный музыкальный файл — выйти за пределы музыкальной папки нельзя.</summary>
    public TrackEntry? FindTrack(string? path)
    {
        var normalized = NormalizePath(path);
        return !string.IsNullOrEmpty(normalized) && _index.Tracks.TryGetValue(normalized, out var track) ? track : null;
    }
}
