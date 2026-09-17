# Release packaging script (Inno Setup)
# Flow: run build.ps1 -> copy output to ./tmp/<version> -> compile installer via ISCC -> move final exe to ./release -> cleanup tmp
# If ISCC is unavailable, fall back to producing a portable .zip in ./release (unless -NoZipFallback).
#
# 只负责打包，不打开资源管理器。
# 「打包完打开产物目录并选中安装包」由上层作业脚本负责：job-pack.ps1

param(
    [string]$BuildScript = "./build.ps1",
    [string]$ISCCPath = "",
    [string]$Version = "V0.2",
    [switch]$NoZipFallback
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

# 调用构建脚本（build.ps1 自身不打开窗口，打开由上层作业脚本负责）
Write-Host "Running build script: $buildScriptPath -Version $Version"
& powershell -NoProfile -ExecutionPolicy Bypass -File $buildScriptPath -Version $Version
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
    # 常见安装位置：系统级安装、以及 winget 的用户级安装（%LOCALAPPDATA%\Programs）
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    $iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

if (-not $iscc) {
    Write-Host "ISCC.exe not found. Falling back to portable zip packaging." -ForegroundColor Yellow

    if ($NoZipFallback) {
        Write-Host "Zip fallback disabled (-NoZipFallback). Cleaning temp and exiting." -ForegroundColor Yellow
        if (Test-Path $tmpDir) { Remove-Item -Path $tmpDir -Recurse -Force }
        exit 1
    }

    $zipPath = Join-Path $releaseRoot ("WinLoop_$versionName" + "_portable.zip")
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

    Write-Host "Creating portable zip: $zipPath"
    Compress-Archive -Path (Join-Path $tmpDir "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force

    if (-not (Test-Path -LiteralPath $zipPath)) {
        Write-Host "Failed to create portable zip. Cleaning temp and exiting." -ForegroundColor Red
        if (Test-Path $tmpDir) { Remove-Item -Path $tmpDir -Recurse -Force }
        exit 1
    }

    if (Test-Path $tmpDir) { Remove-Item -Path $tmpDir -Recurse -Force }

    Write-Host "Portable package created: $zipPath" -ForegroundColor Green
    Write-Host "Tip: install Inno Setup to also produce a .exe installer." -ForegroundColor Yellow
    exit 0
}

Write-Host "Found ISCC: $iscc"

# 生成 Inno 脚本。放在独立的 iss 目录，避免被打进安装包
$issDir = Join-Path -Path $tmpRoot ("iss_" + $versionName)
if (Test-Path $issDir) { Remove-Item -Path $issDir -Recurse -Force }
New-Item -Path $issDir -ItemType Directory | Out-Null

$issPath = Join-Path $issDir ("WinLoop_$versionName.iss")
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
if (Test-Path $issDir) {
    Remove-Item -Path $issDir -Recurse -Force
    Write-Host "Cleaned iss directory: $issDir" -ForegroundColor Green
}

Write-Host "Release packaging complete: $destExe"
