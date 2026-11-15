using SunsetMP.Models;

namespace SunsetMP.Services;

public record LocalFile(string Path, Track Track);

public interface ILocalMusicScanner
{
    Task<bool> RequestPermissionAsync();
    Task<List<LocalFile>> ScanAsync();
}
