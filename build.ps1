# WinLoop build script
# Output: ./build/<version>
# -Version: override version prefix (default V0.2)
# -NoClean: skip the bin/obj cleanup (incremental build, faster but risky — see below)
#
# 只负责构建 + 发布，不打开资源管理器。
# 「构建完打开产物目录并选中 exe」由上层作业脚本负责：job-build.ps1
#
# ==================== 为什么每次都要清 bin/obj ====================
#
# 不是洁癖，是防一个**静默错误**：产物悄悄变大 / 内容不对，但编译 0 报错。
#
# 症状：dll 从 17MB 膨胀到 34MB —— 正好两倍，即两个字体被嵌了两遍。
# 原因：WPF 的增量构建在 obj\ 里缓存「该嵌哪些资源」的清单。
#       当你改动过资源清单（增删/改名 <Resource> 文件、调整通配符、
#       换掉被嵌入的 ico），**旧条目不会自动作废**，新的又叠加上去 → 重复嵌入。
#
# ⚠️ 关键：**`dotnet clean` 解决不了这个问题** ——
#    它只删「当前项目评估认为它产出过」的文件，而陈旧条目恰恰不在当前清单里。
#    唯一可靠做法是直接删掉 obj（增量状态）与 bin（输出）。
#
# 代价是一次完整重编译（约 10~20 秒）。比起"发出去的包里字体翻倍"，
# 这个代价可以接受，所以**默认清理**。
# 只有明确知道本次没动资源清单、想快速迭代时，才用 -NoClean 跳过。

param(
    [string]$Version = "V0.2",
    [switch]$NoClean
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

# ---------- 清理增量状态（见文件头说明；默认执行） ----------
if (-not $NoClean) {
    Write-Host 'Cleaning bin/obj (removes stale incremental resource state)...'

    # 先停掉常驻的编译器服务（VBCSCompiler / MSBuild 节点）——
    # 它会持有 obj 下的句柄，导致 Remove-Item 失败。失败也不致命，忽略即可。
    & dotnet build-server shutdown 2>&1 | Out-Null

    foreach ($dir in @('WinLoop\bin', 'WinLoop\obj')) {
        $target = Join-Path -Path $baseDir -ChildPath $dir
        if (Test-Path -LiteralPath $target) {
            try {
                Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
                Write-Host "  removed $dir"
            }
            catch {
                # 最常见原因：WinLoop.exe 还开着，dll 被占用
                Write-Host "  failed to remove ${dir}: $_" -ForegroundColor Red
                Write-Host '  提示：先退出正在运行的 WinLoop（托盘右键退出），再重试。' -ForegroundColor Yellow
                exit 1
            }
        }
    }
}
else {
    Write-Host 'Skipping bin/obj clean (-NoClean): incremental build.' -ForegroundColor Yellow
}

# ---------- 构建 + 发布 ----------
# ⚠️ 这里**只跑 publish，不要再在前面加 `dotnet build`**。
#    `dotnet publish` 自己就会编译；多跑一次 `dotnet build` 只是白花一倍编译时间 ——
#    它产出的非 RID 那份（bin\Release\netcoreapp3.1\）**没有任何环节消费**，
#    最终 build\<版本>\ 的内容全部来自 win-x64 那份。（2026-09-20 删除了该冗余步骤）
Write-Host 'Building and publishing project...'
Push-Location (Join-Path -Path $scriptDir -ChildPath 'WinLoop')

& dotnet publish -c Release -r win-x64 --self-contained false -o (Join-Path -Path $scriptDir -ChildPath "build/$version")
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Publish failed!' -ForegroundColor Red
    Pop-Location
    exit 1
}
Pop-Location

Write-Host 'Build succeeded!' -ForegroundColor Green
Write-Host "Output: ./build/$version"
