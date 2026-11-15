using System.Collections.Concurrent;

namespace SunsetMP.Server.Library;

public sealed record TrackMeta(string Title, string Artist, string Album, long DurationMs);

/// <summary>Кэш тегов в памяти. Запись считается устаревшей, если изменился размер или дата файла.</summary>
public sealed class MetadataCache
{
    sealed record Entry(long Size, long Ticks, TrackMeta Meta);

    readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.Ordinal);

    /// <summary>Теги, если они уже прочитаны (файл не открывается)</summary>
    public TrackMeta? TryGetCached(TrackEntry t)
        => _cache.TryGetValue(t.Path, out var e) && e.Size == t.Size && e.Ticks == t.ModifiedTicks ? e.Meta : null;

    public TrackMeta Get(TrackEntry t)
    {
        var cached = TryGetCached(t);
        if (cached != null) return cached;

        var meta = Read(t);
        _cache[t.Path] = new Entry(t.Size, t.ModifiedTicks, meta);
        return meta;
    }

    /// <summary>Теги списка треков (параллельно), порядок сохраняется.</summary>
    public TrackMeta[] GetMany(IReadOnlyList<TrackEntry> tracks)
    {
        var result = new TrackMeta[tracks.Count];
        Parallel.For(0, tracks.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => result[i] = Get(tracks[i]));
        return result;
    }

    static TrackMeta Read(TrackEntry t)
    {
        var fallbackTitle = System.IO.Path.GetFileNameWithoutExtension(t.FileName);
        try
        {
            using var file = TagLib.File.Create(t.FullPath);
            var tag = file.Tag;

            var title = string.IsNullOrWhiteSpace(tag.Title) ? fallbackTitle : tag.Title.Trim();
            var artist = tag.FirstPerformer ?? tag.FirstAlbumArtist ?? "";
            var album = tag.Album ?? "";
            return new TrackMeta(title, artist.Trim(), album.Trim(), (long)file.Properties.Duration.TotalMilliseconds);
        }
        catch
        {
            // повреждённый файл или неподдерживаемый формат — показываем по имени файла
            return new TrackMeta(fallbackTitle, "", "", 0);
        }
    }
}
