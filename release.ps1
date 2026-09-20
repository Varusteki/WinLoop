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
    [switch]$NoZipFallback,
    [switch]$NoClean
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
# build.ps1 默认会清 bin/obj（原因见其文件头），这里把 -NoClean 透传下去
Write-Host "Running build script: $buildScriptPath -Version $Version"
$buildArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $buildScriptPath, '-Version', $Version)
if ($NoClean) { $buildArgs += '-NoClean' }
& powershell @buildArgs
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
    # 常见安装位置：**7 优先于 6**。
    # 原因：下面生成的 ISS 会用到 `SetupArchitecture=x64`，那是 **Inno Setup 7 引入**的指令，
    # 6.x 遇到会直接 `Error: Unrecognized [Setup] section directive` + Compile aborted。
    # （本机只装了 6 时脚本会自动退回经典写法 —— 见后面的版本探测段。）
    $candidates = @(
        "C:\Program Files\Inno Setup 7\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
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

# 安装程序自身的图标 —— **必须显式指定**。
# 不设 SetupIconFile 的话，setup.exe 会显示 Inno Setup 的**默认图标**（一个和产品
# 无关的通用安装图标），而安装出来的 WinLoop.exe 却是新图标 —— 两者对不上。
# 用与 WinLoop.exe 同一份 appIcon，保证「安装包 / 安装结果」视觉一致。
# 路径必须写绝对路径：.iss 生成在临时目录，相对路径解析不到仓库里。
$appIconPath = Join-Path -Path $baseDir -ChildPath 'WinLoop\Resources\appIcon.ico'
$setupIconLine = ''
if (Test-Path -LiteralPath $appIconPath) {
    $setupIconLine = "SetupIconFile=$appIconPath"
    Write-Host "Installer icon: $appIconPath"
}
else {
    Write-Host "警告：找不到 $appIconPath，安装程序将退回 Inno Setup 默认图标。" -ForegroundColor Yellow
}

# 探测 ISCC 主版本，决定是否启用 `SetupArchitecture=x64`。
#
# ⚠️ ISCC.exe **没有版本资源**（VersionInfo 恒为 0.0.0.0），别去读文件属性，
#    只能解析 `ISCC /?` 的第一行：「Inno Setup 7 Command-Line Compiler」。
#
# 为什么按版本决定、而不是无脑写上：
#   `SetupArchitecture` 是 **Inno Setup 7 引入**的指令，6.x 遇到会**直接编译中止**
#   （实测 `Error on line N: Unrecognized [Setup] section directive "SetupArchitecture"`）。
#   探测不到版本时（返回 0）取**保守值 —— 不加**，退回经典写法：
#   宁可少一个「安装程序本身是 64 位」的收益，也不能让打包直接失败。
$isccMajor = 0
try {
    $firstLine = "$(& $iscc /? 2>&1 | Select-Object -First 1)"
    if ($firstLine -match 'Inno\s+Setup\s+(\d+)') { $isccMajor = [int]$Matches[1] }
}
catch {
    Write-Host "警告：无法探测 ISCC 版本（$_），按 6.x 处理。" -ForegroundColor Yellow
}

$setupArchLine = ''
if ($isccMajor -ge 7) {
    # 让**安装程序自身**成为 64 位可执行文件（高熵 ASLR）。同时 Inno 会把
    # `ArchitecturesAllowed` / `ArchitecturesInstallIn64BitMode` 的默认值
    # 一并变成 x64compatible —— 所以下面那两行在 IS7 下属于"显式重申默认值"，
    # 保留它们是为了回退到 IS6 时行为不变，不是在纠正默认行为。
    $setupArchLine = 'SetupArchitecture=x64'
    Write-Host "ISCC major = $isccMajor -> SetupArchitecture=x64 enabled (64-bit installer)" -ForegroundColor Green
}
else {
    Write-Host "ISCC major = $isccMajor (<7) -> SetupArchitecture unsupported; classic 32-bit setup + 64-bit install mode." -ForegroundColor Yellow
}

$issContent = @"
[Setup]
AppName=WinLoop
AppVersion=$versionName
; 64 位程序必须**显式声明**，否则 Inno 默认按 32 位模式跑，{autopf} 会解析成
; 「C:\Program Files (x86)」—— 把 64 位程序装进 32 位目录并不合适。
; x64compatible：在 64 位 Windows 上安装（含 ARM64 上的 x64 模拟），
; 同时禁止在 32 位系统上安装。
; 注：ISCC 为 7+ 时下面还会多一行 SetupArchitecture=x64（内容来自 $setupArchLine），
;     届时这两条恰好就是 Inno 的默认值；保留它们是为了回退到 IS6 时行为一致。
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
$setupArchLine
DefaultDirName={autopf}\\WinLoop
DefaultGroupName=WinLoop
OutputBaseFilename=WinLoop_$versionName
Compression=lzma
SolidCompression=yes
$setupIconLine
; 「应用和功能」/「添加或删除程序」列表里显示哪个图标 —— 指向装好的 exe
UninstallDisplayIcon={app}\\WinLoop.exe

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "$tmpDir\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; 清理旧版残留：在声明 64 位之前，安装目录是「C:\Program Files (x86)\WinLoop」。
; 不清理的话升级后会同时存在两套文件，而只有新的那套受卸载程序管理。
; {commonpf32} 恒指 32 位 Program Files，与本安装程序自身的位数无关。
; 注：若旧版正在运行，删除会失败并提示 —— 先退出 WinLoop 再装。
Type: filesandordirs; Name: "{commonpf32}\\WinLoop"

[Registry]
; 同理清掉旧版留在 **32 位注册表视图**里的卸载项，避免「应用和功能」里出现两条 WinLoop。
; 旧版是 admin 模式安装的，卸载项在 HKLM 下（不是 HKCU）；
; deletekey 对不存在的键是空操作，重复安装也安全。
Root: HKLM32; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WinLoop_is1"; Flags: deletekey

[Icons]
Name: "{group}\\WinLoop"; Filename: "{app}\\WinLoop.exe"
Name: "{autodesktop}\\WinLoop"; Filename: "{app}\\WinLoop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\\WinLoop.exe"; Description: "Run WinLoop"; Flags: nowait postinstall skipifsilent

; 装完强制让外壳刷新一次 —— 否则「新创建的桌面快捷方式」可能不立刻出现。
; 原因：安装程序以管理员身份运行，往「所有用户桌面」（{autodesktop} 在 admin 模式
; 下解析到的位置）写快捷方式属于**跨安全上下文写入**，Explorer 的桌面视图经常不会
; 即时更新，表现为「勾了创建桌面图标、装完却看不到，按 F5 才出来」。
; SHCNE_ASSOCCHANGED = 0x08000000，SHCNF_IDLIST = 0（无需传路径）。
; 注：[Code] 段刻意只用 ASCII 注释 —— 生成 .iss 时是 UTF8+BOM，少一层编码风险。
[Code]
{ Force a shell refresh after install.
  The desktop shortcut is created on the All Users desktop while Setup runs elevated.
  Explorer frequently does not refresh the desktop view for such cross-context writes,
  so the new icon may not show up until a manual refresh / re-login.
  SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0 (no path needed). }
procedure SHChangeNotify(wEventId: Integer; uFlags: Cardinal; dwItem1, dwItem2: Cardinal);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    SHChangeNotify(134217728, 0, 0, 0);
  end;
end;
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
