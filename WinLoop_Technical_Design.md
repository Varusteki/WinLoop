# WinLoop 技术设计文档

## 1. 项目概述

WinLoop 是一款适用于 Windows 平台的快捷窗口管理工具，核心包括：

- 通过按住鼠标中键触发环形菜单，快速选择并执行预设窗口操作（最大化、分屏、回到桌面等）。
- “悬空寺（XuanKongSi）”覆盖层：通过双击指定按键（左右 Alt/Shift/Ctrl 三组之一）弹出提示卡片；按 ESC 收起。

### 1.1 核心功能
- **鼠标中键触发**：按住鼠标中键达到设定时长后显示操作菜单
- **环形菜单系统**：Loop 风格为主的圆环菜单（圆环底色 + 蓝色高亮），并内置多种菜单样式实现
- **窗口操作**：提供 13 种窗口管理功能
- **系统托盘集成**：通过系统托盘访问设置和管理工具
- **悬空寺覆盖层**：双击热键显示（支持文字/图片内容），ESC 收起；支持多屏/DPI。

> 备注：当前版本为了稳定性，设置页与菜单工厂会强制使用圆环菜单（`MenuStyle.BasicRadial`）。其他菜单样式实现仍保留在代码中，但不对用户开放。

### 1.2 技术栈
- **编程语言**：C#
- **框架**：.NET Core 3.1
- **UI 技术**：WPF
- **系统 API**：Windows API (user32.dll)

## 2. 架构设计

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                         WinLoop 应用 (App.xaml.cs)                  │
├─────────────────┬─────────────────┬─────────────────────────────────┤
│    系统集成层    │     UI 层       │           核心层               │
├─────────────────┼─────────────────┼─────────────────────────────────┤
│ MouseHookService│ MenuOverlayWindow│ ConfigManager                  │
│ KeyboardHookSvc │ SettingsWindow   │ AppConfig                      │
│ WindowManagement│ XuanKongSiOverlay │ RadialMenuFactory              │
│ AutoStartManager│ (WPF Window)     │ (Menu Styles)                  │
└─────────────────┴─────────────────┴─────────────────────────────────┘
```

### 2.2 模块划分

| 模块 | 主要职责 | 核心类/文件 |
|------|----------|-------------|
| 应用入口 | 初始化服务、事件分发、系统托盘 | App.xaml.cs |
| 鼠标钩子 | 全局鼠标事件监控、触发延时 | MouseHookService.cs |
| 键盘钩子 | 全局键盘事件监控、双击触发、ESC 收起 | KeyboardHookService.cs |
| 窗口管理 | 执行窗口操作（分屏、最大化等） | WindowManagementService.cs |
| 菜单覆盖 | 显示菜单、鼠标跟踪、执行选中 | MenuOverlayWindow.xaml.cs |
| 菜单样式 | 绘制不同风格的菜单与高亮 | BasicRadialMenu.cs 等 |
| 悬空寺覆盖层 | 显示提示卡片、动画、内容渲染（文字/图片） | XuanKongSiOverlayWindow.xaml(.cs) |
| 设置窗口 | 配置界面、实时预览 | SettingsWindow.xaml.cs |
| 配置管理 | JSON 配置读写（含兼容旧配置字段） | ConfigManager.cs |
| 开机自启 | 注册表自启动管理 | AutoStartManager.cs |

## 3. 核心模块实现

### 3.1 鼠标钩子服务 (MouseHookService)

#### 功能描述
- 使用 Windows 低级鼠标钩子 (WH_MOUSE_LL) 监控全局鼠标事件
- 检测中键按下/释放，实现延时触发机制
- 每次按下都会创建新的定时器实例（避免复用导致不触发）

#### 实现要点
- 钩子运行在后台线程，并通过 `System.Windows.Forms.Application.Run()` 保持消息循环。
- 中键按下时启动一次性 `System.Timers.Timer`；若到时仍保持按下则触发菜单显示。

#### 关键实现
```csharp
public class MouseHookService
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    
    public int TriggerDelayMs { get; set; } = 200;
    public event Action<Point> MiddleButtonTriggered;
    public event Action MiddleButtonReleased;
    
    private System.Timers.Timer _triggerTimer;
    private volatile bool _isMiddleButtonDown;
    private Point _downPos;
    
    // 钩子回调处理中键事件
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_MBUTTONDOWN)
        {
            _isMiddleButtonDown = true;
            _downPos = GetMousePositionFromHook(lParam);
            // 每次创建新定时器避免复用问题
            _triggerTimer?.Dispose();
            _triggerTimer = new System.Timers.Timer(TriggerDelayMs);
            _triggerTimer.AutoReset = false;
            _triggerTimer.Elapsed += (s, e) => {
                if (_isMiddleButtonDown)
                    MiddleButtonTriggered?.Invoke(_downPos);
            };
            _triggerTimer.Start();
        }
        else if (msg == WM_MBUTTONUP)
        {
            _isMiddleButtonDown = false;
            _triggerTimer?.Stop();
            MiddleButtonReleased?.Invoke();
        }
        return CallNextHookEx(...);
    }
}
```

> 说明：上面为逻辑示意；实际实现包含更完整的线程/日志/异常兜底处理。

### 3.2 菜单覆盖窗口 (MenuOverlayWindow)

#### 功能描述
- 全屏透明窗口，覆盖虚拟屏幕（支持多显示器）
- 动态创建菜单并定位到鼠标位置
- 使用 DispatcherTimer 轮询鼠标位置更新高亮
- 每次显示都创建新窗口实例（避免闪烁）

#### 交互说明
- 鼠标松开中键时，若当前高亮扇区存在映射动作则执行；未高亮扇区则不执行动作。

#### 关键实现
```csharp
public partial class MenuOverlayWindow : Window
{
    private DispatcherTimer _mouseTrackingTimer; // 60fps 鼠标跟踪
    private RadialMenu _currentMenu;
    private Point _centerPosition;
    private MenuItemPosition? _highlightedPosition;
    
