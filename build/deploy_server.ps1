<#
.SYNOPSIS
    Собирает SunsetMP.Server и выкладывает его на Linux-сервер по SSH (с установкой/перезапуском systemd-сервиса).

.DESCRIPTION
    1. dotnet publish для Linux.
    2. Упаковывает результат в tar.gz.
    3. Копирует архив и install-linux.sh на сервер (scp).
    4. Запускает на сервере install-linux.sh под sudo: файлы заменяются, сервис перезапускается.

    Нужны: .NET SDK 10, клиент OpenSSH (ssh, scp) и tar — всё это есть в Windows 10/11.
    На сервере — пользователь с правом sudo. Пароль sudo спросят один раз.
    Настройки (музыка, токен, порт) лежат на сервере в /etc/sunsetmp-server.env и при обновлении не затираются.

.EXAMPLE
    # первая установка (токен сгенерируется на сервере и будет показан)
    .\Deploy-Server.ps1 -Server 192.168.1.20 -User pi -MusicPath /srv/music

.EXAMPLE
    # обновление после изменений в коде
    .\Deploy-Server.ps1 -Server 192.168.1.20 -User pi

.EXAMPLE
    # Raspberry Pi 64-bit, без установленного на сервере .NET, вход по ключу
    .\Deploy-Server.ps1 -Server nas.local -User admin -KeyFile ~\.ssh\id_ed25519 -Runtime linux-arm64 -SelfContained
#>
[CmdletBinding()]
param(
    # Адрес сервера (имя или IP)
    [Parameter(Mandatory)][string]$Server,

    # Пользователь SSH
    [string]$User = $env:USERNAME,

    [int]$Port = 22,

    # Приватный ключ SSH (если не указан — стандартные ключи или пароль)
    [string]$KeyFile,

    # Куда ставить сервер на Linux
    [string]$RemoteDir = '/opt/sunsetmp-server',

    # linux-x64 | linux-arm64 | linux-arm (Raspberry Pi 32-bit)
    [string]$Runtime = 'linux-x64',

    # Положить .NET внутрь пакета — на сервере не нужен установленный runtime (пакет станет больше)
    [switch]$SelfContained,

    # Только для первой установки (или чтобы изменить): папка с музыкой НА СЕРВЕРЕ
    [string]$MusicPath,

    # Только для первой установки (или чтобы изменить): токен. Не указывайте — сгенерируется на сервере
    [string]$Token,

    # Порт сервера (по умолчанию 5080)
    [int]$ServerPort = 0,

    # Папка с SunsetMP.Server.csproj
    [string]$ProjectDir = "$PSScriptRoot/../src/Server",

    # Не собирать заново, использовать результат прошлой публикации
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Команда завершилась с ошибкой ($LASTEXITCODE): $Exe $($Arguments -join ' ')" }
}

function ConvertTo-ShSingleQuoted([string]$Value) {
    # безопасная подстановка строки в bash: 'abc' (одиночная кавычка внутри → '\'' )
    "'" + $Value.Replace("'", "'\''") + "'"
}

# ---------- проверки ----------
foreach ($tool in 'dotnet', 'ssh', 'scp', 'tar') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Не найдена команда '$tool'. Установите .NET SDK и компонент 'Клиент OpenSSH' (Параметры → Приложения → Дополнительные компоненты)."
    }
}
if ($RemoteDir -notmatch '^/[^/]+/[^/]+') { throw "RemoteDir должен быть вида /opt/sunsetmp-server, а не '$RemoteDir'." }

$ProjectDir = (Resolve-Path $ProjectDir).Path
$csproj = Join-Path $ProjectDir 'SunsetMP.Server.csproj'
if (-not (Test-Path $csproj)) { throw "Не найден $csproj. Укажите -ProjectDir." }

$installScript = Join-Path $PSScriptRoot 'install-linux.sh'
if (-not (Test-Path $installScript)) { throw "Рядом со скриптом нет install-linux.sh." }

$work = Join-Path ([IO.Path]::GetTempPath()) 'sunsetmp-deploy'
$publishDir = Join-Path $work 'publish'
$archive = Join-Path $work 'sunsetmp-server.tar.gz'
$remoteScript = Join-Path $work 'sunsetmp-install.sh'
New-Item -ItemType Directory -Force -Path $work | Out-Null

# ---------- 1. сборка ----------
if (-not $SkipBuild -or -not (Test-Path $publishDir)) {
    Write-Host "==> Сборка ($Runtime, self-contained: $($SelfContained.IsPresent))" -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    $sc = if ($SelfContained) { 'true' } else { 'false' }
    Invoke-Native dotnet @('publish', $csproj, '-c', 'Release', '-r', $Runtime, '--self-contained', $sc,
                           '-o', $publishDir, '-p:DebugType=None', '-p:DebugSymbols=false', '--nologo')
}

# ---------- 2. архив ----------
Write-Host '==> Упаковка' -ForegroundColor Cyan
if (Test-Path $archive) { Remove-Item $archive -Force }
Invoke-Native tar @('-czf', $archive, '-C', $publishDir, '.')

# install-linux.sh должен быть с переводами строк Unix (LF), иначе bash его не поймёт
$text = [IO.File]::ReadAllText($installScript).Replace("`r`n", "`n")
[IO.File]::WriteAllText($remoteScript, $text, (New-Object Text.UTF8Encoding($false)))

# ---------- 3. загрузка ----------
$target = "$User@$Server"
$sshCommon = @('-o', 'StrictHostKeyChecking=accept-new')
if ($KeyFile) { $sshCommon += @('-i', (Resolve-Path $KeyFile).Path) }
$sshArgs = @('-p', $Port) + $sshCommon
$scpArgs = @('-P', $Port) + $sshCommon

Write-Host "==> Загрузка на $target" -ForegroundColor Cyan
Invoke-Native scp ($scpArgs + @($archive, "${target}:/tmp/sunsetmp-server.tar.gz"))
Invoke-Native scp ($scpArgs + @($remoteScript, "${target}:/tmp/sunsetmp-install.sh"))

# ---------- 4. установка на сервере ----------
$remoteArgs = @('--archive', '/tmp/sunsetmp-server.tar.gz', '--dir', (ConvertTo-ShSingleQuoted $RemoteDir))
if ($MusicPath)   { $remoteArgs += @('--music', (ConvertTo-ShSingleQuoted $MusicPath)) }
if ($Token)       { $remoteArgs += @('--token', (ConvertTo-ShSingleQuoted $Token)) }
if ($ServerPort)  { $remoteArgs += @('--port', $ServerPort) }

# -t нужен, чтобы sudo мог спросить пароль
$remoteCommand = "sudo bash /tmp/sunsetmp-install.sh $($remoteArgs -join ' '); code=`$?; rm -f /tmp/sunsetmp-install.sh /tmp/sunsetmp-server.tar.gz; exit `$code"

Write-Host '==> Установка на сервере (может потребоваться пароль sudo)' -ForegroundColor Cyan
Invoke-Native ssh (@('-t') + $sshArgs + @($target, $remoteCommand))

Write-Host 'Готово.' -ForegroundColor Green
