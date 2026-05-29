# Fast dev loop — runs from source (no full publish). Rebuilds only when code changed.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
Write-Host "Starting CopyPaste Pro (dev)..." -ForegroundColor Cyan
Write-Host "Tip: edit code, stop (Ctrl+C), run this script again." -ForegroundColor DarkGray
dotnet run --project CopyPastePro\CopyPastePro.csproj