    public void ShowAt(Point screenPosition)
    {
        _centerPosition = screenPosition;
        
        // 覆盖整个虚拟屏幕
        this.Left = SystemParameters.VirtualScreenLeft;
        this.Top = SystemParameters.VirtualScreenTop;
        this.Width = SystemParameters.VirtualScreenWidth;
        this.Height = SystemParameters.VirtualScreenHeight;
        
        // 创建并定位菜单
        _currentMenu = factory.CreateMenu(...);
        Canvas.SetLeft(_currentMenu, screenPosition.X - radius);
        Canvas.SetTop(_currentMenu, screenPosition.Y - radius);
        OverlayCanvas.Children.Add(_currentMenu);
        
        this.Show();
        Mouse.Capture(OverlayCanvas);
        _mouseTrackingTimer.Start();
    }
    
    public void ExecuteAction()
    {
        // 保存动作 -> 停止定时器 -> 隐藏窗口 -> 执行动作
        var action = GetSelectedAction();
        _mouseTrackingTimer.Stop();
        Mouse.Capture(null);
        this.Hide();
        ActionSelected?.Invoke(action);
    }
}
```

### 3.3 环形菜单 (BasicRadialMenu)

#### 功能描述
- Loop 风格设计：圆环底色 + 透明扇区 + 蓝色高亮
- 8 个扇区，每个 45 度
- 高亮时绘制填充扇形覆盖层

#### 说明
除 `BasicRadialMenu` 外，项目还包含其他菜单样式（如 CSHeadshot/SpiderWeb/Bagua）实现。

当前版本：
- `SettingsWindow` 会强制菜单样式为 `BasicRadial`
- `RadialMenuFactory` 会对非 `BasicRadial` 的输入回退到 `BasicRadial`

#### 关键实现
```csharp
public class BasicRadialMenu : RadialMenu
{
    // 默认颜色
    public string RingColor { get; set; } = "#FFFFFF";      // 白色轮廓
    public string HighlightColor { get; set; } = "#007AFF"; // 蓝色高亮
    
