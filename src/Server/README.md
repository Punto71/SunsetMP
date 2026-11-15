# SunsetMP Server

Небольшой ASP.NET Core сервер, который отдаёт музыку из папки на диске: структура папок, поиск, прослушивание и скачивание.

## Запуск
1. Откройте `appsettings.json` и укажите:
   - `Library:MusicPath` — папка с музыкой;
   - `Auth:Token` — токен доступа (его же вводят в приложении);
   - `Urls` — адрес и порт (по умолчанию `http://0.0.0.0:5080`).
2. `dotnet run -c Release` (нужен .NET 10 SDK).

Docker: `docker build -t sunsetmp-server .` и
`docker run -p 5080:5080 -v /srv/music:/music -e Auth__Token=мой-токен sunsetmp-server`

При запуске сервер один раз сканирует папку (читает только имена файлов — быстро), затем следит за изменениями
и в фоне читает теги. Всё хранится в памяти, базы данных нет.

## Авторизация
Единый токен. Передаётся одним из способов:
`Authorization: Bearer <токен>`, `X-Api-Key: <токен>` или `?token=<токен>` (для плеера и скачивания).
Токен лежит в `appsettings.json`; если там не задан — используется константа `AuthOptions.DefaultToken` в `Options.cs`.
Сервер работает по обычному HTTP — для доступа из интернета поставьте перед ним HTTPS (nginx, Caddy и т. п.).

## API
Все пути в запросах и ответах — относительные от корня музыкальной папки, разделитель `/`.

| Запрос | Описание |
|---|---|
| `GET /api/ping` | проверка соединения и токена, сведения о библиотеке |
| `GET /api/browse?path=Rock/Queen` | **одна папка**: подпапки (`folders`) и треки (`tracks`), лежащие прямо в ней. Пустой `path` — корень. Папки без музыки не показываются |
| `GET /api/tracks?path=Rock/Queen` | все треки папки с вложенными (в порядке отображения) — для «играть папку» и скачивания |
| `GET /api/search?q=queen greatest` | поиск по папкам и трекам (несколько слов — все должны встретиться, ё = е) |
| `GET /api/stream?path=Rock/Queen/01.mp3` | прослушивание, поддерживает Range (перемотка) |
| `GET /api/download?path=Rock/Queen/01.mp3` | скачивание файла (`Content-Disposition: attachment`) |
| `POST /api/rescan` | принудительно пересканировать папку |

Пример: `curl -H "Authorization: Bearer sunset-change-me" "http://localhost:5080/api/browse?path="`
