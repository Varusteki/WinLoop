# WinLoop build script
# Output: ./build/<version>
# -Version: override version prefix (default V0.2)
#
# 只负责构建 + 发布，不打开资源管理器。
# 「构建完打开产物目录并选中 exe」由上层作业脚本负责：job-build.ps1

param(
    [string]$Version = "V0.2"
)

$ErrorActionPreference = 'Stop'

# Keep console output stable
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# Always run from script directory (prefer $PSCommandPath)
$scriptPath = $PSCommandPath
if (-not $scriptPath) { $scriptPath = $MyInvocation.MyCommand.Path }
if (-not $scriptPath) { throw 'Cannot resolve build.ps1 script path (PSCommandPath/MyInvocation empty)' }

$scriptDir = Split-Path -Parent $scriptPath
if (-not $scriptDir) { $scriptDir = $PSScriptRoot }
if (-not $scriptDir) { $scriptDir = (Get-Location).ProviderPath }
$scriptDir = (Resolve-Path -LiteralPath $scriptDir).Path
Set-Location -LiteralPath $scriptDir

# Base directory for all relative paths
$baseDir = $scriptDir

if (-not $baseDir) { throw 'baseDir is empty; cannot locate repo root' }
$baseDir = (Resolve-Path -LiteralPath $baseDir).Path

$currentDate = Get-Date -Format 'yyyyMMddHHmm'
$version = "$Version-$currentDate"
Write-Host "Building WinLoop $version..."

# Ensure build directory exists (keep history)
$buildRoot = Join-Path -Path $baseDir -ChildPath 'build'
if (-not $buildRoot) { throw 'buildRoot is empty; Join-Path failed' }
if (-not (Test-Path -LiteralPath $buildRoot)) {
    try {
        New-Item -Path $buildRoot -ItemType Directory | Out-Null
        Write-Host "Created build directory"
    } catch {
        Write-Host "Failed to create build directory: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "Keeping existing ./build directory (history preserved)."
}

# Copy tray icon if source exists
$winloopRes = Join-Path -Path $baseDir -ChildPath 'WinLoop\Resources'
if (-not $winloopRes) { throw 'Cannot resolve WinLoop\\Resources path' }
if (-not (Test-Path -LiteralPath $winloopRes)) {
    New-Item -Path $winloopRes -ItemType Directory | Out-Null
}
$sourceIcon = Join-Path -Path $baseDir -ChildPath 'WGestures\WGestures.App\Resources\trayIcon.ico'
$destIcon = Join-Path -Path $winloopRes -ChildPath 'trayIcon.ico'
if (Test-Path $sourceIcon) {
    try {
        Copy-Item -Path $sourceIcon -Destination $destIcon -Force -ErrorAction Stop
        Write-Host 'Copied trayIcon.ico to WinLoop Resources'
    } catch {
        Write-Host "Failed to copy trayIcon.ico: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "Source tray icon not found at $sourceIcon; skipping copy" -ForegroundColor Yellow
}

# Build + publish
Write-Host 'Building project...'
Push-Location (Join-Path -Path $scriptDir -ChildPath 'WinLoop')
& dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build failed!' -ForegroundColor Red
    Pop-Location
    exit 1
}

Write-Host 'Publishing project...'
& dotnet publish -c Release -r win-x64 --self-contained false -o (Join-Path -Path $scriptDir -ChildPath "build/$version")
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Publish failed!' -ForegroundColor Red
    Pop-Location
    exit 1
}
Pop-Location

Write-Host 'Build succeeded!' -ForegroundColor Green
Write-Host "Output: ./build/$version"
