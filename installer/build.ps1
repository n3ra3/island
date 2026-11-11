# Собирает установщик: публикует программу одним файлом и упаковывает её в DynamicIsland-Setup-<версия>.exe
# Запуск из папки проекта:  powershell -ExecutionPolicy Bypass -File installer\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Не найден Inno Setup. Установи: winget install JRSoftware.InnoSetup' }

Write-Host '1/2 Собираю программу...'
Remove-Item "$root\dist\app" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\DynamicIsland.csproj" -c Release -o "$root\dist\app" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Сборка программы не удалась' }

Write-Host '2/2 Собираю установщик...'
$version = ([xml](Get-Content "$root\DynamicIsland.csproj" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
& $iscc /Q "/DAppVersion=$version" "$PSScriptRoot\DynamicIsland.iss"
if ($LASTEXITCODE -ne 0) { throw 'Сборка установщика не удалась' }

Get-ChildItem "$root\dist\*.exe" | ForEach-Object { Write-Host ("Готово: {0} ({1:N0} МБ)" -f $_.FullName, ($_.Length / 1MB)) }
