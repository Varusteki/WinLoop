<#
    WinLoop 作业脚本 ②  ——  打包（生成安装包）
    ---------------------------------------------------------------
    用途：本地测试通过、要正式发布时使用。
    流程：调用 release.ps1 完成「构建 -> 生成 Inno Setup 脚本 -> ISCC 编译 -> 输出到 release\」
          -> 定位安装包 -> 打开资源管理器并选中它（打包完成必定打开，无需也无法跳过）。

    用法：
        .\job-pack.ps1                 打包 + 打开产物目录
        .\job-pack.ps1 -Version V0.3   覆盖版本前缀（默认 V0.2）
        .\job-pack.ps1 -NoZipFallback  找不到 ISCC 时直接失败（默认会降级出免安装 zip）

    产物：.\release\WinLoop_<版本>.exe（找不到 Inno Setup 时降级为 .zip）
#>

param(
    [string]$Version = "V0.2",
    [switch]$NoZipFallback
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$baseDir = $PSScriptRoot
if (-not $baseDir) { $baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $baseDir) { $baseDir = (Get-Location).ProviderPath }
$baseDir = (Resolve-Path -LiteralPath $baseDir).Path
Set-Location -LiteralPath $baseDir

$sw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host '========================================'
Write-Host ' WinLoop 作业：打包（安装包）'
Write-Host '========================================'

# ---------- 1. 构建 + 编译安装包（用独立进程调用） ----------
$releaseScript = Join-Path $baseDir 'release.ps1'
if (-not (Test-Path -LiteralPath $releaseScript)) {
    Write-Host "找不到 release.ps1: $releaseScript" -ForegroundColor Red
    exit 1
}

# 打开动作由本脚本统一负责（保证走置顶工具）；release.ps1 自身不打开窗口
$psArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $releaseScript, '-Version', $Version)
if ($NoZipFallback) { $psArgs += '-NoZipFallback' }

& powershell @psArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host '打包失败，终止。' -ForegroundColor Red
    exit 1
}

# ---------- 2. 定位最新安装包 ----------
$releaseRoot = Join-Path $baseDir 'release'
$artifact = Get-ChildItem -Path $releaseRoot -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -eq '.exe' -or $_.Extension -eq '.zip' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $artifact) {
    Write-Host '未在 .\release 下找到安装包。' -ForegroundColor Red
    exit 1
}

$sizeMB = [math]::Round($artifact.Length / 1MB, 2)
$sw.Stop()

Write-Host ''
Write-Host '打包完成' -ForegroundColor Green
Write-Host "  安装包: $($artifact.FullName)"
Write-Host "  大小  : $sizeMB MB"
Write-Host "  耗时  : $([math]::Round($sw.Elapsed.TotalSeconds, 1)) 秒"

# ---------- 3. 打开产物目录（选中安装包）----------
# -Group 传入 release 根目录：若该目录已在资源管理器中打开，会直接复用窗口而不是新开
Write-Host ''
& (Join-Path $baseDir 'open-artifact.ps1') -Path $artifact.FullName -Group $releaseRoot

exit 0
