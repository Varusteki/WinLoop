<#
    WinLoop 作业脚本 ①  ——  构建（只出可执行文件，不生成安装包）
    ---------------------------------------------------------------
    用途：日常迭代验证。产出可直接双击运行的 WinLoop.exe。
    流程：调用 build.ps1 完成 build + publish -> 定位产物 -> 更新稳定入口 build\latest
          -> 打开资源管理器并选中 exe（构建完成必定打开，无需也无法跳过）。

    用法：
        .\job-build.ps1                 构建 + 打开产物目录
        .\job-build.ps1 -Version V0.3   覆盖版本前缀（默认 V0.2）
        .\job-build.ps1 -NoClean        跳过长 bin/obj 清理（快速增量构建）

    产物：.\build\<版本>\WinLoop.exe    稳定入口：.\build\latest\WinLoop.exe

    注：默认会先清 WinLoop\bin 与 obj（原因见 build.ps1 文件头：
        WPF 增量状态里的陈旧资源清单会导致重复嵌入、产物静默膨胀）。
        所以**不需要再手工删缓存**。
#>

param(
    [string]$Version = "V0.2",
    [switch]$NoClean
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
Write-Host ' WinLoop 作业：构建（仅 exe）'
Write-Host '========================================'

# ---------- 1. 构建（用独立进程调用，避免 exit 互相打断） ----------
$buildScript = Join-Path $baseDir 'build.ps1'
if (-not (Test-Path -LiteralPath $buildScript)) {
    Write-Host "找不到 build.ps1: $buildScript" -ForegroundColor Red
    exit 1
}

# 打开动作由本脚本统一负责（保证走置顶工具）；build.ps1 自身不打开窗口
$psArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $buildScript, '-Version', $Version)
if ($NoClean) { $psArgs += '-NoClean' }
& powershell @psArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host '构建失败，终止。' -ForegroundColor Red
    exit 1
}

# ---------- 2. 定位最新产物 ----------
$buildRoot = Join-Path $baseDir 'build'
$latest = Get-ChildItem -Path $buildRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'WinLoop.exe') } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $latest) {
    Write-Host '未在 .\build 下找到可用产物。' -ForegroundColor Red
    exit 1
}

$exePath = Join-Path $latest.FullName 'WinLoop.exe'
$sizeMB = [math]::Round((Get-Item -LiteralPath $exePath).Length / 1MB, 2)
$builtAt = (Get-Item -LiteralPath $latest.FullName).LastWriteTime.ToString('HH:mm:ss')

# ---------- 3. 维护 build\latest 稳定入口 ----------
$linkPath = Join-Path $buildRoot 'latest'
$linkOk = $false
try {
    if (Test-Path -LiteralPath $linkPath) {
        $item = Get-Item -LiteralPath $linkPath -Force
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            # 目录联接：只删链接，绝不动真实目录
            [System.IO.Directory]::Delete($linkPath, $false)
        }
        else {
            # 历史遗留的真实目录副本（旧脚本复制出来的）：能删就删
            Remove-Item -LiteralPath $linkPath -Recurse -Force -ErrorAction Stop
        }
    }

    # 目录删除可能异步落盘，创建联接时重试几次，避免竞态
    for ($i = 0; $i -lt 10 -and -not $linkOk; $i++) {
        try {
            New-Item -ItemType Junction -Path $linkPath -Target $latest.FullName -ErrorAction Stop | Out-Null
            $linkOk = $true
        }
        catch {
            Start-Sleep -Milliseconds 300
        }
    }

    if ($linkOk) {
        Write-Host "已更新稳定入口: build\latest -> $($latest.Name)"
    }
    else {
        Write-Host '提示：build\latest 未能更新（可能被占用），不影响本次构建。' -ForegroundColor Yellow
    }
}
catch {
    Write-Host '提示：build\latest 未能更新（可能被占用），不影响本次构建。' -ForegroundColor Yellow
}

$sw.Stop()

Write-Host ''
Write-Host '构建完成' -ForegroundColor Green
Write-Host "  版本    : $($latest.Name)"
Write-Host "  目录    : $($latest.FullName)"
Write-Host "  可执行  : $exePath  ($sizeMB MB, 构建于 $builtAt)"
Write-Host "  稳定路径: build\latest\WinLoop.exe"
Write-Host "  耗时    : $([math]::Round($sw.Elapsed.TotalSeconds, 1)) 秒"

# ---------- 4. 打开产物目录（选中 exe）----------
# -Group 传入 build 根目录：若已有窗口停在该目录树里，会直接复用并导航过去，不再新开窗口
Write-Host ''
& (Join-Path $baseDir 'open-artifact.ps1') -Path $exePath -Group $buildRoot

exit 0
