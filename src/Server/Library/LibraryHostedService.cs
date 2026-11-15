using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace SunsetMP.Server.Library;

/// <summary>Первое сканирование при запуске, слежение за папкой и фоновое чтение тегов.</summary>
public sealed class LibraryHostedService : BackgroundService
{
    readonly MusicLibrary _library;
    readonly MetadataCache _meta;
    readonly LibraryOptions _options;
    readonly ILogger<LibraryHostedService> _log;

    FileSystemWatcher? _watcher;
    System.Threading.Timer? _debounce;

    public LibraryHostedService(MusicLibrary library, MetadataCache meta,
        IOptions<LibraryOptions> options, ILogger<LibraryHostedService> log)
    {
        _library = library;
        _meta = meta;
        _options = options.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Run(() => _library.Rescan(), stoppingToken);
            if (_options.WatchForChanges) StartWatcher(stoppingToken);
            if (_options.WarmUpTags) await Task.Run(() => WarmUp(stoppingToken), stoppingToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ошибка при сканировании библиотеки");
        }
    }

    void WarmUp(CancellationToken ct)
    {
        var tracks = _library.Current.AllTracks;
        var sw = Stopwatch.StartNew();
        try
        {
            Parallel.ForEach(tracks, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                t => _meta.Get(t));
            _log.LogInformation("Теги прочитаны: {Count} файлов, {Sec} с", tracks.Count, sw.Elapsed.TotalSeconds.ToString("F1"));
        }
        catch (OperationCanceledException) { }
    }

    void StartWatcher(CancellationToken ct)
    {
        if (!Directory.Exists(_library.RootPath)) return;
        try
        {
            // изменения часто приходят пачкой (копирование альбома) — пересканируем один раз после паузы
            _debounce = new System.Threading.Timer(_ =>
            {
                try
                {
                    _library.Rescan();
                    if (_options.WarmUpTags) WarmUp(ct);
                }
                catch (Exception ex) { _log.LogWarning(ex, "Не удалось обновить библиотеку"); }
            });

            void Schedule() => _debounce?.Change(5000, Timeout.Infinite);

            _watcher = new FileSystemWatcher(_library.RootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Created += (_, _) => Schedule();
            _watcher.Deleted += (_, _) => Schedule();
            _watcher.Changed += (_, _) => Schedule();
            _watcher.Renamed += (_, _) => Schedule();
            _watcher.Error += (_, _) => Schedule();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Слежение за папкой недоступно — обновляйте библиотеку через POST /api/rescan");
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        base.Dispose();
    }
}
