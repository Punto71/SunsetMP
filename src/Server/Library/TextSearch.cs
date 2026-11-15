namespace SunsetMP.Server.Library;

/// <summary>Поиск без учёта регистра, ё = е. Запрос из нескольких слов: должны встретиться все.</summary>
public static class TextSearch
{
    public static string Normalize(string s) => s.ToLowerInvariant().Replace('ё', 'е');

    public static string[] Terms(string? query)
        => string.IsNullOrWhiteSpace(query)
            ? Array.Empty<string>()
            : Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public sealed record Result(List<FolderNode> Folders, List<TrackEntry> Tracks, bool Truncated);

    public static Result Find(LibraryIndex index, MetadataCache meta, string? query, int maxFolders, int maxTracks)
    {
        var terms = Terms(query);
        var folders = new List<FolderNode>();
        var tracks = new List<TrackEntry>();
        var truncated = false;
        if (terms.Length == 0) return new Result(folders, tracks, false);

        // Папка: все слова встречаются в её пути, и хотя бы одно — в её собственном названии.
        // Так «queen» найдёт папку Queen, но не все её альбомы, а «queen greatest» — нужный альбом.
        void WalkFolders(FolderNode n)
        {
            if (truncated) return;
            if (n.Path.Length > 0 &&
                terms.Any(n.SearchName.Contains) &&
                terms.All(n.SearchPath.Contains))
            {
                if (folders.Count >= maxFolders) { truncated = true; return; }
                folders.Add(n);
            }
            foreach (var f in n.Folders) WalkFolders(f);
        }
        WalkFolders(index.Root);

        // Трек: слова ищутся в пути и (если теги уже прочитаны) в названии, исполнителе и альбоме
        foreach (var t in index.AllTracks)
        {
            var haystack = t.SearchKey;
            var m = meta.TryGetCached(t);
            if (m != null) haystack += " " + Normalize($"{m.Title} {m.Artist} {m.Album}");

            if (!terms.All(haystack.Contains)) continue;
            if (tracks.Count >= maxTracks) { truncated = true; break; }
            tracks.Add(t);
        }
        return new Result(folders, tracks, truncated);
    }
}
