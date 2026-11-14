# SunsetMP (.NET MAUI, Android)

Плеер локальной музыки и музыки с сервера Jellyfin. Вкладки: Плейлист / Локальные / Jellyfin.

## Сборка
Требуется .NET 10 SDK + workload `maui-android` (`dotnet workload install maui-android`).

    dotnet build -f net10.0-android -c Release
    # запуск на подключённом устройстве/эмуляторе:
    dotnet build -t:Run -f net10.0-android

Для .NET 9: в SunsetMP.csproj замените `net10.0-android` на `net9.0-android`.
Либо откройте SunsetMP.csproj в Visual Studio 2022/2026 и нажмите Run.
