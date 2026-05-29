# Build CopyPaste Pro as a single-file Windows executable
$ErrorActionPreference = "Stop"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$project = Join-Path $PSScriptRoot "CopyPastePro\CopyPastePro.csproj"
$outDir = Join-Path $PSScriptRoot "dist"

if (-not (Test-Path $dotnet)) {
    Write-Error ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
}

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $outDir

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Build complete:" -ForegroundColor Green
    Write-Host "  $outDir\CopyPastePro.exe"
}
