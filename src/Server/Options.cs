namespace SunsetMP.Server;

public sealed class LibraryOptions
{
    /// <summary>Папка с музыкой</summary>
    public string MusicPath { get; set; } = "";

    /// <summary>Расширения музыкальных файлов. null — стандартный список.</summary>
    public string[]? Extensions { get; set; }

    public bool WatchForChanges { get; set; } = true;
    public bool WarmUpTags { get; set; } = true;
}

public sealed class AuthOptions
{
    /// <summary>Единый токен доступа. Если не задан в appsettings.json — используется DefaultToken.</summary>
    public string Token { get; set; } = "";

    /// <summary>Запасной «зашитый» токен — ЗАМЕНИТЕ его (или задайте Auth:Token в appsettings.json).</summary>
    public const string DefaultToken = "sunset-change-me";
}
