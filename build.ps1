# WinLoop build script
# Output: ./build/<version>
# -NoOpen: do not open Explorer

param(
    [switch]$NoOpen
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

function Ensure-ForegroundWindow([IntPtr]$hWnd)
{
    try
    {
        if ($hWnd -eq [IntPtr]::Zero) { return }

        if (-not ([System.Management.Automation.PSTypeName]'WinLoopNative.User32').Type)
        {
            Add-Type -Namespace WinLoopNative -Name User32 -MemberDefinition @"
using System;
using System.Runtime.InteropServices;

public static class User32
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
}
"@
        }

        [WinLoopNative.User32]::ShowWindowAsync($hWnd, 9) | Out-Null
        [WinLoopNative.User32]::SetForegroundWindow($hWnd) | Out-Null
    }
    catch { }
}

if (-not $baseDir) { throw 'baseDir is empty; cannot locate repo root' }
$baseDir = (Resolve-Path -LiteralPath $baseDir).Path

$currentDate = Get-Date -Format 'yyyyMMddHHmm'
$version = "V0.1-$currentDate"
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
& dotnet publish -c Release -o (Join-Path -Path $scriptDir -ChildPath "build/$version")
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Publish failed!' -ForegroundColor Red
    Pop-Location
    exit 1
}
Pop-Location

Write-Host 'Build succeeded!' -ForegroundColor Green
Write-Host "Output: ./build/$version"

# Optionally open output directory
$buildOutputPath = Join-Path $scriptDir "build/$version"
if (-not $NoOpen) {
    try {
        if (Test-Path -LiteralPath $buildOutputPath) {
            $target = (Resolve-Path -LiteralPath $buildOutputPath).Path.TrimEnd('\\')
            $shell = New-Object -ComObject Shell.Application
            $existing = $null
            foreach ($w in @($shell.Windows())) {
                try {
                    if (-not $w) { continue }
                    $fullName = [string]$w.FullName
                    if (-not $fullName) { continue }
                    if ([System.IO.Path]::GetFileName($fullName) -ne 'explorer.exe') { continue }
                    $url = [string]$w.LocationURL
                    if (-not $url) { continue }
                    $localPath = ([uri]$url).LocalPath
                    if (-not $localPath) { continue }
                    $localPath = [System.IO.Path]::GetFullPath($localPath).TrimEnd('\\')
                    if ($localPath -ieq $target) { $existing = $w; break }
                } catch { }
            }

            if ($existing) {
                try { $existing.Visible = $true } catch { }
                try { Ensure-ForegroundWindow ([IntPtr]$existing.HWND) } catch { }
            }
            else {
                Start-Process explorer.exe -ArgumentList $buildOutputPath
            }
        }
    } catch {
        Start-Process explorer.exe -ArgumentList $buildOutputPath
    }
}
