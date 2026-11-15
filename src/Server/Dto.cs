namespace SunsetMP.Server;

/// <summary>Папка. Path — относительный путь от корня музыкальной папки, разделитель всегда '/'.</summary>
public sealed record FolderDto(string Name, string Path, int TrackCount);

/// <summary>Трек. Path — относительный путь файла (он же идентификатор для /api/stream и /api/download).</summary>
public sealed record TrackDto(
    string Path, string FileName, string Title, string Artist, string Album, long DurationMs, long Size);

/// <summary>Содержимое одной папки: подпапки и треки, лежащие прямо в ней.</summary>
public sealed record BrowseDto(string Path, string Name, IReadOnlyList<FolderDto> Folders, IReadOnlyList<TrackDto> Tracks);

public sealed record SearchDto(IReadOnlyList<FolderDto> Folders, IReadOnlyList<TrackDto> Tracks, bool Truncated);

public sealed record InfoDto(string Name, string Version, bool Ready, int Tracks, int Folders, DateTime? ScannedUtc);
