# WinLoop —— 打开产物目录并定位到文件（内部共用工具）
#
# 用法:
#   .\open-artifact.ps1 -Path .\build\latest\WinLoop.exe   # 打开所在目录并选中该文件
#   .\open-artifact.ps1 -Folder .\build\latest             # 只打开目录
#   .\open-artifact.ps1 -Path <新构建目录>\WinLoop.exe -Group .\build
#                                                          # 指定"分组目录"：同组已有窗口时
#                                                          # 直接复用并导航过去，不再新开窗口
#
# 说明:
#   优先调用 Tools\ExplorerFocus（本地编译的小工具）——它能绕过 Windows 的前台锁定，
#   把资源管理器窗口真正拉到最前面；工具不存在时会自动编译一次。
#   任何一步失败都退回 Start-Process explorer.exe，保证"至少能打开"。

param(
    [string]$Path,
    [string]$Folder,
    [string]$Group
)

$ErrorActionPreference = 'Continue'

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$baseDir = $PSScriptRoot
if (-not $baseDir) { $baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $baseDir) { $baseDir = (Get-Location).ProviderPath }
$baseDir = (Resolve-Path -LiteralPath $baseDir).Path

# ---- 解析目标：要选中的文件 / 要打开的目录 ----
$selectFile = $null
$openFolder = $null

if ($Path -and (Test-Path -LiteralPath $Path)) {
    $selectFile = (Resolve-Path -LiteralPath $Path).Path
    $openFolder = Split-Path -Parent $selectFile
}
elseif ($Folder -and (Test-Path -LiteralPath $Folder)) {
    $openFolder = (Resolve-Path -LiteralPath $Folder).Path
}

if (-not $openFolder) {
    Write-Host 'open-artifact: 目标不存在，跳过打开。' -ForegroundColor Yellow
    return
}

# ---- 找到（必要时编译）置顶小工具 ----
$helperCandidates = @(
    (Join-Path $baseDir 'Tools\ExplorerFocus\bin\Release\netcoreapp3.1\ExplorerFocus.exe'),
    (Join-Path $baseDir 'Tools\ExplorerFocus\bin\Debug\netcoreapp3.1\ExplorerFocus.exe')
)
$helper = $helperCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if (-not $helper) {
    $helperProj = Join-Path $baseDir 'Tools\ExplorerFocus\ExplorerFocus.csproj'
    if (Test-Path -LiteralPath $helperProj) {
        Write-Host '首次运行：编译置顶小工具...'
        try {
            & dotnet build $helperProj -c Release --nologo -v quiet | Out-Null
            $helper = $helperCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
        }
        catch { }
    }
}

# ---- 解析分组目录（可选）----
$groupDir = $null
if ($Group -and (Test-Path -LiteralPath $Group)) {
    $groupDir = (Resolve-Path -LiteralPath $Group).Path
}

# ---- 方式一：置顶小工具（首选）----
if ($helper) {
    try {
        $out = & $helper $openFolder $selectFile $groupDir 2>&1 | Out-String

        if ($out -match 'RESULT=OK') {
            $reused = ($out -match 'NEWWINDOW=REUSE')
            $navigated = ($out -match 'NEWWINDOW=NAVIGATED')
            $selected = ($out -match 'SELECTED=True')

            if ($reused) {
                Write-Host "已复用已打开的资源管理器窗口: $openFolder" -ForegroundColor Green
            }
            elseif ($navigated) {
                Write-Host "已复用同组窗口并导航到: $openFolder" -ForegroundColor Green
            }
            else {
                Write-Host "已打开资源管理器窗口: $openFolder" -ForegroundColor Green
            }

            # 只有复用/导航这两种情况才需要自己校验选中结果；
            # 新开窗口时 explorer /select 本身就会选中文件。
            if ($selectFile -and ($reused -or $navigated)) {
                if ($selected) {
                    Write-Host "  已选中: $(Split-Path -Leaf $selectFile)" -ForegroundColor Green
                }
                else {
                    Write-Host "  提示：窗口已复用，但未能在列表中定位到 $(Split-Path -Leaf $selectFile)（可能列表未及时刷新）。" -ForegroundColor Yellow
                }
            }
            return
        }

        Write-Host "置顶工具未成功，改用备用方式打开。" -ForegroundColor Yellow
    }
    catch {
        Write-Host "置顶工具调用异常，改用备用方式打开。" -ForegroundColor Yellow
    }
}

# ---- 方式二：直接调 explorer（至少能把窗口建出来）----
try {
    if ($selectFile) {
        Start-Process explorer.exe -ArgumentList "/select,`"$selectFile`""
    }
    else {
        Start-Process explorer.exe -ArgumentList $openFolder
    }
    Write-Host "已打开目录: $openFolder" -ForegroundColor Green
}
catch {
    Write-Host "打开目录失败: $($_.Exception.Message)" -ForegroundColor Red
}

return