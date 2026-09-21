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
- **悬空寺覆盖层**：双击热键显示（支持图片/文字内容），ESC 收起；支持多屏/DPI。
- **颜色选择器**：设置页所有颜色项点击色块即弹出取色窗口，支持 RGB / HSV / HSL / HEX 四种格式互转。

> 备注（V0.2 起）：4 种菜单样式 **全部对用户开放**，可在设置页「菜单样式」中切换
> （圆环 / Headshot / 蜘蛛网 / 八卦）。此前为稳定性曾强制回退为圆环，
> 该限制已在 V0.2 解除。
>
> **默认样式 = Headshot**（V0.2 后续迭代起，此前为圆环）。
>
> **命名**：设置页上的样式项叫 `Headshot`（取自 CS 的爆头图标），早期文档写作「八角星」——
> 同一个样式，造型仍是八角星；代码里对应 `MenuStyle.CSHeadshotOctagon`。
> ⚠️ `MenuStyle` 枚举按**整数**写进 `config.json`
> （`BasicRadial=0 / CSHeadshotOctagon=1 / SpiderWeb=2 / Bagua=3`），
> 因此**只能改显示名或默认值，不能插入/调换枚举成员**，否则老配置的样式会错位。
> 改默认值也只影响「新建配置 / 恢复默认」，已有配置里用户存的选择不会被改写。

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

当前版本（V0.2 起）：
- 4 种样式**全部可选**，`SettingsWindow` 的「菜单样式」选项直接决定实际渲染
- `RadialMenuFactory` 按 `AppConfig.MenuStyle` 返回对应实现，不再做回退

> 历史：曾有一段时间为稳定性，`SettingsWindow` 强制菜单样式为 `BasicRadial`、
> `RadialMenuFactory` 对非 `BasicRadial` 输入一律回退。该限制已在 V0.2 解除。

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

### 5.7 目标窗口漂移（V0.2 修复）
**问题**：`WindowManagementService` 内部各处均调用 `GetForegroundWindow()` 实时取窗口。
从中键按下到松开之间隔了用户选择扇区的时间，若期间前台窗口发生变化
（通知弹窗、输入法窗口、菜单自身抢焦点），操作会打到错误的窗口上。
若用户先把鼠标移到后台窗口上再按中键，则必然操作错对象。

**解决**：在中键按下的瞬间锁定目标窗口，全程传递。
1. `MouseHookService` 用 `WindowFromPoint(鼠标位置)` + `GetAncestor(GA_ROOT)` 解析顶层窗口；
2. 过滤桌面（`GetDesktopWindow`）、任务栏（`Shell_TrayWnd` / `Shell_SecondaryTrayWnd`）
   与自身进程窗口，命中过滤条件时回退到前台窗口；
3. 通过 `MouseHookService.TargetWindow` 暴露，`App.OnActionSelected` 取出后
   传给 `WindowManagementService.ExecuteAction(action, hwnd)`；
4. 解析失败或窗口已销毁时，`ResolveHandler` 回退到 `GetForegroundWindow()`。

### 5.8 分屏阻塞（V0.2 修复）
**问题**：`MoveWindowCompensated` 在 `ShowWindow(SW_RESTORE)` 后 `Thread.Sleep(50)`
等窗口状态更新。该调用发生在 UI 线程，每按一次中键分屏即阻塞界面 50ms。

**解决**：`SW_RESTORE` 本身是同步生效的，去掉固定等待；
移动改用 `SetWindowPos` + `SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS`，
避免跨进程窗口操作时的同步阻塞。

### 5.9 高 DPI 坐标错位（V0.2 修复）
**问题**：项目原先没有任何 DPI 感知声明，进程运行在 DPI 虚拟化模式下，
`SystemParameters.WorkArea`（WPF 逻辑单位）与 `SetWindowPos`（物理像素）
之间在 125% / 150% 缩放下系统性错位。

**解决**：
1. 新增 `app.manifest`，在 csproj 中通过 `<ApplicationManifest>` 引入，
   声明 `PerMonitorV2` DPI 感知；
2. 工作区改由 `MonitorFromWindow` + `GetMonitorInfo` 获取（`rcWork` 已排除任务栏），
   直接得到物理像素；两个 API 都失败时回退到 `SystemParameters.WorkArea`；
3. 菜单窗口增加 `PhysicalToLogical` 换算（经 `PresentationSource.CompositionTarget.TransformFromDevice`），
   鼠标钩子报告的物理像素统一换算为 WPF 逻辑坐标后再做定位与命中判定；
4. 注意：`ShowAt` 中需先 `Show()` 以获得 `PresentationSource`，换算才有效。

### 5.10 鼠标跟随精度（V0.2 改进）
**问题**：菜单窗口用 `DispatcherTimer` 以 16ms 间隔轮询 `Control.MousePosition`，
存在最多 16ms 延迟与采样丢失，且持续空转。

**解决**：`MouseHookService` 在 `WH_MOUSE_LL` 回调中已能收到每次鼠标移动，
新增 `MouseMoved` 事件推送坐标；`App.OnGlobalMouseMoved` 在钩子线程收到后
用 `Dispatcher.BeginInvoke(DispatcherPriority.Input)` 切回 UI 线程更新高亮。
菜单窗口的轮询定时器已移除，窗口内 `MouseMove` 保留为兜底路径。

### 5.11 命中判定：方向优先，不设半径上限
**问题**：四种样式原先都用「**内死区 + 外上限**」判定（Headshot `0.40R~1.02R`、
圆环 `InnerRadius~OuterRadius`、蛛网 `0.15R~1.1R`、八卦 `0.35R~1.1R`）。
指针一旦越出外上限就返回 `null`，高亮随即消失 —— 手往外一甩就丢选，
径向菜单该有的「往哪边甩就选哪边」手感不成立。

**解决**：`RadialMenu.GetSelectedItem` 的契约改为**只按方向选**：
指针在图形之内、之外、甚至屏幕另一角，都返回其方向对应的扇区；
内侧死区一并取消（Headshot 圆心那块骷髅、八卦的太极图区域同样参与选择）。

**唯一保留的例外**是圆心：那里方向无定义（`atan2(0,0)` 恒为 0°），返回 `null`。
它必须挡住，有两个实打实的理由：
1. 菜单是**以指针位置为中心**弹出的，弹出首帧指针恰好就在圆心，
   若返回扇区，菜单一出现就会闪出一格高亮；
2. 「按下中键但不移动就松开」应当等于什么都没选，
   若圆心返回扇区就会误触发一个动作。

该保护以归一化半径表示为 `RadialMenu.HitMinDistance = 1e-6`
（半径 90px 时约 1e-4 px），是**数值保护而非体验意义上的死区**。

配套回归：`Tools/MenuProbe` 从 44 项扩到 **208 项** —— 每种样式覆盖
「图形内八方向」「近圆心 0.05× 半尺寸」「图形外 1.1× / 1.5× / 3× / 10× 半尺寸」
「圆心必须返回 null」四类断言。

### 5.12 高亮更新路径收敛为唯一入口

**问题**：判定虽然统一了，但**触发判定的路径有三条**，各自喂进不同的坐标系：

| 路径 | 坐标 | 高亮状态 |
|---|---|---|
| 全局钩子 `MouseMoved` → `UpdateHighlightFromScreen` | 物理屏幕坐标 → 换算 | `MenuOverlayWindow._highlightedPosition` |
| 窗口内 `OverlayCanvas.MouseMove` | Canvas 坐标 → 换算 | 同上 |
| ~~`RadialMenu.OnMouseMove`~~ | `e.GetPosition(菜单)` | **菜单类自己的 `_highlightedPosition`** |

第三条（基类里那份「兜底」判定）**绕过覆盖窗口的状态记录**，直接调 `HighlightItem`
用 `e.GetPosition(this)` 算出的扇区。于是「当前高亮是哪个扇区」有**两份真相**，
`ExecuteAction` 读到的未必是屏幕上显示的那个；两条路径交替落笔，
画面上就是高亮在两个扇区之间稳定乱跳。

**真机取证**（`%APPDATA%\WinLoop\log.txt`，2026-09-17）：

- 三次会话（13:36:23 / 13:38:02）**全部执行 `Position3 → RightTwoThirds`**，
  而当时指针分别在 `(1561,17)`、`(1438,19)`、`(1313,156)` —— 相对菜单中心
  应是 P2 / 圆心 / P6，**明明各不相同**；
- 逐点复算确认：把指针的**绝对屏幕坐标**当菜单坐标喂进判定时，
  1920 宽的屏幕上指针永远落在左半偏中，相对原点 `(0,0)` 的射线恒指向右侧
  ⇒ **恒为 Position3**；
