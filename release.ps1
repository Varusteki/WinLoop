# Release packaging script (Inno Setup)
# Flow: run build.ps1 -> copy output to ./tmp/<version> -> compile installer via ISCC -> move final exe to ./release -> cleanup tmp

param(
    [string]$BuildScript = "./build.ps1",
    [string]$ISCCPath = ""
)

$ErrorActionPreference = "Stop"

# 让中文输出在大多数终端更稳定（不影响逻辑）
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# 始终以脚本所在目录为工作目录，避免 Get-Location 在某些宿主下异常
$scriptPath = $PSCommandPath
if (-not $scriptPath) { $scriptPath = $MyInvocation.MyCommand.Path }
if ($scriptPath) {
    $scriptDir = Split-Path -Parent $scriptPath
    if ($scriptDir) { Set-Location -LiteralPath (Resolve-Path -LiteralPath $scriptDir).Path }
}

$baseDir = (Get-Location).ProviderPath

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

        # Restore (9) and bring to foreground.
        [WinLoopNative.User32]::ShowWindowAsync($hWnd, 9) | Out-Null
        [WinLoopNative.User32]::SetForegroundWindow($hWnd) | Out-Null
    }
    catch { }
}

function Get-ExplorerWindowForFolder([string]$folderPath)
{
    try
    {
        if (-not $folderPath) { return $null }
        if (-not (Test-Path -LiteralPath $folderPath)) { return $null }

        $target = (Resolve-Path -LiteralPath $folderPath).Path.TrimEnd('\\')
        $shell = New-Object -ComObject Shell.Application

        foreach ($w in @($shell.Windows()))
        {
            try
            {
                if (-not $w) { continue }
                $fullName = [string]$w.FullName
                if (-not $fullName) { continue }
                if ([System.IO.Path]::GetFileName($fullName) -ne 'explorer.exe') { continue }

                $url = [string]$w.LocationURL
                if (-not $url) { continue }

                $localPath = ([uri]$url).LocalPath
                if (-not $localPath) { continue }
                $localPath = [System.IO.Path]::GetFullPath($localPath).TrimEnd('\\')

                if ($localPath -ieq $target) { return $w }
            }
            catch { }
        }
    }
    catch { }

    return $null
}

function Close-ExplorerWindowsForFolder([string]$folderPath)
{
    try
    {
        if (-not $folderPath) { return }
        if (-not (Test-Path -LiteralPath $folderPath)) { return }

        $target = (Resolve-Path -LiteralPath $folderPath).Path.TrimEnd('\\')
        $shell = New-Object -ComObject Shell.Application

        foreach ($w in @($shell.Windows()))
        {
            try
            {
                if (-not $w) { continue }
                $fullName = [string]$w.FullName
                if (-not $fullName) { continue }
                if ([System.IO.Path]::GetFileName($fullName) -ne 'explorer.exe') { continue }

                $url = [string]$w.LocationURL
                if (-not $url) { continue }

                $localPath = ([uri]$url).LocalPath
                if (-not $localPath) { continue }
                $localPath = [System.IO.Path]::GetFullPath($localPath).TrimEnd('\\')

                if ($localPath -ieq $target)
                {
                    # Close the Explorer window.
                    $w.Quit()
                }
            }
            catch { }
        }
    }
    catch { }
}

