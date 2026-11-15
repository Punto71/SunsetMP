namespace SunsetMP.Services;

/// <summary>Сохранение скачанных файлов в Music/SunsetMP (реализация — Platforms/Android).</summary>
public interface IFileSaver
{
    /// <summary>Запросить разрешение на запись (на Android 10+ не нужно). false — записывать нельзя.</summary>
    Task<bool> EnsurePermissionAsync();

    /// <summary>Файл уже есть: Music/SunsetMP/{dirs}/{fileName}</summary>
    bool Exists(string[] dirs, string fileName);

    /// <summary>
    /// Создаёт файл Music/SunsetMP/{dirs}/{fileName} и вызывает write для записи содержимого.
    /// Если write бросает исключение — недописанный файл удаляется.
    /// </summary>
    Task SaveAsync(string[] dirs, string fileName, Func<Stream, Task> write);
}

/// <summary>Уведомление о ходе скачивания и удержание процесса в фоне (реализация — Platforms/Android).</summary>
public interface IDownloadHost
{
    /// <summary>Пользователь нажал «Отмена» в уведомлении</summary>
    event Action? CancelRequested;

    void Show(string text, int percent);
    void Hide();
}
