namespace SunsetMP.Services;

/// <summary>
/// Кэш в памяти для удалённых (http/https) треков: скачивает файл целиком, если он меньше 50 МБ.
/// Хранит не больше двух треков — текущий и следующий. Файлы больше лимита и ответы без
/// Content-Length (перекодирование на сервере) не кэшируются — их проигрываем потоком.
/// </summary>
public class TrackCache
{
    public const long MaxTrackBytes = 50L * 1024 * 1024;

    readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    readonly object _lock = new();
    readonly Dictionary<string, Entry> _entries = new();

    sealed class Entry
    {
        public Task<byte[]?> Task = null!;
        public readonly CancellationTokenSource Cts = new();
        public byte[]? Data;
        public volatile bool Done;
    }

    /// <summary>Данные уже скачаны и лежат в памяти</summary>
    public bool TryGet(string url, out byte[] data)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(url, out var e) && e.Done && e.Data != null)
            {
                data = e.Data;
                return true;
            }
        }
        data = Array.Empty<byte>();
        return false;
    }

    /// <summary>Запускает скачивание (или возвращает уже идущее). Результат null — в память не берём.</summary>
    public Task<byte[]?> GetAsync(string url)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(url, out var existing)) return existing.Task;
            var entry = new Entry();
            _entries[url] = entry;
            entry.Task = Task.Run(() => DownloadAsync(url, entry));
            return entry.Task;
        }
    }

    public void Start(string url) => _ = GetAsync(url);

    public void Cancel(string url)
    {
        Entry? e;
        lock (_lock) { _entries.Remove(url, out e); }
        try { e?.Cts.Cancel(); } catch { }
    }

    /// <summary>Оставить в кэше только эти два адреса, остальное отменить и освободить.</summary>
    public void SetWanted(string? a, string? b)
    {
        List<Entry> drop;
        lock (_lock)
        {
            var keys = _entries.Keys.Where(k => k != a && k != b).ToList();
            drop = new List<Entry>(keys.Count);
            foreach (var k in keys)
            {
                drop.Add(_entries[k]);
                _entries.Remove(k);
            }
        }
        foreach (var e in drop)
        {
            try { e.Cts.Cancel(); } catch { }
        }
    }

    async Task<byte[]?> DownloadAsync(string url, Entry entry)
    {
        var ct = entry.Cts.Token;
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) { Forget(url, entry); return null; }

            var len = resp.Content.Headers.ContentLength;
            if (len is null or <= 0 || len > MaxTrackBytes)
            {
                entry.Done = true;      // «в память не берём» — запоминаем, чтобы не качать повторно
                return null;
            }

            var buffer = new byte[(int)len.Value];
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
                if (n == 0) break;
                read += n;
            }
            if (read != buffer.Length) { Forget(url, entry); return null; }

            entry.Data = buffer;
            entry.Done = true;
            return buffer;
        }
        catch
        {
            Forget(url, entry);         // сеть/отмена — при следующем обращении попробуем снова
            return null;
        }
    }

    void Forget(string url, Entry entry)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(url, out var cur) && cur == entry) _entries.Remove(url);
        }
        entry.Done = true;
    }
}