- 佐证：当天八个扇区的分布里 **P1/P7/P8 完全消失**（前一天还是均衡的 180~759 次），
  也印证"漏减菜单中心"这一特征。

**解决**：

1. **删除 `RadialMenu.OnMouseMove` / `OnMouseUp`** 里的判定与 `OnItemSelected` 事件
   （事件全项目无订阅者，是死代码）。基类只保留 `SelectSectorByDirection`
   与抽象的高亮绘制方法 —— 子类只负责「高亮画成什么形状」，不负责「什么时候高亮」。
2. **菜单中心改由实际布局位置推导**（`MenuOverlayWindow.MenuCenterInCanvas`）：
   读 `Canvas.GetLeft/GetTop(_currentMenu)` 加半径，而不是复用 `ShowAt` 里那条
   「屏幕坐标 → DPI 换算 → 减虚拟屏幕原点」的推导链。
   后者任一环节在非 100% 缩放或多屏下出偏差，判定就会整体偏掉。
3. 菜单控件设 `IsHitTestVisible = false`：指针压在菜单图形上时事件不再被它吃掉，
   窗口内 `MouseMove` 这条兜底路径在菜单范围内也保持有效。

**回归**：`Tools/MenuProbe` 扩到 **917 项**，其中坐标换算一节直接采用真机日志的
三个现场坐标，并**显式验证「漏减中心会让任意指针位置都算出 Position3」** ——
保证这条用例真的抓得住该 bug，而不是空断言（采样点特意不用正右方：
那个位置漏减中心后恰好也得到 P3，区分不开两条路径）。

### 5.13 窗口动作执行不得依赖前台（P5 启动初期失效）

**问题**：软件启动后的一两分钟内，P5（默认「最小化窗口」）不生效 ——
高亮正常显示 P5，但目标窗口纹丝不动；过一两分钟后又 100% 可用。

**根因**：`WindowManagementService.MinimizeWindow` 用的是

```csharp
PostMessage(hwnd, WM_SYSCOMMAND, SC_MINIMIZE_, IntPtr.Zero);   // ❌
```

`WM_SYSCOMMAND` + `SC_MINIMIZE` 是**「用户主动点了标题栏最小化按钮」的语义**。
Windows 会校验消息发起者与目标窗口的前台关系；不是用户直接点，就要求
目标窗口处于前台。而 `MenuOverlayWindow.ShowAt` 里执行了 `Activate()` /
`Focus()` / `Mouse.Capture()`，前台早已被覆盖窗口抢走 ⇒ 消息被静默忽略。
`PostMessage` 本身还是**异步**的，返回值也不反映处理结果，所以日志里
`Executing action: Minimize, hwnd=...` 照常打印，看不出任何异常。

启动初期还在 **`ForegroundLockTimeout`（默认 200000 ms）保护期内**，
系统对前台切换的校验更严；保护期一过，同一份代码又能用了 ——
这就是「过一两分钟自己好了」的由来。日志确认动作**每次都执行了**，
与用户描述的「高亮正常但窗口没最小化」完全吻合。

**对照**：同文件的兄弟动作早就是前台无关写法 ——
`MaximizeWindow` 用 `ShowWindow(hwnd, SW_MAXIMIZE)`，
`ToggleMaximizeWindow` 用 `ShowWindowAsync(hwnd, SW_RESTORE/SW_MAXIMIZE)`。
只有最小化这一个动作踩了坑。

**解决**：改用与兄弟动作一致的写法，**直接设置窗口状态**，不走用户主动语义：

```csharp
ShowWindowAsync(hwnd, SW_MINIMIZE);   // ✅ 不依赖前台
```

同时删掉随之变为死代码的 `SC_MINIMIZE_` 常量。
`CloseWindow` 保留 `SC_CLOSE` 是**有意为之**（关闭要让目标自行处理保存提示），
不在本约束范围内 —— 回归里专门钉住这个例外，防止被「顺手统一」掉。

**回归**：`Tools/MenuProbe` 新增静态契约检查（源码扫描），
断定 `MinimizeWindow` / `MaximizeWindow` / `ToggleMaximizeWindow` 的**可执行代码**
中不得出现 `WM_SYSCOMMAND` / `SC_MINIMIZE`，且必须有 `ShowWindow*` 调用；
注释里出现这些词是允许的（用来说明为什么不能用），扫描前先剥注释。
断言 `CloseWindow` 仍保留 `SC_CLOSE`。总数 **917 → 921 项**。

> 经验：**凡是「用户语义」的窗口消息，都不要替用户发。**
> 自己发就会遇到前台校验。要改窗口状态就用 `ShowWindow*`。

### 5.14 中心死区：贴合各样式图形内缘

**需求**：指针退回圆心附近一片区域就取消高亮、松手也不触发动作。

**约定**：死区边界**不是统一的半径比例**，而是**各样式自己图形的内缘** ——
圆环就是内圆之内、八卦就是太极范围、蜘蛛网就是中心第一个八边形、
Headshot 的八角星星形就是底层同心圆的内圆之内。这样死区形状永远与画出来的图形严丝合缝，
用户改设置里的「大小」时死区也跟着缩放。

**实现**：内缘是各样式自己的几何，基类算不出来，所以新增一个由子类报告的属性：

```csharp
// RadialMenu（基类）
public virtual double CenterDeadZoneRadius => 0.0;   // 返回 0 = 无死区
```

各子类的取值：

| 样式 | 死区内缘 | 表达式 | 默认占半径 |
|---|---|---|---|
| 圆环 `BasicRadialMenu` | 内圆 | `OuterRadius − Thickness` | 0.56 |
| Headshot `CSHeadshotMenu` | 底层同心圆内圈的外缘 | `_scale × RingInnerHigh`（0.6590） | 0.637 |
| 蜘蛛网 `SpiderWebMenu` | 中心第一个八边形 | `_OuterRadius / (Rings + 1)` | 0.227 |
| 八卦 `BaguaMenu` | 太极图 | `_OuterRadius × TAICHI_RADIUS_SCALE`（0.32） | 0.267 |

判定里把原先的"圆心退化点"扩成这圈死区：

```csharp
double distance = Math.Sqrt(dx * dx + dy * dy);
double deadZone = CenterDeadZoneRadius;
if (deadZone <= 0) deadZone = HitMinDistance * VisualRadius;   // 兜底
if (distance <= deadZone) return null;
```

`HitMinDistance` 保留为**兜底**（子类没报告有效值时仍挡住 `atan2(0,0)` 的退化点），
但已不再是主机制。

**两个必须注意的同源点**：

1. **Headshot 用 `_scale` 而不是 `VisualRadius`** —— 同心圆是用 `_scale` 的矩阵画的，
   而 `VisualRadius` 另乘了 `MaxRadius × EXTENT_MARGIN`（≈1.0346）。用错会让死区偏小 3.5%。
2. **八卦的 `0.32` 提成常量 `TAICHI_RADIUS_SCALE`** —— 绘制（`DrawTaiChi`）与死区
   共用这一个来源，不会出现"死区与画出来的太极不一样大"。
3. **圆环的 `InnerRadius` 与 `OuterRadius` 是两个独立输入框** —— 用户只调大外径时
   环带本来就变厚、死区跟着变大是**正确行为**，不要写成"死区/半径比例恒定"的断言。

**回归**：`Tools/MenuProbe` 921 → **897 / 0**（总数下降是因为 `CheckDirectionUnification`
的采样档位从"写死 0.5×"改成"各样式死区之外 ×1.05"，档位数虽仍为 2，但那是**同一批断言换了采样点**；
真正的变化是新增了死区验证）。新增/改写三处：

- `CheckDeadZone`：死区内（圆心、0.5×、0.98× 三档 × 八方向 = 24 项）必须全为 `null`，
  死区外紧邻处八个方向必须立刻恢复选中 —— **钉住"子类报告了半径、基类真的用上了"**；
- `CheckCustomSize` 里的死区判据**按样式分两类**：圆环验证「死区 == 外径 − 厚度」这个定义式，
  其余三种验证「归一化比例恒定」（防止有人把内缘写成写死的像素值）；
- `CheckDirectionUnification` 的低档采样从写死的 `0.5` 改为 `CenterDeadZoneRadius × 1.05 / VisualRadius` ——
  否则死区大的样式（圆环 0.56、Headshot 0.637）会采到死区里，得到 `null` 而误报失败。

