namespace SunsetMP.Models;

public class Track : BaseViewModel
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    /// <summary>content://... для локальных файлов или http(s)://... для SunsetMP</summary>
    public string Uri { get; set; } = "";
    public long DurationMs { get; set; }
    /// <summary>Путь к файлу на сервере SunsetMP (если известен) — нужен для скачивания</summary>
    public string? ServerPath { get; set; }
    /// <summary>Ссылка для скачивания файла (если отличается от Uri для прослушивания)</summary>
    public string? DownloadUri { get; set; }

    bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }

    public string DurationText => DurationMs <= 0 ? "" : FormatTime(DurationMs);

    public Track Clone() => new()
    {
        Title = Title, Artist = Artist, Uri = Uri, DurationMs = DurationMs, ServerPath = ServerPath, DownloadUri = DownloadUri
    };

    public static string FormatTime(double ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
