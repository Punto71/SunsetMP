namespace SunsetMP.Services;

public interface IAudioEngine
{
    event Action? Prepared;
    event Action? Completed;
    event Action? Error;
    event Action? FocusLost;

    // Команды «снаружи»: кнопки гарнитуры, шторка уведомлений, экран блокировки,
    // отключение наушников (AUDIO_BECOMING_NOISY → RemotePause)
    event Action? RemotePlay;
    event Action? RemotePause;
    event Action? RemoteNext;
    event Action? RemotePrevious;
    event Action<long>? RemoteSeek;

    int PositionMs { get; }
    int DurationMs { get; }

    /// <param name="startPositionMs">С какой позиции начать (восстановление после перезапуска приложения)</param>
    void Play(string uri, string title, string artist, long durationMs, long startPositionMs = 0);
    void Pause();
    void Resume();
    void Stop();
    void SeekTo(int ms);

    /// <summary>Заранее загрузить следующий трек (только http/https, иначе — отмена предзагрузки)</summary>
    void Preload(string? uri);
}