> ⚠️ 反过来说：这个改动**会让菜单的有效选择区显著缩小**。
> 圆环默认死区占了半径的 56%，可操作区只剩最外那一环。
> 若体感太紧，调小圆环的 `InnerRadius`（设置面板里有独立输入框）即可 ——
> 死区会跟着内圆一起变小，不需要改代码。

### 5.15 尺寸单位：DIP（设备无关像素）

**问题**：尺寸配置本身就是 DIP。只要不额外做 DPI 运算，WPF 就会按本屏 DPI
自动把它换算成物理像素 —— 这才是"同一份配置在任何缩放下看起来一样大"的正解。
（早期在这里**额外**乘过一次「本屏 DPI / 96」，反而是双重缩放，已废弃，见下。）

**方案**：尺寸字段的语义就是「**DIP（设备无关像素）**」，
菜单渲染**直接使用**该值 —— WPF 会按本屏 DPI 自动把它换算成物理像素。

程序在 `app.manifest` 里声明了 Per-Monitor V2 DPI 感知，所以：

| 缩放 | 1 DIP 对应 | 「半径 90」的物理尺寸 |
|---|---|---|
| 100% | 1.0 px | 90/96 ≈ 0.94 英寸 |
| 125% | 1.25 px | 同上 |
| 150% | 1.5 px | 同上 |
| 175% | 1.75 px | 同上 |
| 200%（4K 常见） | 2.0 px | 同上 |

**物理尺寸只由配置值决定，与 DPI、分辨率都无关** —— 这正是 DIP 的定义，
也是用户换机器 / 改缩放后菜单"看起来一样大"的保证。

**只追随 DIP，不追随分辨率。** 分辨率本身不改变"界面元素该多大"这个用户偏好 ——
4K + 100% 缩放的用户就是想要小元素、大工作区，按分辨率强行放大等于夺走他的选择。

> ⚠️ **曾经的做法（已废弃，是个 bug）**：早期把配置值再乘一次「本屏 DPI / 96」，
> 理由是"否则 200% 的 4K 屏上菜单只有 1080p 的四分之一大"。
> 这个推理把 DIP 当成了物理像素 —— WPF 渲染时**已经**按 DPI 换算过一次，
> 再乘一次就成了**双重缩放**：175% 缩放下菜单被撑到 1.75 倍，肉眼可见地"特别大"。
> 修正见 `RadialMenu.Scaled` 的注释与 `Tools/MenuProbe` 的尺寸基准用例。

**实现落点**：

- `Menus/RadialMenu.cs`：`protected double Scaled(double baseValue)` 是**唯一的尺寸入口**
  （子类读任何半径/粗细都必须经过它）。它现在**是恒等函数** —— 配置值就是 DIP。
  保留这一层是为了将来若要引入真正的整体缩放，只改这一处。
- `Models/AppConfig.cs`：尺寸字段直接就是 DIP；**不再有** `SizingScale` 运行时字段。
- `Models/SizingScale.cs`：只保留 `FromVisual(Visual)`（用
  `PresentationSource.FromVisual(...).CompositionTarget.TransformToDevice.M11`
  取本屏缩放比），**仅供设置面板把「半径 90」讲成「本屏约 158px」给人看**，
  不再参与任何菜单尺寸计算。
- `UI/MenuOverlayWindow.xaml.cs`：`ShowAt` 只把本屏缩放比记进日志（诊断用），
  不做任何尺寸换算。
- `UI/SettingsWindow.xaml.cs`：设置面板预览与运行时**共用同一套 DIP 基准**，
  因此"预览里看到多大，弹出来就多大"。

**配置兼容（v1 → v2）**：

老 `config.json` 里没有单位标记，反序列化后 `SizingUnitVersion = 0`（= v1）。
v1 与 v2 在 100% 缩放时**数值完全等价**（1 DIP = 1 物理像素），
因此 `ConfigManager.MigrateSizingUnit` **只补标记、不动任何数值** ——
用户的 50/28/70/90 原样保留，含义统一为 **DIP**：
WPF 按本屏 DPI 自动换算成物理像素，换机器 / 改缩放都是同一个视觉大小。

> ⚠️ `ConfigDto.SizingUnitVersion` **不能**给非 0 默认值 ——
> 给了老配置就会被误判成"已迁移"，`AppConfig` 侧的默认值是 `SizingUnitCurrent`，
> 两者分工不同：DTO 负责"如实读出文件里有没有"，AppConfig 负责"新建时就是新版"。

**回归**：`CheckDpiScaling`（尺寸基准用例）覆盖 4 种样式，验证
① `VisualRadius` 严格等于配置基准（尺寸里**不含任何 DPI 因子**）、
② 物理尺寸换算后在各 DPI 档位（1.0 / 1.25 / 1.5 / 1.75 / 2.0）下恒定、
③ 配置整体放大一倍时 VisualRadius 与中心死区**同比例**跟随、
④ 方向判定与尺寸无关（两份尺寸 × 72 点逐点一致）。
当前总数 **917 / 0**。

**仍不做（后续版本优化目标）**：多屏混合缩放下 `MenuOverlayWindow` 的坐标错位 ——
`SystemParameters.VirtualScreen*` 是物理像素，被直接赋给了期望逻辑单位的
`Window.Left/Top/Width/Height`，跨不同缩放的显示器时菜单中心会偏。
单屏（含单屏 4K）不受影响。

### 5.16 设置界面：行流结构（参照 PixPin）

**问题**：设置界面原先是"每块内容一张小卡片"，每页 2–5 张卡各自为战：
行高不统一（26 与 auto 混用）、无行间分割线、单卡撑不满时右侧与底部大片留白、
页级标题与卡内标题层级含混。整体观感零散。

**参照物**：PixPin 配置窗口。它的核心不是配色或圆角，而是**用"行"当基本单位**：

| 维度 | PixPin | 重构后 |
|---|---|---|
| 页面结构 | 页级大标题（卡外）+ 单张大白卡 + 行流 | 同 |
| 表单行 | 等高，左右两端对齐，行间 1px 淡线 | 同 |
| 标签列 | 固定左边界的视觉竖线 | `FormLabelCol = 104` |
| 行总高 | 约 44（控件 30 + 上下留白 7+7） | 同（`FormRowH=30` + `FormRowPadV=7`） |
| 左导航 | 窄栏 + 实心块选中（蓝底白字） | 同（150px + `NavActiveBg`） |
| 色块 | 内嵌于按钮内的小方块 + 文字 | 同（`ColorSwatchButton` 内嵌 14×14 chip） |
| 底部 | 左下"恢复所有默认设置" + 右下确定/取消 | 同 |

**实现落点**：

- `SettingsWindow.xaml` 设计令牌层新增：`RowLine`（`#EDEDED`）、`NavActiveBg`、`NavHoverBg`；
  `FormRowH` 26 → 30，`FormLabelCol` 92 → 104
- 新增样式：`SettingRow`（行容器，统一 `Margin="0,7"`）、`RowSeparator`（1px 分割线）、
  `RowHintStyle`（行内右侧说明文字）、`GroupHeaderStyle`（卡内组标题）、
  `DangerGhostButton`（左下恢复默认）
- 四个页面重排为「页级标题 → 单卡 → 组标题 + 行流」，组间用分割线 + 组标题换气
- 左导航选中态由「细竖条 + 淡蓝底」改为**整块实心蓝 + 白字**
- 内容区滚动容器加 `VerticalAlignment="Top"` —— 否则 `StackPanel` 会被拉伸，
  卡片底部留下大片空白（这是"消除了留白"的关键一行）

**踩坑记录（均为编译期查不出、必须运行时验证）**：

1. **`ControlTemplate` 只能有一个直接子级**。给 `NavItemStyle` 的 `Border` 里补
   `ContentPresenter` 时，误把 `ContentPresenter` 留在了 `Border` 外面 →
   编译期报 `MC3089`（这个侥幸被编译器抓到了）。
2. **`ComboStyle` 自带 `Margin` 是重叠元凶**。原 `Margin="0,6"` 与相邻元素的
   上边距叠加，在行距紧的地方直接把提示文字顶到重叠。
   现约束：**行内控件一律不给自己加外边距**，行距统一由 `SettingRow` 控制。

**修掉的历史缺陷**：操作配置页 `CurrentPositionCombo` 与下方提示文字重叠。

**交互变化**：底部由「提示 + 恢复默认 + 保存」改为「恢复所有默认设置（左下）+
状态提示（中）+ 取消 / 确定（右下）」。取消语义天然成立 —— 本窗口原本就不在
`Closing` 里自动保存，只有点「确定」才写盘，因此取消不需要回滚逻辑。
保存 / 恢复默认后，中间状态文字显示 2.6 秒后自动淡出（`SetFooterStatus`）。

