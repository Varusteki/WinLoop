# WinLoop

适用于 Windows 平台的快捷窗口管理工具 - 按住鼠标中键，快速管理窗口！
（本项目代码完全用AI编写）
[![Version](https://img.shields.io/badge/version-0.2-blue.svg)](#)
[![.NET](https://img.shields.io/badge/.NET%20Core-3.1-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-brightgreen.svg)](https://www.microsoft.com/windows)

## ✨ 功能亮点

### 🎯 一键操作
只需按住鼠标中键，移动鼠标选择操作，松开即可完成窗口管理 - 无需记忆复杂的键盘快捷键！

### 🎨 4 种菜单样式
- **圆环（BasicRadial）**：圆环底色 + 蓝色高亮选中，简洁直观
- **八角星（CSHeadshotOctagon）**：硬朗的八角星布局
- **蜘蛛网（SpiderWeb）**：多层同心环 + 放射线
- **八卦（Bagua）**：八卦图形风格

### 🪟 丰富的窗口操作
- **基础操作**：最小化、最大化、显示桌面 (Win+D)
- **窗口状态**：最大化/还原切换、窗口置顶切换、关闭窗口
- **半屏分屏**：左/右/上/下 半屏
- **四分屏**：左上/左下/右上/右下 四个象限
- **三分之二屏**：左/右 2/3 屏幕（适合多任务）

### ⚙️ 高度可定制
- 自定义每个扇区的操作（点击扇区选择）
- 调整菜单大小、颜色
- **颜色选择器**：点击色块弹出取色窗口，RGB / HSV / HSL / HEX 四格式互转，切换格式不漂移
- 可配置触发延时（默认 200ms）
- 支持开机自启动
- 最小化到系统托盘

### 🏯 悬空寺（XuanKongSi）覆盖层
- **双击触发键显示**：支持三组触发键：左右 Alt / 左右 Shift / 左右 Ctrl
- **按 ESC 收起**：不会因为点击覆盖层而收起
- **展示内容可配置**：图片 或 文字（支持 Markdown）
- **图片预览**：设置页可直接看到当前生效的图片，宽度跟随窗口自适应；
  未选图时展示随包分发的默认键位图

## 🚀 快速开始

### 安装
1. 下载最新安装包（`release/WinLoop_V0.2-YYYYMMDDHHMMSS.exe`）
2. 安装并运行 `WinLoop`
3. 程序将在系统托盘运行

（可选）如果你需要免安装版本，可使用 `build/V0.2-YYYYMMDDHHMMSS/` 目录下的发布产物。

### 基本使用
1. **显示菜单**：按住鼠标中键约 0.2 秒
2. **选择操作**：移动鼠标到目标方向（扇区会高亮为蓝色）。
   高亮**只看方向、不看距离**——指针移出菜单图形之外（甚至屏幕另一角）也照样保持高亮；
   只有指针正好落在圆心、方向无定义时才不高亮
3. **执行操作**：松开中键完成操作

### 打开设置
- 双击系统托盘图标
- 或右键托盘图标 → 选择"设置"

## 🎯 默认操作布局

`BasicRadialMenu` 的 8 个扇区从 12 点钟方向开始，按顺时针编号为 `Position1` ~ `Position8`。

默认映射（可在设置中修改）：

| 方向 | 扇区 | 默认动作 |
|---|---|---|
| ↑ | Position1 | 最大化 |
| ↗ | Position2 | 右上 1/4 |
| → | Position3 | 右 2/3 |
| ↘ | Position4 | 回到桌面 |
| ↓ | Position5 | 最小化 |
| ↙ | Position6 | 左下 1/4 |
| ← | Position7 | 左 2/3 |
| ↖ | Position8 | 左上 1/4 |

## 📋 系统要求

- Windows 10 / 11 (64位)
- .NET Core 3.1 Runtime
- 带中键的鼠标

## 🛠️ 开发和构建

### 开发环境
- Visual Studio 2019+ 或 VS Code
- .NET Core 3.1 SDK

### 编译项目
```powershell
# 开发版本编译
dotnet build WinLoop\WinLoop.csproj -c Debug

# 正式构建（产出可双击运行的 exe + 自动打开产物目录）
.\job-build.ps1
# 输出: ./build/V0.2-{日期时间}/

# 打包安装程序（需要 Inno Setup；找不到时降级出免安装 zip）
.\job-pack.ps1
# 输出: ./release/WinLoop_V0.2-{日期时间}.exe
```

> ⚠️ **打包前先清 `WinLoop\bin` 和 `WinLoop\obj`**，否则增量构建会把上一版
> 残留的 dll 一起带进产物，体积会异常膨胀（曾出现 dll 涨到 34MB）。

底层脚本 `build.ps1` / `release.ps1` 只负责干活、不打开目录；
日常用上层的 `job-build.ps1` / `job-pack.ps1` 即可。

### 运行时自检
发布产物自带两个自检开关，用于验证「编译通过但运行时才暴露」的问题：
```powershell
# 字体自检：验证内嵌字体能否解析、字重是否落到不同字面
# 注意：参数是【日志文件路径】
.\build\latest\WinLoop.exe --fontcheck .\fontcheck.txt

# 设置窗口自检：构造窗口、逐页截图、断言布局与控件尺寸
# 注意：参数是【输出目录】，报告写在 <目录>\report.txt
.\build\latest\WinLoop.exe --settingscheck .\probe-out\sc
```

这两个自检**必须挂在 `WinLoop.exe` 上**，不能用外部探针程序 ——
`pack://application:,,,` 的解析基准是入口程序集，外部探针永远解析不到。

### 项目结构
```
WinLoop/
├── WinLoop/                        # 主项目
│   ├── App.xaml.cs                # 应用入口、系统托盘、自检开关分发
│   ├── app.manifest               # Per-Monitor V2 DPI 感知声明
│   ├── Config/
│   │   └── ConfigManager.cs       # JSON 配置读写
│   ├── Menus/                     # 菜单样式实现
│   │   ├── RadialMenu.cs          # 菜单基类
│   │   ├── BasicRadialMenu.cs     # 圆环
│   │   ├── CSHeadshotMenu.cs      # 八角星
│   │   ├── CSHeadshotLayers.cs    # 八角星图层数据
│   │   ├── CSHeadshotPathData.cs  # 八角星路径数据
│   │   ├── SpiderWebMenu.cs       # 蜘蛛网
│   │   ├── BaguaMenu.cs           # 八卦
│   │   └── RadialMenuFactory.cs   # 按配置创建菜单实例
│   ├── Models/
│   │   ├── AppConfig.cs           # 配置数据结构
│   │   └── SizingScale.cs         # 尺寸缩放（DPI 换算）
│   ├── UI/
│   │   ├── MenuOverlayWindow.xaml      # 菜单覆盖层
│   │   ├── XuanKongSiOverlayWindow.xaml # 悬空寺覆盖层
│   │   ├── SettingsWindow.xaml         # 设置窗口
│   │   ├── ColorPickerWindow.xaml      # 颜色选择器
│   │   ├── FontSelfCheck.cs            # --fontcheck 实现
│   │   └── SettingsSelfCheck.cs        # --settingscheck 实现
│   ├── SystemIntegration/         # 系统集成
│   │   ├── MouseHookService.cs         # 全局鼠标钩子
│   │   ├── KeyboardHookService.cs      # 全局键盘钩子（悬空寺双击检测）
│   │   ├── WindowManagementService.cs  # 窗口操作
│   │   └── AutoStartManager.cs         # 开机自启
│   └── Resources/
│       ├── Fonts/                 # 内嵌字体（Noto Sans SC Regular/Medium）
│       ├── XuanKongSi/            # 悬空寺键位图（4 套双拼方案）
│       ├── skull.xaml             # 八角星图形资源
│       └── trayIcon.ico
├── Tools/                         # 开发期小工具（不随包分发）
│   ├── CpTest/                    # 颜色选择器真值测试
│   ├── ConfigTester/              # 配置读写测试
│   ├── ShowSettings/              # 单独打开设置窗口
│   ├── MenuShot/ MenuProbe/       # 菜单截图与探针
│   └── ExplorerFocus/             # 把资源管理器窗口拉到最前
├── job-build.ps1 / job-pack.ps1   # 作业脚本（构建/打包 + 打开目录）
├── build.ps1 / release.ps1        # 底层构建/打包脚本
├── open-artifact.ps1              # 打开产物目录的共用工具
├── build/                         # 构建输出（带时间戳，保留历史）
├── release/                       # 安装包输出
└── probe-out/                     # 临时探针输出（可删）
```

## 📚 文档

| 文档 | 说明 |
|---|---|
| [产品需求文档 (PRD)](PRD.md) | 功能定位、设置项与默认值、版本改动记录 |
| [技术设计文档](WinLoop_Technical_Design.md) | 架构、核心模块、关键问题解决方案、构建部署 |

非项目必需的开发过程产物（旧文件备份、方案对比稿等）统一归档在 [docs/archive/](docs/archive/)，
目录内附 `README.md` 说明每份文件的来源与归档原因。

## 🗺️ 开发路线图

### ✅ V0.1
- [x] 设置窗口（菜单样式、操作配置、杂项设置）
- [x] Loop 风格环形菜单（白色轮廓 + 蓝色高亮）
- [x] 全局鼠标钩子（中键按下/释放检测）
- [x] 窗口管理操作（13 种操作）
- [x] 系统托盘集成（双击/右键菜单）
- [x] 开机自启动（注册表方式）
- [x] 配置持久化（JSON 格式）
- [x] 触发时长可配置（实时生效）
- [x] 悬空寺（XuanKongSi）覆盖层（图片 / Markdown 文字）
- [x] 自定义悬空寺触发键（左右 Alt / 左右 Shift / 左右 Ctrl）

### ✅ V0.2 (当前版本)
- [x] **目标窗口锁定**：中键按下瞬间锁定窗口，菜单弹出后不再误操作其它窗口
- [x] **性能优化**：移除分屏时的 50ms 阻塞等待
- [x] **多显示器 / 高 DPI 支持**：Per-Monitor V2 DPI 感知 + 物理像素坐标统一
- [x] **鼠标跟随优化**：由 16ms 轮询改为钩子事件推送
- [x] **菜单动画**：弹出缩放淡入 / 收起淡出
- [x] **更多窗口操作**：最大化还原切换、窗口置顶切换、关闭窗口
- [x] **恢复 4 种菜单样式**：圆环 / 八角星 / 蜘蛛网 / 八卦
- [x] **代码清理**：移除约 500 行不可达代码

#### 界面与体验迭代
- [x] **内嵌字体**：打包 Noto Sans SC，界面渲染不依赖目标机器已装字体
- [x] **颜色选择器**：独立取色窗口，RGB / HSV / HSL / HEX 互转
- [x] **设置窗口改版**：固定尺寸 + PixPin 行流版式
- [x] **色值框隐藏化**：色值改由色块按钮直接显示
- [x] **输入框统一等宽**：宽度令牌收进统一样式，行内单位提示移除
- [x] **悬空寺图片预览**：宽度自适应、等比缩放，默认图也展示
- [x] **运行时自检**：`--fontcheck` / `--settingscheck`

### 🔮 V0.3+ (未来)
- [ ] 手势识别
- [ ] 快捷键支持
- [ ] 主题系统
- [ ] 插件系统

## 🙏 致谢

### 参考项目
- [Loop](https://github.com/MrKai77/Loop) - 功能设计和交互灵感来源
- [WGestures](https://github.com/yingDev/WGestures) - Windows 平台鼠标钩子技术参考

---

<!-- chore: trigger GitHub homepage refresh -->