function Show-ExplorerAndSelectFile([string]$filePath)
{
    try
    {
        if (-not $filePath) { return }
        if (-not (Test-Path -LiteralPath $filePath)) { return }

        $resolvedFile = (Resolve-Path -LiteralPath $filePath).Path
        $folder = Split-Path -Parent $resolvedFile

        # Simple + reliable: close any already-open Explorer windows at target folder,
        # then open a fresh one selecting the new artifact.
        Close-ExplorerWindowsForFolder -folderPath $folder
        Start-Process explorer.exe -ArgumentList "/select,`"$resolvedFile`""
    }
    catch { }
}

Write-Host "Starting release packaging..."

# 检查构建脚本（支持相对路径：相对于 release.ps1 所在目录）
$buildScriptPath = $BuildScript
if (-not [System.IO.Path]::IsPathRooted($buildScriptPath)) {
    $buildScriptPath = Join-Path -Path $baseDir -ChildPath $buildScriptPath
}

if (-not (Test-Path -LiteralPath $buildScriptPath)) {
    Write-Host "Build script not found: $buildScriptPath" -ForegroundColor Red
    exit 1
}

# 调用构建脚本（传入 -NoOpen 禁止自动打开 Explorer）
Write-Host "Running build script: $buildScriptPath -NoOpen"
& powershell -NoProfile -ExecutionPolicy Bypass -File $buildScriptPath -NoOpen
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build script failed, aborting packaging." -ForegroundColor Red
    exit 1
}

# 找到 build 目录下最新的版本文件夹
$buildRoot = Join-Path -Path $baseDir -ChildPath "build"
$latest = Get-ChildItem -Path $buildRoot -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $latest) {
    Write-Host "No build output directory found under ./build. Aborting." -ForegroundColor Red
    exit 1
}

$versionName = $latest.Name
$buildDir = $latest.FullName
Write-Host "Latest build: $versionName -> $buildDir"

# 创建临时目录用于打包过程
$tmpRoot = Join-Path -Path $baseDir -ChildPath "tmp"
$tmpDir = Join-Path $tmpRoot $versionName
if (Test-Path $tmpDir) { Remove-Item -Path $tmpDir -Recurse -Force }
New-Item -Path $tmpDir -ItemType Directory | Out-Null

Write-Host "Copying build output to temp dir: $tmpDir ..."
Copy-Item -Path (Join-Path $buildDir "*") -Destination $tmpDir -Recurse -Force

# 确保 release 目录存在（最终 exe 放这个目录）
$releaseRoot = Join-Path -Path $baseDir -ChildPath "release"
if (-not (Test-Path $releaseRoot)) { New-Item -Path $releaseRoot -ItemType Directory | Out-Null }

# 查找 ISCC
$iscc = $null
if ($ISCCPath -and (Test-Path $ISCCPath)) {
    $iscc = $ISCCPath
}
else {
    $candidates = @("C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe", "C:\\Program Files\\Inno Setup 6\\ISCC.exe")
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $iscc) {
    Write-Host "ISCC.exe not found. Install Inno Setup or pass -ISCCPath. Cleaning temp and exiting." -ForegroundColor Yellow
    if (Test-Path $tmpDir) { Remove-Item -Path $tmpDir -Recurse -Force }
    exit 1
}

Write-Host "Found ISCC: $iscc"

# 生成 Inno 脚本放在 tmp 目录
$issPath = Join-Path $tmpDir ("WinLoop_$versionName.iss")
$issContent = @"
[Setup]
AppName=WinLoop
AppVersion=$versionName
DefaultDirName={autopf}\\WinLoop
DefaultGroupName=WinLoop
OutputBaseFilename=WinLoop_$versionName
Compression=lzma
SolidCompression=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "$tmpDir\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\\WinLoop"; Filename: "{app}\\WinLoop.exe"
Name: "{autodesktop}\\WinLoop"; Filename: "{app}\\WinLoop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\\WinLoop.exe"; Description: "Run WinLoop"; Flags: nowait postinstall skipifsilent
"@

Set-Content -Path $issPath -Value $issContent -Encoding UTF8
Write-Host "Generated Inno script: $issPath"

# 使用 ISCC 编译，输出到 tmpDir
Write-Host "Compiling installer via ISCC..."
& "$iscc" "/O$tmpDir" "$issPath"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ISCC compilation failed (exit $LASTEXITCODE). Cleaning temp and exiting." -ForegroundColor Red
    if (Test-Path $tmpDir) { Remove-Item -Path $tmpDir -Recurse -Force }
    exit 1
}

# 在 tmpDir 中寻找生成的 installer，并移动到 ./release 根目录
$installer = Get-ChildItem -Path $tmpDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $installer) {
    Write-Host "No installer exe produced in temp directory. Cleaning temp and exiting." -ForegroundColor Red
    if (Test-Path $tmpDir) { Remove-Item -Path $tmpDir -Recurse -Force }
    exit 1
}

$destExe = Join-Path $releaseRoot $installer.Name
Move-Item -Path $installer.FullName -Destination $destExe -Force
Write-Host "Installer moved to: $destExe" -ForegroundColor Green

# 删除临时目录（过程文件）
if (Test-Path $tmpDir) {
    Remove-Item -Path $tmpDir -Recurse -Force
    Write-Host "Cleaned temp directory: $tmpDir" -ForegroundColor Green
}

# 在资源管理器中选中最终 exe
Show-ExplorerAndSelectFile -filePath $destExe

Write-Host "Release packaging complete: $destExe"