    protected override void OnRender(DrawingContext dc)
    {
        // 1. 绘制透明背景的白色轮廓环
        var ringPen = new Pen(new SolidColorBrush(ParseColor(RingColor)), 2);
        dc.DrawEllipse(Brushes.Transparent, ringPen, center, OuterRadius, OuterRadius);
        dc.DrawEllipse(Brushes.Transparent, ringPen, center, InnerRadius, InnerRadius);
        
        // 2. 绘制 8 条分隔线
        for (int i = 0; i < 8; i++)
        {
            double angle = i * 45 - 90 + 22.5; // 从12点开始
            // 绘制从内圆到外圆的线段
        }
        
        // 3. 如果有高亮，绘制填充扇形
        if (_highlightedPosition.HasValue)
        {
            DrawHighlightSector(dc, _highlightedPosition.Value);
        }
    }
}
```

### 3.4 窗口管理服务 (WindowManagementService)

#### 功能描述
- 封装 Windows API 实现窗口操作
- 支持 13 种窗口操作
- 显示桌面通过模拟 Win+D 快捷键实现

#### 支持的操作
| 操作 | 实现方式 |
|------|----------|
| 最大化 | ShowWindow(hwnd, SW_MAXIMIZE) |
| 最小化 | ShowWindow(hwnd, SW_MINIMIZE) |
| 显示桌面 | keybd_event(VK_LWIN + VK_D) |
| 左/右/上/下半屏 | MoveWindow + 屏幕尺寸计算 |
| 四分屏 | MoveWindow + 屏幕尺寸计算 |
| 左/右 2/3 屏 | MoveWindow + 屏幕尺寸计算 |

#### 关键实现
```csharp
public class WindowManagementService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    
    private void ShowDesktop()
    {
        // 模拟 Win+D 快捷键
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(VK_D, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
    
    private void SetWindowToHalf(HalfPosition position)
    {
        var hwnd = GetForegroundWindow();
        var workArea = SystemParameters.WorkArea;
        // 根据 position 计算 x, y, width, height
        MoveWindow(hwnd, x, y, width, height, true);
    }
}
```

### 3.5 应用入口 (App.xaml.cs)

#### 功能描述
- 初始化所有服务并建立事件连接
- 管理系统托盘图标和菜单
- 每次触发创建新的 MenuOverlayWindow（避免闪烁）
- 提供静态方法更新触发时长

#### 关键流程
```csharp
public partial class App : Application
{
    private static App _instance;
    private MouseHookService _mouseHookService;
    private MenuOverlayWindow _menuOverlayWindow;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = this;
        
        // 初始化服务
        _mouseHookService = new MouseHookService();
        _mouseHookService.TriggerDelayMs = config.TriggerDelay;
        
        // 订阅事件
        _mouseHookService.MiddleButtonTriggered += OnMiddleButtonTriggered;
        _mouseHookService.MiddleButtonReleased += OnMiddleButtonReleased;
        
        _mouseHookService.Start();
    }
    
    private void OnMiddleButtonTriggered(Point position)
    {
        Dispatcher.Invoke(() =>
        {
            // 每次都创建新窗口避免闪烁
            _menuOverlayWindow?.Close();
            _menuOverlayWindow = new MenuOverlayWindow();
            _menuOverlayWindow.ActionSelected += OnActionSelected;
            _menuOverlayWindow.ShowAt(position);
        });
    }
    
    public static void UpdateTriggerDelay(int delayMs)
    {
        _instance?._mouseHookService.TriggerDelayMs = delayMs;
    }
}
```

## 4. 数据结构

### 4.1 配置数据 (AppConfig)

```csharp
public class AppConfig
{
    public MenuStyle MenuStyle { get; set; } = MenuStyle.BasicRadial;

    // 各菜单样式配置
    public BasicRadialMenuConfig BasicRadialMenuConfig { get; set; } = new BasicRadialMenuConfig();
    public CSHeadshotMenuConfig CSHeadshotMenuConfig { get; set; } = new CSHeadshotMenuConfig();
    public SpiderWebMenuConfig SpiderWebMenuConfig { get; set; } = new SpiderWebMenuConfig();
    public BaguaMenuConfig BaguaMenuConfig { get; set; } = new BaguaMenuConfig();

    // 操作映射（8 个扇区 -> 窗口操作）
    public Dictionary<MenuItemPosition, WindowAction> ActionMapping { get; set; }

    // 杂项设置
    public bool AutoStart { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;

    // 鼠标中键长按触发延迟（毫秒）
    public int TriggerDelay { get; set; } = 200;

    // 悬空寺覆盖层
    public XuanKongSiConfig XuanKongSi { get; set; } = new XuanKongSiConfig();
}

public class BasicRadialMenuConfig
{
    public double OuterRadius { get; set; } = 50;
    public double InnerRadius { get; set; } = 28;
    public double Thickness { get; set; } = 22;
    public string RingColor { get; set; } = "#C0C0C0";       // 银灰色
    public string HighlightColor { get; set; } = "#007AFF";  // 蓝色高亮
}

public enum WindowAction
{
    BackToDesktop, Minimize, Maximize,
    LeftHalf, RightHalf, TopHalf, BottomHalf,
    TopLeftQuadrant, BottomLeftQuadrant, TopRightQuadrant, BottomRightQuadrant,
    LeftTwoThirds, RightTwoThirds
}

public enum MenuItemPosition
{
    Position1,
    Position2,
    Position3,
    Position4,
    Position5,
    Position6,
    Position7,
    Position8
}

public class XuanKongSiConfig
{
    public bool Enabled { get; set; } = true;
    public XuanKongSiScheme Scheme { get; set; } = XuanKongSiScheme.Xiaohe;

    // 热键（支持“左右”以及“三组：左右 Alt/Shift/Ctrl”）
    public XuanKongSiTriggerKey TriggerKey { get; set; } = XuanKongSiTriggerKey.LeftShift;

    // 双击间隔阈值（毫秒）
    public int HoldDurationMs { get; set; } = 300;