**未做（B 档）**：预览区缩放自适应、尺寸实时像素反馈。

### 5.17 预览自动适配与尺寸实时反馈（B 档）

**问题 1｜预览被裁切**：预览画布固定 280×280，而半径输入框没有上限。
半径超过约 130 逻辑像素后，菜单图形会超出画布被切掉 ——
用户看不到完整轮廓，也就失去了"预览"的意义。

**做法**：`ApplyPreviewFit()` 按「能完整容纳」的比例整体缩放渲染，规则：

```csharp
const double pad = 8.0;                          // 防止描边贴边被切半个像素
double fit = Math.Min(availW / menuWidth, availH / menuHeight);
if (fit >= 1.0) fit = 1.0;                       // 只缩小，不放大
if (NaN/Inf/fit <= 0.01) fit = 0.01;             // 极端值保护，避免整个图形消失
_previewMenu.RenderTransform = new ScaleTransform(fit, fit);
_previewMenu.RenderTransformOrigin = new Point(0.5, 0.5);   // 围绕中心缩放
```

**关键设计**：
- **只影响显示，绝不回写用户填的数值** —— 用户填 200 就是 200，预览只是"看得见"
- **只缩小不放大**：半径很小时保持原尺寸，用户才能直观感到"我改小了"
- 缩放用 `RenderTransform` 而非改坐标：位置仍按原始尺寸居中计算，居中性不受影响
- 缩放比例标注在画布下方（`PreviewFitLabel`），100% 时清空不显示多余文字

**验证**：临时把画布改成 160 跑一遍，日志 `menu=216.0x216.0 canvas=160.0x240.0 fit=0.667`
与手算 `(160-16)/216 = 0.667` 严格一致，截图确认八卦完整可见 + 标注「预览按 67% 缩放显示」。

**问题 2｜尺寸没有实际像素反馈**：用户填的是"逻辑像素 @100% 基准"，
但在自己这块屏上究竟多大，只能靠脑补。

**做法**：`RegisterSizeField()` 把尺寸输入框登记进 `_sizeFields` 表并挂 `TextChanged`，
实时刷新 Tooltip：

```
圆环外半径
逻辑像素：200
本屏缩放：150%
实际显示：约 300px
```

**问题 3｜越界无提示**：`MaxRecommended` 是"预览画布能完整容纳"的经验上限
（半径类 130，内半径 120，见画布 280 的一半）。超出时**只把输入框描边染成
橙色 `#FFD08A3A` 并追加警告文案，不阻止输入** —— 高级用户可能确实想要大半径，
我们不能替他做决定。这属于"软提醒"。

**验证工具注意**：截图脚本 `tmp/btour*.py` 依赖 `SetCursorPos` 模拟点击，
在某些会话状态下会被系统拒绝（`pywintypes.error (0, 'SetCursorPos')`）。
遇到时改用「临时改代码参数 + 读日志」的方式验证逻辑，比模拟鼠标更可靠。

### 5.18 颜色选择器：自绘取色面板（参照 PixPin）

**背景**：原先两处取色都直接调系统 `System.Windows.Forms.ColorDialog`。
它有两个硬伤：一是观感与设置窗口完全脱节（系统灰底方角，跟 PixPin 风格的
白卡行流像两个软件）；二是交互差 —— 只能在 RGB/HSV 分页里试数，
没有二维调色板，取一个"浅一点的蓝"要点很多次。

**做法**：新建 `ColorPickerWindow`（340 宽，`SizeToContent=Height`）。
布局自上而下：① SV 二维调色板 ② 色相彩虹滑杆 ③ 格式下拉 + 分量输入行 ④ 取消/确认。

**范围界定（与用户确认）**：
- **不做屏幕吸管** —— 需全局热键 + 截屏取色，工作量大，
  且可能与 WinLoop 自身的热键机制冲突。PixPin 输入行最左侧那个位置留空，
  不塞多余控件充数。
- **只输出 6 位不透明色** —— 渲染层与配置字段都按 `#RRGGBB` 处理，
  不给渲染链路引入 alpha 复杂度。

**关键实现点：**

**① 调色板尺寸必须运行时实测，不能硬编码。**
最初把调色板宽度写死 `312`（= 340 − 2×14 padding），但**窗口宽度包含客户区边框**，
实际可用只有 **296**。后果是右侧 16px 被 `ClipToBounds` 裁掉，
且标记位置计算 `_sat * 312` 与实际渲染宽度不符，**最右侧一整条取不到纯饱和色**。
改为在 `Loaded` 里读 `SvCanvas.ActualWidth/ActualHeight`：

```csharp
private double _svW = 1.0, _svH = 1.0;
private void MeasurePalette()
{
    if (SvCanvas.ActualWidth > 1 && SvCanvas.ActualHeight > 1)
    { _svW = SvCanvas.ActualWidth; _svH = SvCanvas.ActualHeight; }
    HueBar.Width = HueCanvas.ActualWidth > 1 ? HueCanvas.ActualWidth : _svW;
}
```
XAML 里 Canvas 不设 `Width`，靠父级 `StackPanel` 撑满；三层视觉元素用
`Rectangle`（自动拉伸）而非固定宽 `Border`。
实测日志：`sv=296x212 hue=296 client=340x429.6`。

**② 格式切换：四个格式各参数独立成框，不挤在一格。**
`[模式下拉] [分量1] [分量2] [分量3]`，HEX 时切回单个宽框。
两个容器叠在同一格靠 `Visibility` 切换，避免整行宽度跳动。
HEX 用 `HexPanel`，其余用 `TriPanel`（三框 + 下方 `Comp1/2/3Label`）。

**③ 分量一律整数显示；靠「渲染单向」保证不漂移。**
（按用户要求：分量显示取整，且删掉模式下方的格式说明文字。）

先说风险：整数显示本身是**有损**的。
`#3A7BD5` 的真实 HSV 是 `214.8 / 72.8 / 83.5`，显示成 `215 / 73 / 84`。
如果实现成「切格式时读回显示串 → 解析 → 覆盖内部状态」，
那么每次切换都会把丢掉精度的近视值写回状态，
颜色**悄悄漂移 1 个色阶**（`#3A7BD5 → #3A7BD6`）。

规避办法不是提高显示精度，而是**切断反向路径**：
内部 `_hue/_sat/_val` 始终是唯一真相，切格式只做「状态 → 界面」单向渲染，
绝不从显示串反推状态。唯一会反向解析的时机是**用户在框里手输**，
那本来就是他主动改颜色，解析是预期行为。

```csharp
// 切格式：只渲染，不解析
private void ModeCombo_SelectionChanged(...)
{
    ...设定 _format...
    SyncFormatBox();      // 单向：状态 -> 界面
    FocusPrimaryInput();
}

/// <summary>分量显示：一律取整，不带小数位。</summary>
private static string Fmt(double v)
{
    return ((long)Math.Round(v, MidpointRounding.AwayFromZero))
        .ToString(CultureInfo.InvariantCulture);
}
```

**验证**：11 组代表色，每组依次切遍 `Hex→Rgb→Hsv→Hsl→Hsv→Rgb→Hex`
（每次都强制渲染），回读颜色必须与初始完全一致 —— **11 组样本，0 漂移**。
另有一组反向解析用例确认「手输整数写法」只保证每通道 ±1（这是整数输入的固有精度，非缺陷）。

**④ 三个分量框共用一个 `TextChanged`，且只把可见框当真相来源。**
三格各自触发事件，每次都用三格当前值整体重算，
这样改任意一格都能立即看到效果，三格之间也不会互相覆盖。
任一分量解析失败（用户还在输入中）就整体跳过，不打断输入。

**⑤ 提示文字全部去掉，改用分量名。**
原本下拉下方有一行随格式变的说明（"R / G / B 各 0–255。"等），
窗口显冗余。删掉后只在三个框正下方各贴一个字母（`R/G/B`、`H/S/V`、`H/S/L`），
贴着对应框，比整行文字更直接，也让窗口更紧凑。

**⑥ 校验器统一到渲染层同一套解析器。**
`SettingsWindow.ColorTextBox_LostFocus()` 原用
`System.Drawing.ColorTranslator.FromHtml` 校验，它的接受范围更宽
（例如 `rgb(255,0,0)` 也认），会放过渲染层 `ColorConverter` 解析不了的值，
一路存进配置后在绘制时抛异常。两者统一为 WPF `ColorConverter` 后堵上了这个口子。

