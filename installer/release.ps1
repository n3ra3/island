# Выпуск новой версии: меняет номер версии, собирает установщик и выкладывает его в GitHub Releases.
# У всех, у кого стоит островок, появится уведомление «Доступна версия …».
#
# Пример:  powershell -ExecutionPolicy Bypass -File installer\release.ps1 -Version 1.1.0 -Notes "Новые глазки"
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = ""
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repo = 'n3ra3/island'

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Версия должна быть вида 1.2.3' }

# 1. Номер версии в проекте — из него его берут и островок, и установщик
$csproj = "$root\DynamicIsland.csproj"
$text = [IO.File]::ReadAllText($csproj)
$text = [regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($csproj, $text)
Write-Host "Версия: $Version"

# 2. Сборка
& "$PSScriptRoot\build.ps1"
$setup = "$root\dist\DynamicIsland-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "Не найден $setup" }

# 3. Выкладываем на GitHub
if (Get-Command gh -ErrorAction SilentlyContinue) {
    if (-not $Notes) { $Notes = "Dynamic Island $Version" }
    gh release create "v$Version" $setup --repo $repo --title "Dynamic Island $Version" --notes $Notes
    if ($LASTEXITCODE -ne 0) { throw 'Не получилось создать релиз на GitHub' }
    Write-Host "Готово: https://github.com/$repo/releases/tag/v$Version"
} else {
    Write-Host ''
    Write-Host 'GitHub CLI не установлен — выложи вручную:'
    Write-Host "  1. Открой https://github.com/$repo/releases/new"
    Write-Host "  2. Тег: v$Version, заголовок: Dynamic Island $Version"
    Write-Host "  3. Перетащи файл: $setup"
    Write-Host '  4. Нажми «Publish release»'
    Start-Process "https://github.com/$repo/releases/new?tag=v$Version&title=Dynamic%20Island%20$Version"
}
