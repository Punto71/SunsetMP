using Android.Content;
using Android.Media;
using Android.Provider;
using SunsetMP.Services;

namespace SunsetMP;

/// <summary>
/// Android 10+ — через MediaStore (разрешения не нужны, файлы видны в «Музыке» и файловых менеджерах).
/// Android 8–9 — обычная запись в /sdcard/Music/SunsetMP (нужно разрешение на запись).
/// </summary>
public class AndroidFileSaver : IFileSaver
{
    const string BaseDir = "Music/SunsetMP";

    public async Task<bool> EnsurePermissionAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) return true;

        var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.StorageWrite>();
        return status == PermissionStatus.Granted;
    }

    static string RelativePath(string[] dirs)
        => dirs.Length == 0 ? BaseDir + "/" : BaseDir + "/" + string.Join("/", dirs) + "/";

    static string LegacyPath(string[] dirs, string fileName)
    {
        var parts = new List<string>
        {
            Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath, "Music", "SunsetMP"
        };
        parts.AddRange(dirs);
        parts.Add(fileName);
        return Path.Combine(parts.ToArray());
    }

    static string MimeFor(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var mime = Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(ext);
        return mime != null && mime.StartsWith("audio/") ? mime : "audio/mpeg";
    }

    public bool Exists(string[] dirs, string fileName)
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                var resolver = Android.App.Application.Context.ContentResolver!;
                using var cursor = resolver.Query(
                    MediaStore.Audio.Media.ExternalContentUri!, new[] { "_id" },
                    "relative_path = ? AND _display_name = ?", new[] { RelativePath(dirs), fileName }, null);
                return cursor != null && cursor.Count > 0;
            }
            return File.Exists(LegacyPath(dirs, fileName));
        }
        catch { return false; }
    }

    public Task SaveAsync(string[] dirs, string fileName, Func<System.IO.Stream, Task> write)
        => OperatingSystem.IsAndroidVersionAtLeast(29)
            ? SaveMediaStoreAsync(dirs, fileName, write)
            : SaveFileAsync(dirs, fileName, write);

    static async Task SaveMediaStoreAsync(string[] dirs, string fileName, Func<System.IO.Stream, Task> write)
    {
        var resolver = Android.App.Application.Context.ContentResolver!;
        var collection = MediaStore.Audio.Media.ExternalContentUri!;

        var values = new ContentValues();
        values.Put("_display_name", fileName);
        values.Put("mime_type", MimeFor(fileName));
        values.Put("relative_path", RelativePath(dirs));   // подпапки создаются автоматически
        values.Put("is_pending", 1);                       // пока пишем — файл не виден другим приложениям

        var uri = resolver.Insert(collection, values) ?? throw new IOException("Не удалось создать файл");
        try
        {
            using (var output = resolver.OpenOutputStream(uri) ?? throw new IOException("Не удалось открыть файл"))
            {
                await write(output);
                await output.FlushAsync();
            }

            var done = new ContentValues();
            done.Put("is_pending", 0);
            resolver.Update(uri, done, null, null);
        }
        catch
        {
            try { resolver.Delete(uri, null, null); } catch { }
            throw;
        }
    }

    static async Task SaveFileAsync(string[] dirs, string fileName, Func<System.IO.Stream, Task> write)
    {
        var path = LegacyPath(dirs, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = path + ".part";
        try
        {
            await using (var output = File.Create(temp))
                await write(output);
            File.Move(temp, path, true);
            MediaScannerConnection.ScanFile(Android.App.Application.Context, new[] { path }, null, null);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }
}