**验证工具**：`Tools/CpTest` 用反射直接调窗口的私有解析/格式化方法做真值测试，
不依赖模拟键盘 —— `(` `)` `%` 这些符号键在无人值守环境用
`VkKeyScanA` 敲不可靠，测出来的是脚本的毛病而不是程序的。

**已知遗留**：PixPin 的取色面板支持屏幕吸管与透明度滑杆，本版本均未实现（见范围界定）。

### 5.19 设置窗口：固定尺寸与内嵌字体

本轮三项调整：① 移除左侧导航顶部的 "WinLoop" 应用名；② 窗口固定尺寸；
③ 整体字阶上移一档（正文 13→14、页标题 15→17），并把字体换成内嵌的 Noto Sans SC。

**① 移除导航顶部应用名。**
窗口标题栏已经写着 "WinLoop 设置"，左侧栏再重复一次是冗余。
去掉后导航项整体上移（`StackPanel` 改 `Margin="0,14,0,0"`），
第一项「菜单样式」与右侧页标题的基线更贴近。

**② 窗口固定尺寸：`ResizeMode="NoResize"`。**
一个 setter 同时达成两个目标 —— 拖边框改不了大小，最大化按钮也被系统置灰
（`NoResize` 隐含 `ResizeMode` 不含 `CanMaximize`）。
同时删掉 `MinWidth`/`MinHeight`：固定尺寸下这两个属性不再有任何作用，
留着只会让后来者误以为窗口还是可缩放的。

尺寸从 760×560 调到 **880×620**。原因是字阶上移后原尺寸不够用：
正文 13→14 字宽增加约 8%，行高 30→32 增加约 7%，
而左侧栏为了容纳变大的导航文字也从 150 加宽到 168。
三项叠加后 760 宽会让大部分页出现横向滚动条。

**③ 字体：内嵌 Noto Sans SC（随包分发）。**

字阶统一上移一档：

| 用途 | 样式键 | 原 | 现 |
|---|---|---|---|
| 正文 / 表单标签 / 输入框 / 下拉 | `LabelStyle`·`InputStyle`·`ComboStyle` | 13 | 14 |
| 弱化说明 | `HintStyle` | 11.5 | 12.5 |
| 分组小标题 | `GroupHeaderStyle`·`SectionTitleStyle` | 13 | 14 |
| 页标题 | `PageTitleStyle` | 15 | 17 |
| 按钮 / 导航项 | 各 Button Style·`NavItemStyle` | 13 | 14 |

配套的几何令牌同步放大：行高 30→32、行内上下留白 7→8、
标签列 104→108、输入框宽 78→84、卡片内边距 20,16→22,18、
各页滚动区边距 20,18→24,20。

**关于"文字飘在半空"的成因与修复。**
原先标签列的 `ColumnDefinition` 宽 104，而视觉上文字只占 40~60px，
右侧留出 40~60px 空白 —— 标签、输入框、单位说明三者被拉成一条稀疏横排，
眼睛找不到对齐锚点，观感上就成了"字浮在行里"。
修复分两步：
1. 标签列与输入框同步收窄比例，栅格本身更紧凑；
2. 正文提到 14 号、行高提到 32，让文字有实体感，不再是细线浮标。
另把 `LabelStyle` 的前景色从 `TextSub` 改为 `TextMain` —— 标签是行的主信息，
原先用次级灰会把视觉重心推到右侧输入框上。

**字体必须内嵌，不能只写字体名。**
只写 `FontFamily="Noto Sans SC"` 时，目标机没装该字体，WPF 会**静默回退**
到微软雅黑：不报错、不提示。结果是字宽、字间距、标题层级全部走形，
而开发者本机（装了字体）看到的一切正常 —— 典型的"在我机器上是好的"。
因此把 otf 作为 `<Resource>` 打进程序集，用 pack URI 引用。

字体文件放在 `Resources/Fonts/`，只带两个静态字重：

| 文件 | 内嵌家族名 | 用途 |
|---|---|---|
| `NotoSansSC-Regular.otf` | `Noto Sans SC` | 正文（`FontWeight="Normal"`） |
| `NotoSansSC-Medium.otf` | `Noto Sans SC Medium` | 强调（`FontWeight="Medium"`） |

Bold 不带 —— 界面里没有任何 `FontWeight="Bold"` 引用，省 8.5MB。

**踩坑一：pack URI 的解析基准是「入口程序集」。**
最初用独立的探针程序（Tools/FontProbe）引 WinLoop.dll 去验证字体，
所有候选写法**全部失败**、一律回退到 Arial。
原因是 `pack://application:,,,` 的基准是**入口程序集**，
探针进程的入口是 FontProbe.exe，它去 FontProbe 里找
`Resources/Fonts/` 当然找不到。加了 `;component` 限定程序集名也没用
—— 这条路的基准偏移不在 URI 里，而在宿主进程上。

结论：**内嵌字体的验证只能在真实入口（WinLoop.exe）里做**。
为此在 App 增加了 `--fontcheck` 开关（见 5.19 末），
把自检挂在真实入口上。这也是本轮唯一可信的验证手段 ——
用外部程序测出来的全是假阴性。

**踩坑二：Medium 字重不会自动映射到 SemiBold。**
两个 otf 的内嵌家族名并不相同（`Noto Sans SC` vs `Noto Sans SC Medium`），
把两个 pack URI 并列写进 `FontFamily` 只会得到两个独立家族，
`FontWeight="SemiBold"` 在第一个家族里找不到更粗的字面，
**落到 Regular** —— 标题和正文视觉上完全一样。

自检抓到了这一条：

```
[字重] Normal    -> 字面 'Regular'
[字重] Medium    -> 字面 'Medium'
[字重] SemiBold  -> 字面 'Regular'   <== 标题不会变粗
```

试过在 URI 里用 `#Noto Sans SC, Regular, Medium, SemiBold` 声明字重，
`SemiBold` 依然落 Regular —— WPF 的字重匹配在这个只有
400/500 两档的家族里，对 600 的请求会取到 400 而不是就近的 500。
最终采用最直接的写法：

```xml
<FontFamily x:Key="UiFontFamily">pack://application:,,,/Resources/Fonts/#Noto Sans SC, Regular, Medium; Microsoft YaHei UI, Segoe UI</FontFamily>
```

界面里凡是要强调的地方**统一写 `FontWeight="Medium"` 而不是 `SemiBold`**。
校验结果：

```
[字重] Normal -> 'Regular'    [字重] Medium -> 'Medium'    PASS
```

**字体自检开关（`--fontcheck`）。**
```
WinLoop.exe --fontcheck [输出文件路径]
```
在真实入口里打印并断言五项：① 字体资源已编入程序集；② `UiFontFamily`
资源存在；③ 解析到的家族名含 `Noto`（未回退到系统字体）；
④ `Medium` 与 `Regular` 命中不同字面（强调文字能与正文区分）；
⑤ 界面实际用到的 159 个字符全部有字形（不缺字、不显示方框）。

这项自检值得保留：它抓到的两个问题（家族名不一致、字重回退）
**编译期查不出来、截图上肉眼也看不出来**，只有断言能拦住。

**代价**：`WinLoop.dll` 从 1.3MB 涨到 **17.2MB**（两个 otf 共约 17MB，
以压缩形式存在 `WinLoop.g.resources` 里）。单文件发布包相应从 2.9MB
涨到约 20MB。这是"任何设备上渲染一致"的必然代价，已确认接受。

**回退链**：pack URI 后仍挂了 `Microsoft YaHei UI, Segoe UI`。
万一将来发布时资源被裁掉或路径失效，还能退回一个合格的中文界面字体，
不至于显示成方框 —— 属于防御性兜底，正常路径下不会走到。

### 5.20 XAML 样式类型不匹配：编译期查不出的崩溃

**现象**：设置窗口完全打不开，托盘双击与菜单项均报同一句：

```
Tray open settings error: 设置属性"System.Windows.FrameworkElement.Style"时引发了异常。
```

**根因**：`Style` 的 `TargetType="CheckBox"` 被套到了 `RadioButton` 上。
WPF 在**套用样式的那一刻**校验 `TargetType`，不匹配立即抛异常。

**为什么难查**：这句话没有 Style 名、没有控件名、没有行号，也没有
`InnerException`。它只说"设置 Style 属性时出错"，从日志直接看不出
是哪个 Style、哪一行。编译期完全正常 —— 生成成功、0 错误。

**修复**：`CheckStyle` 拆成两份类型正确的样式，外观相同但类型各自匹配：

