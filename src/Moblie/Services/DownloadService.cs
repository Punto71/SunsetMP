using System.Windows.Input;
using SunsetMP.Models;

namespace SunsetMP.Services;

/// <summary>Очередь скачивания треков из SunsetMP в Music/SunsetMP.</summary>
public class DownloadService : BaseViewModel
{
    sealed record Job(string Title, List<DownloadItem> Items);

    readonly IFileSaver _saver;
    readonly IDownloadHost _host;
    readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    readonly Queue<Job> _queue = new();
    CancellationTokenSource? _cts;
    bool _running;
    int _total, _done, _ok, _skipped, _failed;

    /// <summary>Очередь закончилась (или отменена): итоговое сообщение</summary>
    public event Action<string>? Finished;

    public ICommand CancelCommand { get; }

    bool _isActive;
    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }

    string _text = "";
    public string Text { get => _text; private set => Set(ref _text, value); }

    double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public DownloadService(IFileSaver saver, IDownloadHost host)
    {
        _saver = saver;
        _host = host;
        _host.CancelRequested += Cancel;
        CancelCommand = new Command(Cancel);
    }

    /// <summary>Добавляет задание в очередь. false — скачивание не начато (нет разрешения).</summary>
    public async Task<bool> EnqueueAsync(string title, List<DownloadItem> items)
    {
        if (items.Count == 0) return false;

        // разрешение нужно запрашивать из UI-потока
        if (!await _saver.EnsurePermissionAsync())
        {
            Finished?.Invoke("Нет разрешения на запись в память устройства");
            return false;
        }

        CancellationToken token;
        lock (_queue)
        {
            _queue.Enqueue(new Job(title, items));
            _total += items.Count;
            if (_running) return true;     // уже качаем — задание подхватит текущий цикл

            _running = true;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        IsActive = true;
        Text = "Подготовка…";
        Progress = 0;
        _ = Task.Run(() => RunAsync(token));
        return true;
    }

    public void Cancel()
    {
        lock (_queue) { _cts?.Cancel(); }
    }

    async Task RunAsync(CancellationToken ct)
    {
        (int ok, int skipped, int failed, bool cancelled) result;

        while (true)
        {
            Job job;
            lock (_queue)
            {
                if (ct.IsCancellationRequested || _queue.Count == 0)
                {
                    result = (_ok, _skipped, _failed, ct.IsCancellationRequested);
                    _queue.Clear();
                    _running = false;
                    _total = _done = _ok = _skipped = _failed = 0;
                    break;
                }
                job = _queue.Dequeue();
            }

            foreach (var item in job.Items)
            {
                if (ct.IsCancellationRequested) break;
                await DownloadOneAsync(item, ct);
                _done++;
            }
        }

        var message = result.cancelled
            ? $"Скачивание отменено. Скачано: {result.ok}"
            : $"Скачано: {result.ok}"
              + (result.skipped > 0 ? $", уже было: {result.skipped}" : "")
              + (result.failed > 0 ? $", ошибок: {result.failed}" : "");

        _host.Hide();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsActive = false;
            Progress = 0;
            Text = "";
            Finished?.Invoke(message);
        });
    }

    async Task DownloadOneAsync(DownloadItem item, CancellationToken ct)
    {
        try
        {
            SetStatus(item.FileName, 0);

            // уже скачан раньше — пропускаем без запроса к серверу
            if (!item.NeedsExtension && _saver.Exists(item.Dirs, item.FileName)) { _skipped++; return; }

            using var response = await _http.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var fileName = item.NeedsExtension
                ? item.FileName + ExtensionFor(response.Content.Headers.ContentType?.MediaType)
                : item.FileName;
            if (item.NeedsExtension && _saver.Exists(item.Dirs, fileName)) { _skipped++; return; }

            var total = response.Content.Headers.ContentLength ?? 0;
            long copied = 0;
            var lastUpdate = Environment.TickCount64;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await _saver.SaveAsync(item.Dirs, fileName, async target =>
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    copied += read;

                    var now = Environment.TickCount64;
                    if (now - lastUpdate > 400)
                    {
                        lastUpdate = now;
                        SetStatus(fileName, total > 0 ? (double)copied / total : 0);
                    }
                }
            });
            _ok++;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // отмена: недописанный файл уже удалён, ошибкой это не считаем
        }
        catch
        {
            _failed++;
        }
    }

    void SetStatus(string fileName, double fraction)
    {
        var total = Math.Max(_total, 1);
        var overall = Math.Clamp((_done + fraction) / total, 0, 1);
        var text = $"{Math.Min(_done + 1, total)} из {total} · {fileName}";

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Text = text;
            Progress = overall;
        });
        _host.Show(text, (int)(overall * 100));
    }

    static string ExtensionFor(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/flac" or "audio/x-flac" => ".flac",
        "audio/mp4" or "audio/x-m4a" or "audio/m4a" => ".m4a",
        "audio/aac" or "audio/aacp" => ".aac",
        "audio/ogg" or "application/ogg" => ".ogg",
        "audio/opus" => ".opus",
        "audio/wav" or "audio/x-wav" or "audio/wave" => ".wav",
        "audio/x-ms-wma" => ".wma",
        _ => ".mp3"
    };
}
