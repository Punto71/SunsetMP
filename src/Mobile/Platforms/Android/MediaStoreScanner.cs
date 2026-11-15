using Android.Content;
using Android.Provider;
using SunsetMP.Models;
using SunsetMP.Services;

namespace SunsetMP;

public class MediaStoreScanner : ILocalMusicScanner
{
    public async Task<bool> RequestPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<AudioReadPermission>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<AudioReadPermission>();

        // Уведомление о воспроизведении (Android 13+). Отказ не критичен.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            try
            {
                var ns = await Permissions.CheckStatusAsync<NotificationPermission>();
                if (ns != PermissionStatus.Granted)
                    await Permissions.RequestAsync<NotificationPermission>();
            }
            catch { /* ignore */ }
        }
        return status == PermissionStatus.Granted;
    }

    public Task<List<LocalFile>> ScanAsync() => Task.Run(() =>
    {
        var result = new List<LocalFile>();
        var ctx = Android.App.Application.Context;
        var baseUri = MediaStore.Audio.Media.ExternalContentUri!;
        string[] projection = { "_id", "_data", "title", "artist", "duration" };

        using var cursor = ctx.ContentResolver!.Query(baseUri, projection, "is_music != 0", null, "_data ASC");
        if (cursor == null) return result;

        int iId = cursor.GetColumnIndex("_id");
        int iData = cursor.GetColumnIndex("_data");
        int iTitle = cursor.GetColumnIndex("title");
        int iArtist = cursor.GetColumnIndex("artist");
        int iDuration = cursor.GetColumnIndex("duration");

        while (cursor.MoveToNext())
        {
            var path = cursor.GetString(iData) ?? "";
            if (path.Length == 0) continue;

            var artist = cursor.GetString(iArtist) ?? "";
            if (artist == "<unknown>") artist = "";

            var title = cursor.GetString(iTitle);
            if (string.IsNullOrWhiteSpace(title)) title = Path.GetFileNameWithoutExtension(path);

            var uri = ContentUris.WithAppendedId(baseUri, cursor.GetLong(iId))!.ToString()!;

            result.Add(new LocalFile(path, new Track
            {
                Title = title!,
                Artist = artist,
                DurationMs = cursor.GetLong(iDuration),
                Uri = uri
            }));
        }
        return result;
    });
}