| 样式键 | TargetType | 用途 |
|---|---|---|
| `CheckGlyphTemplate` + `CheckStyle` | `CheckBox` | 开机启动 / 最小化到托盘 / 启用悬空寺 |
| `RadioGlyphTemplate` + `RadioStyle` | `RadioButton` | 样式选择（圆环/Headshot/蜘蛛网/八卦） |

⚠️ `ControlTemplate` **同样不能跨类型复用** —— `TargetType="CheckBox"` 的模板
不能挂到 `RadioButton` 上。所以两处模板是**复制**而非引用。
代码注释里已标注"改外观时两处同改"。

**两道防护**（这是本次事件真正的产出）：

**① 运行时自检：`WinLoop.exe --settingscheck [输出目录]`**

```
PASS: SettingsWindow 构造成功（XAML 可正常解析）
PASS: 窗口已显示，实际尺寸 880x620（期望 880x620）
PASS: ResizeMode = NoResize（不可缩放、不可最大化）
共 4 个页面
PASS: 页 1（菜单样式）已渲染，可见控件 23 个 -> page1.png
...
结果: ALL PASS
```

实现要点：窗口挪到屏外 `(-32000, -32000)` 构造并 `Show()`（离屏不 `Show`
拿不到真实布局），遍历 `TabItem` 切页，逐页 `RenderTargetBitmap` 存 PNG，
并统计可见控件数作为"这一页真的渲染出东西了"的粗粒度证据。
构造失败时打印 `GetType().FullName` + `Message` + `InnerException` ——
比日志里那句无上下文的话有用得多。

**② 静态扫描**：比对每个 `Style="{StaticResource KEY}"` 所在元素的类型
与 `KEY` 的 `TargetType`，并检查 `Style` 内嵌 `ControlTemplate` 的
`TargetType` 是否与外层一致。可在改动 XAML 后快速回归。

**结论（写进项目铁律）**：
> XAML 的 `TargetType` 不匹配是**编译期查不出的运行时错误**，报错信息没有上下文。
> 因此改完 XAML **必须**跑 `--settingscheck`，编译通过 ≠ 界面能打开。
> 需要两种控件共用外观时，**必须写两份类型正确的 Style + Template**，不能复用一份。

### 5.21 色值框隐藏化：让色块按钮直接显示色值

**需求**：设置页原本每处配色是「标签 + 色块按钮 + 外层只读色值文本框」三件套，
视觉上色值飘在按钮右边、与按钮分离。需求是**色值直接显示在色块按钮上**，
不再单独占一个框。

**方案**：**隐藏文本框而非删除**。

```xml
<Button Name="BasicRingColorPickButton" Style="{StaticResource ColorSwatchButton}"
        Content="{Binding ElementName=BasicRingColorBox, Path=Text}"
        Tag="{Binding ElementName=BasicRingColorBox, Path=Text}"/>
<TextBox Name="BasicRingColorBox" Text="#FFFFFF"
         Width="0" Height="0" Visibility="Collapsed"
         IsTabStop="False" Focusable="False"/>
```

**为什么隐藏而不是删除** —— 这些 TextBox 是**真值载体**，C# 里有约 10 处引用：

| 引用点 | 作用 |
|---|---|
| `ColorTextBox_LostFocus` | 手输色值后校验并写配置 |
| `PopulateUIFromConfig` | 从配置读色值填进界面 |
| `SaveConfig` | 保存时从 TextBox 取值写回配置 |
| `PickColorInto` | 取色器确认后回填 |
| `UpdatePreview` | 预览渲染时读当前色值 |

删掉要动一整片逻辑，隐藏的成本最低且行为完全不变。
按钮的 `Content` 与 `Tag` 都绑到同一个 TextBox 的 `Text` 上
（`Tag` 供现有 `CountColorButtons` 自检按 `#` 前缀识别色块按钮）。

⚠️ **两个属性不能省**：`IsTabStop="False" Focusable="False"`。
否则 Tab 键会把焦点送进 0×0 的不可见控件，界面上表现为"焦点消失"。

**涉及五处**：`BasicRingColorBox` / `BasicHighlightColorBox` /
`SpiderWebLineColorBox` / `SpiderWebHighlightColorBox` / `BaguaLineColorBox`。

**配套调整**：`ColorSwatchButton` 的 `MinWidth` 138→146、`Padding` 9,0→10,0
（多了色值文字），ToolTip 改「点击打开取色器」。

**顺带修复行为不一致**：`BasicHighlightColorPickButton_Click` 原本还在用
系统 `System.Windows.Forms.ColorDialog`，而圆环颜色早已换成自绘的
`ColorPickerWindow`。现已统一 —— 同一界面里两种取色体验是明显的割裂。

**自检扩展**：`--settingscheck` 从「四页截图」扩到「四种菜单样式变体截图 +
色值同步断言」。关键在于 `CheckColorTextSync` 会**真的写一个探针色值**
进隐藏文本框，再读按钮的 `Content` 看是否同步：

```
box.Text = "#1A2B3C";  // 探针
// 断言 btn.Content as string == "#1A2B3C"
```

这条断言才是"绑定真的通了"的证据 —— 仅截图看不出绑定是否双向生效。

> 注：Headshot 页没有配色行（只有尺寸滑动条），因此该页报
> `WARN: 没有可见的色块按钮可测`，属**预期行为**而非缺陷。

### 5.22 输入框等宽化与悬空寺图片预览

#### 5.22.1 行内单位提示全部移除

设置页原先每行输入框右侧都挂一句 `RowHintStyle` 的单位说明
（`逻辑像素 @100%`、`层`、`%`、`毫秒`）。这些提示把行撑得过宽、
视觉噪音大，已全部删除——**信息并未丢失**，同名控件上本来就有 `ToolTip`
写了同样的内容，只是不再占据行内空间。

#### 5.22.2 输入框等宽：写进 Style，而不是逐个对齐

宽度从各控件的内联属性**收进 `InputStyle`**：

```xml
<Style x:Key="InputStyle" TargetType="TextBox">
    <Setter Property="Width" Value="{StaticResource FormFieldW}"/>
    ...
</Style>
```

这个改法的意义不在于"少写几行"，而在于**让"所有输入框等宽"成为结构上成立的属性**：
以后新增输入框只要套 `InputStyle` 就自动同宽，不存在"忘了写宽度导致不齐"的可能。
原先 8 处 `Width="{StaticResource FormFieldW}"` 冗余声明已删除。

`FormFieldW` 由 84 调整为 **100**：84 的宽度下四位数字（如 `1000`）会贴上内边距。

另新增 `InputFillStyle`（`BasedOn="InputStyle"`，`Width=Auto` + `Stretch`），
供 Markdown 编辑器这类"整块内容区"使用——它不该受单行输入框的宽度约束。

#### 5.22.3 悬空寺图片项与预览

三项改动：

1. 分组标题由 `键位图` 改为 **`图片`** —— 该功能实际接受任意图片，
   标题写"键位图"会让用户以为只能用小鹤双拼那张。
2. 新增预览区，**宽度跟随卡片可用宽度**，等比缩放完整显示：

```xml
<Border Name="XuanKongSiImagePreviewBorder" ... Visibility="Collapsed">
    <Image Name="XuanKongSiImagePreview"
           Stretch="Uniform"
           StretchDirection="DownOnly"
           HorizontalAlignment="Center"/>
</Border>
```

- `Stretch="Uniform"` —— 保持宽高比，高度由宽度和原始比例自动推出，
  无需任何手写计算
- `StretchDirection="DownOnly"` —— 小图不放大（放大只会糊）

3. **默认图也参与预览**：用户从未选过图时，预览区展示的是随包分发的
   默认键位图（`Resources/XuanKongSi/小鹤双拼-键位图.png`），而不是一片空白。
   首次打开设置窗口的用户走的全是这条路径，如果这时不给预览，
   预览功能对他们等于不存在。

```csharp
// 优先级必须与 XuanKongSiOverlayWindow.RenderImage 一致，
// 否则预览会与实际生效的图不符 —— 预览的意义就在"所见即所得"。
//   ① 用户选定的图片（配置目录 Media/<ImageFileName>）
//   ② 随包分发的默认图 Resources/XuanKongSi/小鹤双拼-键位图.png
//   ③ 该目录下任意一张图（兜底）
private string ResolvePreviewImagePath() { ... }
```

只有当三级都落空时才 `Collapsed`（不留空占位）。