    // 展示内容：图片 / 文字（Web 字段保留兼容）
    public XuanKongSiContentType ContentType { get; set; } = XuanKongSiContentType.Image;
    public string TextXaml { get; set; } = "";
    public string ImageFileName { get; set; } = "";
    public string WebUrl { get; set; } = "";
}

public enum XuanKongSiContentType
{
    Image,
    Text,
    Web
}

public enum XuanKongSiScheme
{
    Xiaohe,
    Ziranma,
    Microsoft,
    Ziguang
}

public enum XuanKongSiTriggerKey
{
    LeftCtrl = 0,
    RightCtrl = 1,
    LeftShift = 2,
    RightShift = 3,
    LeftAlt = 4,
    RightAlt = 5,
    LeftWin = 6,
    RightWin = 7,

    // 新增三组：任一侧均可触发
    Alt = 8,
    Shift = 9,
    Ctrl = 10
}
```

### 4.2 配置文件位置

```
%LOCALAPPDATA%\WinLoop\config.json
%APPDATA%\WinLoop\log.txt
%LOCALAPPDATA%\WinLoop\Media\  (悬空寺自定义图片存储目录)
```

### 4.3 配置序列化要点

- `ActionMapping` 的键为 `MenuItemPosition`（枚举），写入 JSON 时会转换为字符串键（DTO 方式）以保证可读性与兼容性。
- 读取配置时支持兼容旧字段名（用于历史版本迁移）。

## 5. 关键问题解决方案

### 5.1 菜单闪烁问题
**问题**：复用 MenuOverlayWindow 时，旧菜单内容会短暂显示
**解决**：每次触发都创建新窗口实例，关闭旧窗口

### 5.2 定时器复用问题
**问题**：System.Timers.Timer 停止后再启动，Elapsed 事件不触发
**解决**：每次都创建新的定时器实例，先 Dispose 旧定时器

### 5.3 触发时长实时生效
**问题**：设置窗口保存后触发时长不生效
**解决**：App 提供静态方法 UpdateTriggerDelay()，保存时调用

### 5.4 显示桌面功能
**问题**：Shell API 方式不稳定
**解决**：改用 keybd_event 模拟 Win+D 快捷键

### 5.5 悬空寺 Markdown 渲染卡死
**问题**：在解析 Markdown 时遇到不匹配的特殊字符（如 `[`）可能导致指针不前进，从而死循环卡死。

**解决**：解析器在无法匹配语法时强制消费当前字符并继续前进，避免死循环。

### 5.6 悬空寺内容渲染与兼容

- 文字模式：编辑器输入 Markdown，运行时渲染为 WPF `FlowDocument`；同时兼容旧版本保存的 `FlowDocument` XAML。
- 图片模式：优先加载用户在 `%LOCALAPPDATA%\WinLoop\Media\` 下选择的图片；若未配置或加载失败，会回退到应用自带的 `WinLoop/Resources/XuanKongSi/` 默认图片。

## 6. 构建与部署

### 6.1 构建脚本 (build.ps1)
```powershell
# 输出: ./build/<version>
# 可选参数：-NoOpen（不自动打开输出目录）
$version = "V0.1-$(Get-Date -Format 'yyyyMMddHHmm')"
dotnet build -c Release
dotnet publish -c Release -o ./build/$version
```

### 6.2 发布脚本 (release.ps1)

- 负责：调用构建/发布，并通过 Inno Setup 生成安装包
- 输出：`release/WinLoop_V0.1-YYYYMMDDHHMMSS.exe`

### 6.3 开机自启动
通过注册表实现：
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
键名: WinLoop
值: "{exe路径}"
```

## 7. 版本历史

### V0.1 (2025-12-20)
- ✅ 完整的设置窗口（菜单样式、操作配置、杂项设置）
- ✅ Loop 风格环形菜单（白色轮廓 + 蓝色高亮 + 透明扇区）
- ✅ 全局鼠标钩子（稳定的中键检测和延时触发）
- ✅ 13 种窗口操作（包括 Win+D 显示桌面）
- ✅ 系统托盘集成
- ✅ 配置持久化
- ✅ 触发时长实时生效

### V0.1 (2026-01)
- ✅ 新增“悬空寺（XuanKongSi）”覆盖层：双击热键显示、ESC 收起
- ✅ 悬空寺内容支持 Markdown（渲染到 WPF 文档），并兼容旧 XAML 文档
- ✅ 触发键支持三组：左右 Alt / 左右 Shift / 左右 Ctrl

## 8. 参考资料

- [Loop (macOS)](https://github.com/MrKai77/Loop) - 功能设计参考
- [WGestures](https://github.com/yingDev/WGestures) - Windows 鼠标钩子参考
- [Windows API 文档](https://docs.microsoft.com/en-us/windows/win32/api/)
