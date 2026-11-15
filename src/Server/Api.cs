using SunsetMP.Server.Library;

namespace SunsetMP.Server;

public static class Api
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Проверка соединения и токена
        api.MapGet("/ping", (MusicLibrary library) => Results.Ok(Info(library)));

        // Содержимое ОДНОЙ папки: подпапки + треки, лежащие прямо в ней. path пустой — корень.
        api.MapGet("/browse", (string? path, MusicLibrary library, MetadataCache meta) =>
        {
            if (!library.IsReady) return NotReady();
            var node = library.FindFolder(path);
            if (node == null) return Results.NotFound(new { error = "Папка не найдена" });

            return Results.Ok(new BrowseDto(
                node.Path, node.Name,
                node.Folders.Select(ToDto).ToList(),
                ToDtos(meta, node.Tracks)));
        });

        // Все треки папки со всеми вложенными (для «играть папку» и скачивания), в порядке отображения
        api.MapGet("/tracks", (string? path, MusicLibrary library, MetadataCache meta) =>
        {
            if (!library.IsReady) return NotReady();
            var node = library.FindFolder(path);
            if (node == null) return Results.NotFound(new { error = "Папка не найдена" });

            return Results.Ok(ToDtos(meta, MusicLibrary.Collect(node)));
        });

        // Поиск по папкам и трекам
        api.MapGet("/search", (string? q, MusicLibrary library, MetadataCache meta) =>
        {
            if (!library.IsReady) return NotReady();

            var result = TextSearch.Find(library.Current, meta, q, maxFolders: 100, maxTracks: 300);
            return Results.Ok(new SearchDto(
                result.Folders.Select(ToDto).ToList(),
                ToDtos(meta, result.Tracks),
                result.Truncated));
        });

        // Прослушивание: отдаёт файл с поддержкой Range (перемотка)
        api.MapGet("/stream", (string? path, MusicLibrary library) => ServeFile(library, path, download: false));

        // Скачивание: тот же файл, но с заголовком attachment
        api.MapGet("/download", (string? path, MusicLibrary library) => ServeFile(library, path, download: true));

        // Принудительно пересканировать папку с музыкой
        api.MapPost("/rescan", (MusicLibrary library) =>
        {
            library.Rescan();
            return Results.Ok(Info(library));
        });
    }

    static IResult ServeFile(MusicLibrary library, string? path, bool download)
    {
        if (!library.IsReady) return NotReady();

        var track = library.FindTrack(path);
        if (track == null || !File.Exists(track.FullPath))
            return Results.NotFound(new { error = "Файл не найден" });

        return Results.File(
            track.FullPath,
            contentType: ContentTypes.For(track.FileName),
            fileDownloadName: download ? track.FileName : null,
            enableRangeProcessing: true);
    }

    static IResult NotReady()
        => Results.Json(new { error = "Идёт первое сканирование библиотеки, повторите через минуту" }, statusCode: 503);

    static InfoDto Info(MusicLibrary library)
    {
        var index = library.Current;
        return new InfoDto("SunsetMP Server", "1.0", library.IsReady,
            index.AllTracks.Count, Math.Max(index.Folders.Count - 1, 0), index.ScannedUtc);
    }

    static FolderDto ToDto(FolderNode f) => new(f.Name, f.Path, f.TrackCount);

    static List<TrackDto> ToDtos(MetadataCache meta, IReadOnlyList<TrackEntry> tracks)
    {
        var metas = meta.GetMany(tracks);
        var result = new List<TrackDto>(tracks.Count);
        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            var m = metas[i];
            result.Add(new TrackDto(t.Path, t.FileName, m.Title, m.Artist, m.Album, m.DurationMs, t.Size));
        }
        return result;
    }
}
