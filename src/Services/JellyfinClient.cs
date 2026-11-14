using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SunsetMP.Models;

namespace SunsetMP.Services;

public record JfItem(string Id, string Name, string Type, bool IsFolder, string? CollectionType, string Artists, long DurationMs, string? Path = null);

public class JellyfinClient
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string ServerUrl { get; private set; } = "";
    public string UserId { get; private set; } = "";
    public string? Token { get; private set; }
    public bool HasToken => !string.IsNullOrEmpty(Token);

    static string DeviceId
    {
        get
        {
            var id = Preferences.Get("jf_device_id", "");
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                Preferences.Set("jf_device_id", id);
            }
            return id;
        }
    }

    public static string NormalizeUrl(string url)
    {
        url = (url ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) return url;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;
        return url;
    }

    string AuthHeader()
    {
        var h = $"MediaBrowser Client=\"SunsetMP\", Device=\"Android\", DeviceId=\"{DeviceId}\", Version=\"1.0.0\"";
        if (HasToken) h += $", Token=\"{Token}\"";
        return h;
    }

    HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("X-Emby-Authorization", AuthHeader());
        req.Headers.TryAddWithoutValidation("Authorization", AuthHeader());
        return req;
    }

    public bool TryRestore()
    {
        var server = Preferences.Get("jf_server", "");
        var token = Preferences.Get("jf_token", "");
        var userId = Preferences.Get("jf_userid", "");
        if (server.Length == 0 || token.Length == 0 || userId.Length == 0) return false;
        ServerUrl = server; Token = token; UserId = userId;
        return true;
    }

    public async Task LoginAsync(string server, string user, string password)
    {
        server = NormalizeUrl(server);
        if (server.Length == 0) throw new Exception("Укажите адрес сервера");
        if (string.IsNullOrWhiteSpace(user)) throw new Exception("Укажите имя пользователя");

        Token = null;
        var req = CreateRequest(HttpMethod.Post, $"{server}/Users/AuthenticateByName");
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { Username = user, Pw = password ?? "" }),
            Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new Exception("Неверное имя пользователя или пароль");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Token = doc.RootElement.GetProperty("AccessToken").GetString();
        UserId = doc.RootElement.GetProperty("User").GetProperty("Id").GetString() ?? "";
        ServerUrl = server;

        Preferences.Set("jf_server", ServerUrl);
        Preferences.Set("jf_user", user);
        Preferences.Set("jf_token", Token ?? "");
        Preferences.Set("jf_userid", UserId);
    }

    public void Disconnect()
    {
        Token = null; UserId = "";
        Preferences.Remove("jf_token");
        Preferences.Remove("jf_userid");
    }

    async Task<string> GetJsonAsync(string pathAndQuery, CancellationToken ct = default)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Get, ServerUrl + pathAndQuery);
            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new Exception("Сессия истекла, подключитесь заново");
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Android при отмене запроса бросает не OperationCanceledException, а IOException("Canceled")
            throw new OperationCanceledException(ct);
        }
    }

    static List<JfItem> Parse(string json)
    {
        var list = new List<JfItem>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Items", out var arr)) return list;

        foreach (var e in arr.EnumerateArray())
        {
            string? Str(string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var artists = "";
            if (e.TryGetProperty("Artists", out var a) && a.ValueKind == JsonValueKind.Array)
                artists = string.Join(", ", a.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)));
            if (artists.Length == 0) artists = Str("AlbumArtist") ?? "";

            long ticks = e.TryGetProperty("RunTimeTicks", out var rt) && rt.ValueKind == JsonValueKind.Number ? rt.GetInt64() : 0;
            bool isFolder = e.TryGetProperty("IsFolder", out var f) && f.ValueKind == JsonValueKind.True;

            var id = Str("Id");
            if (id == null) continue;
            list.Add(new JfItem(id, Str("Name") ?? "?", Str("Type") ?? "", isFolder, Str("CollectionType"), artists, ticks / 10000, Str("Path")));
        }
        return list;
    }

    /// <summary>Дочерние элементы: parent == null → музыкальные библиотеки пользователя.</summary>
    public async Task<List<JfItem>> GetChildrenAsync(JfItem? parent)
    {
        if (parent == null)
        {
            var views = Parse(await GetJsonAsync($"/Users/{UserId}/Views"));
            return views.Where(v => v.CollectionType == null || v.CollectionType is "music" or "playlists").ToList();
        }

        var items = new List<JfItem>();
        if (parent.Type == "MusicArtist")
            items = Parse(await GetJsonAsync(
                $"/Items?userId={UserId}&albumArtistIds={parent.Id}&includeItemTypes=MusicAlbum&recursive=true&sortBy=ProductionYear,SortName"));

        if (items.Count == 0)
        {
            var sort = parent.Type == "Playlist" ? "" : "&sortBy=ParentIndexNumber,IndexNumber,SortName";
            items = Parse(await GetJsonAsync($"/Items?userId={UserId}&parentId={parent.Id}{sort}"));
        }

        items = items.Where(i => i.IsFolder || i.Type == "Audio").ToList();
        return items.Where(i => i.IsFolder).Concat(items.Where(i => !i.IsFolder)).ToList();
    }

    /// <summary>Все аудиотреки внутри папки/альбома/артиста/плейлиста (рекурсивно).</summary>
    public async Task<List<Track>> GetTracksAsync(JfItem item)
    {
        if (!item.IsFolder) return new List<Track> { ToTrack(item) };

        var sort = item.Type switch
        {
            "Playlist" => "",
            "MusicAlbum" => "&sortBy=ParentIndexNumber,IndexNumber,SortName",
            _ => "&sortBy=AlbumArtist,Album,ParentIndexNumber,IndexNumber,SortName"
        };

        var items = new List<JfItem>();
        if (item.Type == "MusicArtist")
            items = Parse(await GetJsonAsync(
                $"/Items?userId={UserId}&albumArtistIds={item.Id}&recursive=true&includeItemTypes=Audio{sort}"));

        if (items.Count == 0)
            items = Parse(await GetJsonAsync(
                $"/Items?userId={UserId}&parentId={item.Id}&recursive=true&includeItemTypes=Audio{sort}"));

        return items.Where(i => i.Type == "Audio").Select(ToTrack).ToList();
    }

    /// <summary>Поиск по серверу: исполнители, альбомы, плейлисты, треки.</summary>
    public async Task<List<JfItem>> SearchAsync(string query, CancellationToken ct)
    {
        var q = Uri.EscapeDataString(query);
        var itemsTask = GetJsonAsync(
            $"/Items?userId={UserId}&searchTerm={q}&recursive=true&includeItemTypes=MusicAlbum,Playlist,Audio&limit=80", ct);
        var artistsTask = GetJsonAsync($"/Artists?userId={UserId}&searchTerm={q}&limit=20", ct);
        await Task.WhenAll(itemsTask, artistsTask);

        var artists = Parse(artistsTask.Result)
            .Where(i => i.Type == "MusicArtist")
            .Select(i => i with { IsFolder = true })
            .ToList();
        var items = Parse(itemsTask.Result);

        return artists
            .Concat(items.Where(i => i.Type == "MusicAlbum"))
            .Concat(items.Where(i => i.Type == "Playlist"))
            .Concat(items.Where(i => i.Type == "Audio"))
            .ToList();
    }

    static int TotalOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("TotalRecordCount", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt32() : 0;
    }

    /// <summary>Все аудиотреки библиотеки вместе с путём к файлу на сервере (постранично).</summary>
    public async Task<List<JfItem>> GetAudioWithPathAsync(string libraryId, CancellationToken ct = default)
    {
        const int page = 1000;
        var all = new List<JfItem>();
        var start = 0;
        while (true)
        {
            var json = await GetJsonAsync(
                $"/Items?userId={UserId}&parentId={libraryId}&recursive=true&includeItemTypes=Audio" +
                $"&fields=Path&enableImages=false&enableUserData=false&sortBy=SortName&startIndex={start}&limit={page}", ct);

            var items = Parse(json);
            all.AddRange(items.Where(i => i.Type == "Audio"));
            start += page;
            if (items.Count == 0 || start >= TotalOf(json)) break;
        }
        return all;
    }

    public Track ToTrack(JfItem i) => new()
    {
        Title = i.Name,
        Artist = i.Artists,
        DurationMs = i.DurationMs,
        Uri = $"{ServerUrl}/Audio/{i.Id}/stream?static=true&api_key={Token}"
    };
}
