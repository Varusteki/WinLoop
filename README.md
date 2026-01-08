# WinLoop

适用于 Windows 平台的快捷窗口管理工具 - 按住鼠标中键，快速管理窗口！
（本项目代码完全用AI编写）
[![Version](https://img.shields.io/badge/version-0.1-blue.svg)](#)
[![.NET](https://img.shields.io/badge/.NET%20Core-3.1-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-brightgreen.svg)](https://www.microsoft.com/windows)

## ✨ 功能亮点

### 🎯 一键操作
只需按住鼠标中键，移动鼠标选择操作，松开即可完成窗口管理 - 无需记忆复杂的键盘快捷键！

### 🎨 Loop 风格菜单
- **简洁环形设计**：圆环底色 + 蓝色高亮选中
- **透明扇区**：不遮挡屏幕内容
- **8 个方向**：覆盖常用窗口操作

> 说明：当前版本仅开放“圆环（BasicRadial）”菜单样式（为了稳定性，设置页与工厂会强制使用圆环菜单）。

### 🪟 丰富的窗口操作
- **基础操作**：最小化、最大化、显示桌面 (Win+D)
- **半屏分屏**：左/右/上/下 半屏
- **四分屏**：左上/左下/右上/右下 四个象限
- **三分之二屏**：左/右 2/3 屏幕（适合多任务）

### ⚙️ 高度可定制
- 自定义每个扇区的操作（点击扇区选择）
- 调整菜单大小、颜色
- 可配置触发延时（默认 200ms）
- 支持开机自启动
- 最小化到系统托盘

### 🏯 悬空寺（XuanKongSi）覆盖层
- **双击触发键显示**：支持三组触发键：左右 Alt / 左右 Shift / 左右 Ctrl
- **按 ESC 收起**：不会因为点击覆盖层而收起
- **展示内容可配置**：图片 或 文字（支持 Markdown）

## 🚀 快速开始

### 安装
1. 下载最新安装包（`release/WinLoop_V0.1-YYYYMMDDHHMMSS.exe`）
2. 安装并运行 `WinLoop`
3. 程序将在系统托盘运行

（可选）如果你需要免安装版本，可使用 `build/V0.1-YYYYMMDDHHMMSS/` 目录下的发布产物。

### 基本使用
1. **显示菜单**：按住鼠标中键约 0.2 秒
2. **选择操作**：移动鼠标到目标方向（扇区会高亮为蓝色）
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

# 正式版本编译（带版本号）
.\build.ps1
# 输出: ./build/V0.1-{日期时间}/

# 打包安装程序
.\release.ps1
# 输出: ./release/WinLoop_V0.1-{日期时间}.exe
```

### 项目结构
```
WinLoop/
├── WinLoop/                    # 主项目代码
│   ├── App.xaml.cs            # 应用入口、系统托盘、事件处理
│   ├── Config/                # 配置管理
│   │   └── ConfigManager.cs   # JSON 配置读写
│   ├── Core/                  # 核心逻辑
│   ├── Menus/                 # 菜单样式实现
│   │   ├── RadialMenu.cs      # 菜单基类
│   │   ├── BasicRadialMenu.cs # Loop 风格环形菜单
│   │   └── RadialMenuFactory.cs
│   ├── Models/                # 数据模型
│   │   └── AppConfig.cs       # 配置数据结构
│   ├── UI/                    # 用户界面
│   │   ├── MenuOverlayWindow.xaml  # 菜单覆盖层窗口
│   │   ├── XuanKongSiOverlayWindow.xaml  # 悬空寺覆盖层窗口
│   │   └── SettingsWindow.xaml     # 设置窗口
│   ├── SystemIntegration/     # 系统集成
│   │   ├── MouseHookService.cs     # 全局鼠标钩子
│   │   ├── WindowManagementService.cs  # 窗口操作
│   │   └── AutoStartManager.cs     # 开机自启管理
│   └── Utils/                 # 工具类
├── build/                     # 编译输出
├── release/                   # 发布版本
├── deprecated/                # 历史产物/归档（可选删除）
└── Tools/                     # 小工具
```

## 📚 文档

- [产品需求文档 (PRD)](PRD.md)
- [技术设计文档](WinLoop_Technical_Design.md)
## 🗺️ 开发路线图

### ✅ V0.1 (当前版本)
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

### 🔜 V0.2 (计划中)
- [ ] 多显示器支持优化
- [ ] 菜单动画效果
- [ ] 更多窗口操作选项
- [ ] 性能优化

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
