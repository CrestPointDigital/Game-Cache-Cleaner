param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

Write-Host "==> Publishing self-contained single-file EXE..." -ForegroundColor Cyan
dotnet publish "$PSScriptRoot\GameCacheCleaner.UI\GameCacheCleaner.UI.csproj" `
  -c $Configuration -r $Runtime `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:SelfContained=true `
  --self-contained true

$pubDir = Join-Path "$PSScriptRoot\GameCacheCleaner.UI\bin" "$Configuration\net8.0-windows\$Runtime\publish"
$exe = Join-Path $pubDir "GameCacheCleaner.UI.exe"
if (!(Test-Path $exe)) { throw "EXE not found at $exe" }

$zip = Join-Path $PSScriptRoot "GameCacheCleaner_UI_Release.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host "==> Zipping publish output to $zip" -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($pubDir, $zip)

Write-Host "==> Done. Output:" -ForegroundColor Green
Write-Host "    EXE : $exe"
Write-Host "    ZIP : $zip"
