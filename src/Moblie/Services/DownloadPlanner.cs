using SunsetMP.Models;

namespace SunsetMP.Services;

/// <param name="Url">Откуда скачивать</param>
/// <param name="Dirs">Подпапки внутри Music/SunsetMP</param>
/// <param name="FileName">Имя файла</param>
/// <param name="NeedsExtension">Расширение неизвестно — определим по Content-Type ответа</param>
public sealed record DownloadItem(string Url, string[] Dirs, string FileName, bool NeedsExtension);

/// <summary>Решает, в какую подпапку Music/SunsetMP попадёт каждый скачиваемый трек.</summary>
public static class DownloadPlanner
{
    static readonly char[] PathSeparators = { '/', '\\' };
    static readonly HashSet<char> InvalidChars = new("\\/:*?\"<>|".Concat(Path.GetInvalidFileNameChars()));

    /// <summary>
    /// Один трек — в корень Music/SunsetMP.
    /// Папка — Music/SunsetMP/{имя папки}/...: вложенные папки повторяются так, как они лежат на сервере.
    /// </summary>
    /// <param name="skipSegments">
    /// Сколько первых сегментов серверного пути занимает сама выбранная папка (они в иерархию не входят).
    /// -1 — определить автоматически по общей части путей.
    /// </param>
    public static List<DownloadItem> Plan(IReadOnlyList<string> rootSegments, bool singleTrack, IReadOnlyList<Track> tracks, int skipSegments = -1)
    {
        var root = singleTrack
            ? Array.Empty<string>()
            : rootSegments.Select(Sanitize).Where(s => s.Length > 0).ToArray();

        // Общая часть путей на сервере (/mnt/music/Rock) — это и есть скачиваемая папка;
        // всё, что глубже, повторяем как подпапки.
        var commonLength = 0;
        if (!singleTrack)
        {
            var dirs = tracks.Select(t => SplitPath(t.ServerPath)).Where(s => s.Length > 0).Select(s => s[..^1]).ToList();
            commonLength = skipSegments >= 0 ? skipSegments : CommonPrefixLength(dirs);
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DownloadItem>();

        foreach (var t in tracks)
        {
            var segments = SplitPath(t.ServerPath);
            string[] relative = Array.Empty<string>();
            string name;
            bool needsExtension;

            if (segments.Length > 0)
            {
                name = Sanitize(segments[^1]);
                needsExtension = !Path.HasExtension(name);
                if (!singleTrack)
                    relative = segments[..^1].Skip(commonLength).Select(Sanitize).Where(s => s.Length > 0).ToArray();
            }
            else
            {
                name = Sanitize(string.IsNullOrWhiteSpace(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}");
                needsExtension = true;
            }
            if (name.Length == 0) name = "track";
            name = LimitLength(name);

            var dirsFull = root.Concat(relative).ToArray();

            // одинаковые имена в одной папке: «track (2).mp3»
            var stem = needsExtension ? name : Path.GetFileNameWithoutExtension(name);
            var ext = needsExtension ? "" : Path.GetExtension(name);
            var candidate = name;
            for (var n = 2; !used.Add(string.Join("/", dirsFull) + "/" + candidate); n++)
                candidate = $"{stem} ({n}){ext}";

            result.Add(new DownloadItem(t.DownloadUri ?? t.Uri, dirsFull, candidate, needsExtension));
        }
        return result;
    }

    static string[] SplitPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? Array.Empty<string>() : path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);

    static int CommonPrefixLength(List<string[]> dirs)
    {
        if (dirs.Count == 0) return 0;
        var max = dirs.Min(d => d.Length);
        for (var i = 0; i < max; i++)
        {
            var segment = dirs[0][i];
            if (dirs.Any(d => !string.Equals(d[i], segment, StringComparison.OrdinalIgnoreCase))) return i;
        }
        return max;
    }

    /// <summary>Убирает символы, недопустимые в именах файлов и папок.</summary>
    public static string Sanitize(string s)
    {
        var chars = s.Select(c => InvalidChars.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        return new string(chars).Trim().TrimEnd('.', ' ');
    }

    static string LimitLength(string name)
    {
        const int max = 150;
        if (name.Length <= max) return name;
        var ext = Path.GetExtension(name);
        if (ext.Length > 10) ext = "";
        return name[..(max - ext.Length)] + ext;
    }
}
