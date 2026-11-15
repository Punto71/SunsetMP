using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SunsetMP.Models;

namespace SunsetMP.Services;

// Модели ответов сервера SunsetMP.Server
public record ServerFolder(string Name, string Path, int TrackCount);
public record ServerTrack(string Path, string FileName, string Title, string Artist, string Album, long DurationMs, long Size);
public record ServerBrowse(string Path, string Name, List<ServerFolder> Folders, List<ServerTrack> Tracks);
public record ServerSearch(List<ServerFolder> Folders, List<ServerTrack> Tracks, bool Truncated);
public record ServerInfo(string Name, string Version, bool Ready, int Tracks, int Folders);

/// <summary>Клиент к SunsetMP.Server: папки, поиск, ссылки для прослушивания и скачивания.</summary>
public class ServerClient
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public string BaseUrl { get; private set; } = "";
    public string Token { get; private set; } = "";
    public bool HasCredentials => BaseUrl.Length > 0 && Token.Length > 0;

    public static string NormalizeUrl(string url)
    {
        url = (url ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) return url;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;
        return url;
    }

    /// <summary>Подставляет адрес и токен, сохранённые при прошлом подключении</summary>
    public bool TryRestore()
    {
        var url = Preferences.Get("srv_url", "");
        var token = Preferences.Get("srv_token", "");
        if (url.Length == 0 || token.Length == 0) return false;
        BaseUrl = url;
        Token = token;
        return true;
    }

    public async Task ConnectAsync(string url, string token, CancellationToken ct = default)
    {
        url = NormalizeUrl(url);
        if (url.Length == 0) throw new Exception("Укажите адрес сервера");
        if (string.IsNullOrWhiteSpace(token)) throw new Exception("Укажите токен");

        BaseUrl = url;
        Token = token.Trim();
        try
        {
            await GetAsync<ServerInfo>("/api/ping", ct);   // заодно проверяет токен
        }
        catch
        {
            BaseUrl = "";
            Token = "";
            throw;
        }

        Preferences.Set("srv_url", BaseUrl);
        Preferences.Set("srv_token", Token);
    }

    public void Disconnect()
    {
        Token = "";
        Preferences.Remove("srv_token");
    }

    // ---------- Запросы ----------
    static string Q(string value) => Uri.EscapeDataString(value);

    /// <summary>Содержимое одной папки. path пустой — корень.</summary>
    public Task<ServerBrowse> BrowseAsync(string path, CancellationToken ct = default)
        => GetAsync<ServerBrowse>($"/api/browse?path={Q(path)}", ct);

    /// <summary>Все треки папки с вложенными</summary>
    public async Task<List<Track>> TracksAsync(string path, CancellationToken ct = default)
    {
        var items = await GetAsync<List<ServerTrack>>($"/api/tracks?path={Q(path)}", ct);
        return items.Select(ToTrack).ToList();
    }

    public Task<ServerSearch> SearchAsync(string query, CancellationToken ct = default)
        => GetAsync<ServerSearch>($"/api/search?q={Q(query)}", ct);

    // ---------- Ссылки ----------
    // Токен передаётся в параметре: плеер и скачивание ходят по обычному URL без заголовков
    public string StreamUrl(string path) => $"{BaseUrl}/api/stream?path={Q(path)}&token={Q(Token)}";
    public string DownloadUrl(string path) => $"{BaseUrl}/api/download?path={Q(path)}&token={Q(Token)}";

    public Track ToTrack(ServerTrack t) => new()
    {
        Title = string.IsNullOrWhiteSpace(t.Title) ? System.IO.Path.GetFileNameWithoutExtension(t.FileName) : t.Title,
        Artist = t.Artist,
        DurationMs = t.DurationMs,
        ServerPath = t.Path,
        Uri = StreamUrl(t.Path),
        DownloadUri = DownloadUrl(t.Path)
    };

    async Task<T> GetAsync<T>(string pathAndQuery, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + pathAndQuery);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

            using var response = await _http.SendAsync(request, ct);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized: throw new Exception("Неверный токен");
                case HttpStatusCode.NotFound: throw new Exception("Не найдено на сервере");
                case HttpStatusCode.ServiceUnavailable:
                    throw new Exception("Сервер сканирует библиотеку, повторите через минуту");
            }
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<T>(ct))!;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Android при отмене запроса бросает не OperationCanceledException, а IOException("Canceled")
            throw new OperationCanceledException(ct);
        }
        catch (TaskCanceledException)
        {
            throw new Exception("Сервер не отвечает");
        }
        catch (HttpRequestException ex)
        {
            throw new Exception("Не удалось связаться с сервером: " + ex.Message);
        }
    }
}