4. **文件名提示文案已删除**：原先按钮后面挂了一句
   `(默认：小鹤双拼键位图)`，预览下方还有一句
   `以上为实际显示效果，等比缩放至当前宽度。`
   两处都是"解释性文字"——预览图本身已经把这两件事说清楚了
   （是什么图、什么效果都能直接看到），文字属于冗余。
   相应地，原先"标签与预览必须同步"的约束也随之消失，
   状态来源只剩 `UpdateXuanKongSiImagePreview` 一处。

**实现细节**：`BitmapImage.CacheOption = BitmapCacheOption.OnLoad` 是必须的。
默认的 `OnDemand` 会持有文件句柄，用户换图时 `File.Copy` 会因旧文件被锁而失败。
`OnLoad` 立刻把像素读进内存并释放句柄。

#### 5.22.5 颜色按钮与输入框等宽

用户报"数字输入框和颜色框的宽度不一致"。

根因是两个控件族**各自定宽、互不知情**：

| 控件 | 原宽度声明 | 实际宽度 |
|---|---|---|
| 单行输入框 | `InputStyle` → `Width = FormFieldW` | 100 |
| 颜色按钮 | `ColorSwatchButton` → `MinWidth = 146` | 146 |

同一列里上下相邻的行，右边缘差 46px，视觉上非常刺眼。

修复是把颜色按钮也收敛到**同一个令牌**：

```xml
<Style x:Key="ColorSwatchButton" TargetType="Button">
    <!-- ⚠️ 必须用 Width 而不是 MinWidth：
         MinWidth 只设下限，内容（色块+色值文字）撑长就又不齐了。
         定宽才能保证右边缘对齐。 -->
    <Setter Property="Width" Value="{StaticResource FormFieldW}"/>
```

同时把按钮内容的尺寸压进 100 的额度内（原先按 146 设计）：

| 项 | 原值 | 新值 |
|---|---|---|
| Border `Padding` | `10,0` | `8,0` |
| 色块 `Width/Height` | 15 | 14 |
| 色块与文字间距 | 8 | 6 |

内容合计约 `14 + 6 + 60(文字) + 16(内边距) ≈ 96`，在 100 宽度里放得下。

> **一般化**：同一列里要求"对齐"的控件，宽度必须来自**同一个令牌**。
> 两个样式各自写死一个数字，编译不报错、单看代码也都合理，
> 只有把两者放进同一列才会暴露——这类耦合只能靠**实测**发现，
> 所以自检里加了一条 `CheckColorButtonWidth`：逐个切页+选样式后量宽度，
> 与输入框基准值比对（见 5.22.6）。

#### 5.22.6 自检：量控件尺寸的前提条件

本轮给 `--settingscheck` 加了三段断言（宽度一致性、颜色按钮等宽、
默认图预览），过程中踩到 WPF 布局的一个硬约束，值得单独记下来：

> **在 TabControl 里量控件的尺寸，必须先把"它所在的那一页 + 它所在的那个互斥面板"都激活。**

具体情况：

| 层面 | 问题 |
|---|---|
| 跨页 | 未选中的 TabItem 内容不在可视树上，`ActualHeight/Width` 为 0 |
| 跨面板 | 菜单样式页里圆环/Headshot/蜘蛛网/八卦是四组**互斥显示**的面板，未选中的那组控件是 `Collapsed`，尺寸同样为 0 |
| 定位 | 逻辑树会把所有页的内容都挂着，用"全局按名字查找"去判断页归属会**全部误判到第一页** |

正确的三段式做法：

```csharp
// 1) 限定在单个 TabItem 子树里查，确定控件归属哪一页
foreach (TabItem ti in tabs.Items)
    if (FindByName(ti, boxName) != null) homePage[boxName] = pageIndex;

// 2) 切到那一页，并把该控件所属的互斥面板（菜单样式）也选中
tabs.SelectedIndex = homePage[boxName];
if (styleKey != null) ((RadioButton)GetField(styleKey)).IsChecked = true;

// 3) 等布局与消息队列都跑完，再量
win.UpdateLayout(); Pump();
double w = box.ActualWidth;
```

**配套原则：断言要能自己造前置条件。**

"默认图也能预览"这条，如果按"看当前配置里有没有图"去写，
用户配置里本来就有图时会走不到默认分支，没图时又可能整个 SKIP ——
两种情况都等于这段代码从没被验证过。

正确做法是自检**自己造出前置条件**：用 `DrawingVisual` +
`RenderTargetBitmap` 画一张 900×360 的测试图（不依赖 `System.Drawing`），
然后显式地把 `ImageFileName` 置空、调一次刷新，断言预览区仍为
`Visible` 且 `Source != null`。

实测输出：

```
PASS: 未选图时走默认图预览（563x427.3，源 .../Resources/XuanKongSi/小鹤双拼-键位图.png）
PASS: 5 个颜色按钮宽度与输入框一致（100）
PASS: 8 个单行输入框宽度全部一致（100）
```

> **断言清单要往"安全的失败方向"设计。**
> 输入框名字清单是**手写**的，新增输入框时可能忘记加进去。
> 忘加的后果是"断言覆盖变少"，不会产生误报警——
> 属于安全的失败方向。反过来，如果清单是从可视树自动收集的，
> 那么"漏收"和"宽度真的不一致"会混在一起，反而更难查。

**图片预览的断言方式**：自检**自己造一张 900×360 测试图**注入配置来测，
而不是判断"用户当前有没有选图"——后者很容易 SKIP 掉，等于这段代码从没被验证过。
造图用 `DrawingVisual` + `RenderTargetBitmap`（不依赖 `System.Drawing`）。

实测输出：

```
四页累计：单行输入框 8 个、多行编辑器 1 个
PASS: 8 个单行输入框宽度全部一致（100）
PASS: 行内已无任何单位/提示文字（单位信息仅在 ToolTip 中）
PASS: 分组标题已改为「图片」
PASS: 文件名标签已同步更新
PASS: 等比缩放保持（宽高比 2.500）        ← 900x360 → 563x225.2
PASS: 宽度已自适应撑满容器（563 / 可用 563）
结果: ALL PASS
```

> **维护提示**：`CheckInputUniformity` 里有一张输入框名字清单 `known`。
> 新增输入框时要把它加进去，否则该输入框不参与等宽断言。
> 漏加只会让覆盖变少、不会误报，属于"安全的失败方向"。

## 6. 构建与部署

脚本分**两层**：底层 `build.ps1` / `release.ps1` 只负责干活，
上层 `job-build.ps1` / `job-pack.ps1` 负责「干活 + 打开产物目录」。
日常用上层作业脚本即可。

### 6.1 作业脚本（推荐入口）

| 脚本 | 用途 | 产物 | 是否自动打开目录 |
|---|---|---|---|
| `job-build.ps1` | 日常迭代验证 | `build/<版本>/WinLoop.exe` | 是（选中 exe） |
| `job-pack.ps1` | 正式发布 | `release/WinLoop_<版本>.exe` | 是（选中安装包） |

```powershell
.\job-build.ps1                  # 构建 + 打开产物目录
.\job-build.ps1 -Version V0.3    # 覆盖版本前缀（默认 V0.2）
.\job-build.ps1 -NoClean         # 跳过 bin/obj 清理（快速增量构建）

.\job-pack.ps1                   # 打包 + 打开产物目录
.\job-pack.ps1 -NoZipFallback    # 找不到 ISCC 时直接失败（默认降级出免安装 zip）
.\job-pack.ps1 -NoClean          # 同上，跳过清理
```

两者都会在结束后把 `build/latest`（或 release 产物）定位并打开，
不需要（也无法）跳过。打开动作统一由 `open-artifact.ps1` 实现 ——
它优先调用 `Tools/ExplorerFocus` 绕过 Windows 前台锁定，
把资源管理器窗口真正拉到最前；失败时退回 `explorer.exe`。

### 6.2 底层脚本

```powershell
# build.ps1 —— 只构建，不打开目录
# 参数：-Version（版本前缀，默认 V0.2）、-NoClean
# 输出：./build/<版本>/     （版本 = "V0.2-yyyyMMddHHmm"）
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -o ./build/$version

# release.ps1 —— 构建 + Inno Setup 编译安装包
# 参数：-Version（版本前缀）、-NoZipFallback、-NoClean
# 输出：./release/WinLoop_<版本>.exe
#       找不到 ISCC 时降级为 ./release/WinLoop_<版本>.zip
```

> 注：`dotnet publish` 指定 `-r win-x64 --self-contained false`，
> 产物为 framework-dependent 的 64 位版本，需要目标机器安装 .NET Core 3.1 Runtime。

#### 构建前自动清 `bin`/`obj`（2026-09-20 起内置）

