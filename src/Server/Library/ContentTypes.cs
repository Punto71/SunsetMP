namespace SunsetMP.Server.Library;

public static class ContentTypes
{
    static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".m4a"] = "audio/mp4",
        [".alac"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".wma"] = "audio/x-ms-wma",
        [".ape"] = "audio/x-ape",
        [".aiff"] = "audio/aiff"
    };

    public static string For(string fileName)
        => Map.TryGetValue(System.IO.Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";
}