`build.ps1` **默认会先删掉 `WinLoop/bin` 与 `WinLoop/obj`**，
所以不再需要手工 `rm -rf`（这个清理曾经是外部步骤，每次都要人工执行 + 确认）。

**为什么必须清**——不是洁癖，是防一个**静默错误**：

- **症状**：dll 从 17MB 膨胀到 34MB（正好两倍），即两个字体被嵌了两遍。
  编译 **0 报错**，只是产物悄悄变大。
- **原因**：WPF 增量构建把「该嵌哪些资源」的清单缓存在 `obj\` 里。
  一旦改动过资源清单（增删/改名 `<Resource>` 文件、调整通配符、换掉被嵌入的 ico），
  **旧条目不会自动作废**，新的叠加上去 → 重复嵌入。
- ⚠️ **`dotnet clean` 修不了这个**：它只删「当前项目评估认为它产出过」的文件，
  而陈旧条目恰恰不在当前清单里。唯一可靠做法是直接删 `obj`（增量状态）与 `bin`（输出）。

**实现要点**：删之前会先跑 `dotnet build-server shutdown` 停掉常驻的
`VBCSCompiler` / MSBuild 节点 —— 它们持有 `obj` 下的句柄，不清掉会删失败。
删除失败（最常见是 `WinLoop.exe` 还开着）时脚本**直接以退出码 1 终止**并提示先退出程序，
不做静默降级 —— 半清理状态下构建出来的产物可能带陈旧资源，比直接失败更糟。

代价是一次完整重编译（约 10~20 秒）。只有明确知道本次没动资源清单、
想快速迭代时才用 `-NoClean` 跳过。

### 6.3 运行时自检开关

发布产物自带两个自检开关，用于验证「编译通过但运行时才暴露」的问题：

```powershell
# 内嵌字体自检：验证 pack URI 能否解析、字重是否落到不同字面
# 参数是【日志文件路径】
WinLoop.exe --fontcheck <日志文件路径>

# 设置窗口自检：构造窗口、逐页截图、断言布局与控件尺寸
# 参数是【输出目录】，报告写在 <目录>/report.txt
WinLoop.exe --settingscheck <输出目录>
```

> ⚠️ 两个开关的参数类型不同（文件 vs 目录），别混用 ——
> 传错时不会报错，只会静默产生空报告。
>
> 这两个自检必须挂在 `WinLoop.exe` 上，不能用外部探针程序：
> `pack://application:,,,` 的解析基准是**入口程序集**，
> 外部探针引 WinLoop.dll 里的资源永远解析不到，会静默回退到系统字体。

### 6.4 开机自启动

通过注册表实现：
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
键名: WinLoop
值: "{exe路径}"
```

### 6.5 目录约定与归档

**根目录只放"构建/运行必需"的东西**：源码、脚本、文档、产物目录。
开发过程的中间产物一律不入根目录。

| 目录 | 性质 | 是否进 git |
|---|---|---|
| `WinLoop/` | 主项目源码 | 是 |
| `Tools/` | 开发期小工具 | 否（被忽略） |
| `build/` `release/` | 构建产物 | 否 |
| `tmp/` | 临时工作区 | 否（整目录忽略） |
| `docs/archive/` | 历史归档 | **是**（归档理由需登记） |
| `probe-out/` | 临时探针输出 | 否 |

**归档规则**（`docs/archive/`）：

1. 只有**非项目必需**的文件才进归档：旧版本备份、一次性方案产物、被替代的设计稿。
2. 每份归档文件必须在 `docs/archive/README.md` 登记**来源**、
   **为什么留着**、**可否安全删除**三项。归档不是垃圾堆，
   是"以后想回溯当时怎么做的"凭据。
3. `docs/archive/` **要进 git** —— 否则归档就失去意义（丢了就真没了）。
   但里面的 `.log`、`report.txt` 等临时产物仍忽略。

**⚠️ 归档前必查：该文件是否被源码注释引用。**

本项目踩过一次：`tmp/` 一度被当作"纯调试垃圾"准备清空，
但 grep 后发现两处源码注释明确指向它：

```csharp
// WinLoop/Menus/CSHeadshotPathData.cs
/// 生成脚本：tmp/ref/gen_pathdata.py（源图 tmp/ref/r2.jpg，DP 容差 1.2px）

// WinLoop/Menus/CSHeadshotLayers.cs
/// 生成脚本：tmp/ref/zf1_export.py
```

这两个脚本是 Headshot 图形的**矢量化来源** —— 源码里的路径数据是它们的**产物**。
删掉就切断了"图是怎么来的"这条追溯链，以后想调容差重新生成将失去依据。

而 `tmp/` 恰好在 `.gitignore` 里，**这份"再生图纸"不会进仓库**。
处理方式：把最小必要集（生成脚本 + 源图 + 模型 JSON）复制到
`docs/archive/xuankongsi-star-source/`（该目录进 git），
本地 `tmp/ref/` 保持原位不动（源码注释路径依然有效）。

> **教训**：清理任何目录前，先 `grep -rn "<目录名>" --include=*.cs` 查一遍
> 有没有源码注释引用了它。注释里的路径是**契约**，不是随手写的备注。

## 7. 版本历史

### V0.2 (2026-09)
- ✅ 目标窗口锁定：中键按下瞬间锁定窗口句柄，菜单弹出期间不受前台窗口变化影响
- ✅ 性能优化：移除分屏时的 `Thread.Sleep(50)` 阻塞，改用 `SWP_ASYNCWINDOWPOS`
- ✅ 多显示器 / 高 DPI：`app.manifest` 声明 Per-Monitor V2；工作区改由
  `MonitorFromWindow` + `GetMonitorInfo` 取物理像素；增加物理像素到 WPF 逻辑坐标的换算
- ✅ 鼠标跟随：由 16ms 轮询改为鼠标钩子事件推送
- ✅ 菜单动画：弹出缩放淡入 120ms / 收起淡出 80ms
- ✅ 新增 3 种窗口动作：最大化/还原切换、窗口置顶切换、关闭窗口
- ✅ 恢复 4 种菜单样式可选（此前强制回退为圆环）
- ✅ 内嵌 Noto Sans SC 字体（Regular + Medium），界面渲染不再依赖目标机器字体
- ✅ 新增颜色选择器窗口（RGB / HSV / HSL / HEX 四格式互转，切换格式不漂移）
- ✅ 设置窗口改为固定尺寸 + PixPin 行流版式（白卡 + 等高行 + 1px 分割线）
- ✅ 设置页色值框隐藏化：色值改由色块按钮直接显示
- ✅ 输入框统一等宽（宽度令牌收进 `InputStyle`）
- ✅ 悬空寺图片面板：新增预览（宽度自适应、等比缩放），默认图也展示
- ✅ 新增运行时自检：`--fontcheck` / `--settingscheck`
- ✅ 程序图标 / 托盘图标重做：八角星主图（右上扇区金色高亮）+ 中蓝 `#0F6CBD` 圆角底，
  多尺寸 ICO；图形占比按显示尺寸分档（桌面撑满 / 托盘 16px 留 2px 边框）
- ✅ **默认菜单样式改为 Headshot**，设置页该项显示名由「八角星」改为 `Headshot`
  （枚举顺序未动，老配置不受影响）
- ✅ 构建脚本内置缓存清理：`build.ps1` 默认先清 `bin`/`obj`（`-NoClean` 可跳过）；
  移除冗余的 `dotnet build` 与一段会覆盖托盘图标的 WGestures 遗留代码
- ✅ 代码清理：移除 MainWindow、Core/* 及 MenuOverlayWindow.CalculateMenuPosition 等不可达代码

### V0.1 (2025-12 ~ 2026-01)
- ✅ 完整设置窗口（菜单样式、操作配置、杂项设置）
- ✅ Loop 风格环形菜单（白色轮廓 + 蓝色高亮 + 透明扇区）
- ✅ 全局鼠标钩子（中键检测 + 延时触发）
- ✅ 13 种窗口操作（含 Win+D 显示桌面）
- ✅ 系统托盘集成、配置持久化、触发时长实时生效
- ✅ 新增「悬空寺」覆盖层：双击热键显示、ESC 收起
- ✅ 悬空寺内容支持 Markdown（渲染到 WPF 文档），兼容旧 XAML 文档
- ✅ 触发键支持：左右 Alt / 左右 Shift / 左右 Ctrl

## 8. 参考资料

- [Loop (macOS)](https://github.com/MrKai77/Loop) - 功能设计参考
- [WGestures](https://github.com/yingDev/WGestures) - Windows 鼠标钩子参考
- [Windows API 文档](https://docs.microsoft.com/en-us/windows/win32/api/)
