using System.Collections.Generic;

namespace WinLoop.Models
{
    public class AppConfig
    {
        /// <summary>
        /// 尺寸类字段的「单位版本」。用于识别老配置，见 <see cref="SizingUnitV1"/> / <see cref="SizingUnitCurrent"/>。
        ///
        /// 老 config.json 里没有这个字段，反序列化后是 0（= <see cref="SizingUnitV1"/>），
        /// 于是 ConfigManager 能识别出「这是老配置」，补写标记后继续用。
        /// </summary>
        public int SizingUnitVersion { get; set; } = SizingUnitCurrent;

        /// <summary>
        /// v1（历史）：尺寸字段曾被视为**固定像素**。
        /// </summary>
        public const int SizingUnitV1 = 0;

        /// <summary>
        /// v2（当前）：尺寸字段就是 **DIP（设备无关像素）**。
        ///
        /// WPF 渲染时会自动按本屏 DPI 把它换算成物理像素，因此任何缩放比例下
        /// 物理（视觉）大小恒定 —— 程序是 Per-Monitor V2 感知的，见 app.manifest。
        ///
        /// 数值上与 v1 同义（100% 缩放时 DIP = 物理像素），
        /// 所以从 v1 迁移到 v2 **不需要改动任何数值**，只是补一个标记。
        /// 版本号保留下来只为识别老配置，不再影响任何尺寸计算。
        /// </summary>
        public const int SizingUnitCurrent = 2;

        /// <summary>
        /// 默认菜单样式：**八角星（Headshot）**。
        ///
        /// 只在「没有配置文件」或「恢复默认值」时生效 ——
        /// 已存在的 config.json 里存了显式值，不会被动改写（尊重用户选择）。
        ///
        /// ⚠️ 改这个默认值**不需要**动 <see cref="MenuStyle"/> 的成员顺序：
        /// 枚举是整数值序列化进配置的（BasicRadial=0 / CSHeadshotOctagon=1 / …），
        /// 插入或调换成员会让老配置的动作错位。
        /// </summary>
        public MenuStyle MenuStyle { get; set; } = MenuStyle.CSHeadshotOctagon;
        public BasicRadialMenuConfig BasicRadialMenuConfig { get; set; } = new BasicRadialMenuConfig();
        public CSHeadshotMenuConfig CSHeadshotMenuConfig { get; set; } = new CSHeadshotMenuConfig();
        public SpiderWebMenuConfig SpiderWebMenuConfig { get; set; } = new SpiderWebMenuConfig();
        public BaguaMenuConfig BaguaMenuConfig { get; set; } = new BaguaMenuConfig();
        public Dictionary<MenuItemPosition, WindowAction> ActionMapping { get; set; } = new Dictionary<MenuItemPosition, WindowAction>
        {
            { MenuItemPosition.Position1, WindowAction.Maximize },
            { MenuItemPosition.Position2, WindowAction.TopRightQuadrant },
            { MenuItemPosition.Position3, WindowAction.RightTwoThirds },
            { MenuItemPosition.Position4, WindowAction.BackToDesktop },
            { MenuItemPosition.Position5, WindowAction.Minimize },
            { MenuItemPosition.Position6, WindowAction.BottomLeftQuadrant },
            { MenuItemPosition.Position7, WindowAction.LeftTwoThirds },
            { MenuItemPosition.Position8, WindowAction.TopLeftQuadrant }
        };
        public bool AutoStart { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        // 触发时长（毫秒），PRD默认200ms
        public int TriggerDelay { get; set; } = 200;

        // 悬空寺配置
        public XuanKongSiConfig XuanKongSi { get; set; } = new XuanKongSiConfig();

        // (Removed accent color options — using explicit Wheel/Ring/Highlight colors)
    }

    public class BasicRadialMenuConfig
    {
        // 以下尺寸字段单位 = **DIP（设备无关像素）**，见 AppConfig.SizingUnitCurrent。
        // WPF 渲染时自动按本屏 DPI 换算成物理像素 —— 这里不需要、也不得再乘任何 DPI 系数。
        public double OuterRadius { get; set; } = 50;
        // Match Loop defaults: radius 50, thickness 22 => inner = 50 - 22 = 28
        public double InnerRadius { get; set; } = 28;
        // Thickness (outerRadius - innerRadius). Loop exposes "radialMenuThickness" (default 22).
        public double Thickness { get; set; } = 22;
        // 配色设置
        public string WheelColor { get; set; } = "#C0C0C0";      // 银灰色（已弃用，保留兼容）
        public string RingColor { get; set; } = "#C0C0C0";       // 圆环银灰色
        public string HighlightColor { get; set; } = "#007AFF";  // 高亮蓝色
    }

    public class CSHeadshotMenuConfig
    {
        /// <summary>「大小」的基准：**100% 缩放**时尖刺顶点对应的逻辑像素半径。</summary>
        public const double BaseRadius = 90;

        /// <summary>
        /// 尖角顶点半径（像素，即整个菜单的半尺寸）。
        /// 设置面板里只暴露一个「大小」百分比，存盘时按 <see cref="BaseRadius"/> 换算到这里；
        /// 也兼容直接手改配置文件。
        /// </summary>
        public double Radius { get; set; } = BaseRadius;
        public string LineColor { get; set; } = "#1A1A1A";      // 墨迹：近黑（参考图是黑白插画风）
        public string BodyColor { get; set; } = "#FCFCFC";      // 底衬：近白；设为 Transparent 可只要线稿
        public string HighlightColor { get; set; } = "#D4AF37"; // 高亮：金属金（选中那一极的纯色填充）

        // 注：八角星的「星尖粗细」（谷半径）曾短暂做成可调配置，现已**写死**在
        // CSHeadshotMenu.STAR_VALLEY_RADIUS = 0.45（调来调去没有更好的值，不如固定下来）。
    }

    public class SpiderWebMenuConfig
    {
        /// <summary>外圈半径，单位 = 100% 缩放下的逻辑像素（DIP 基准）。</summary>
        public double OuterRadius { get; set; } = 70;
        public int Rings { get; set; } = 3;
        public string LineColor { get; set; } = "#C0C0C0";       // 银灰色
        public string HighlightColor { get; set; } = "#007AFF";  // 亮蓝色
    }

    public class BaguaMenuConfig
    {
        /// <summary>八卦图半径，单位 = 100% 缩放下的逻辑像素（DIP 基准）。</summary>
        public double OuterRadius { get; set; } = 90;            // 八卦图半径
        public string LineColor { get; set; } = "#000000";       // 黑色（经典八卦色）
        public int SectorTransparency { get; set; } = 70;         // 选区默认透明度 (0-100)，0=不透明，100=完全透明
        public int HighlightTransparency { get; set; } = 0;       // 高亮透明度 (0-100)，0=不透明，100=完全透明
    }

    public enum MenuStyle
    {
        BasicRadial,
        CSHeadshotOctagon,
        SpiderWeb,
        Bagua
    }

    public enum WindowAction
    {
        BackToDesktop,
        Minimize,
        Maximize,
        LeftHalf,
        RightHalf,
        TopHalf,
        BottomHalf,
        TopLeftQuadrant,
        BottomLeftQuadrant,
        TopRightQuadrant,
        BottomRightQuadrant,
        LeftTwoThirds,
        RightTwoThirds,
        // 以下为 V0.2 新增。必须追加在末尾：WindowAction 会按序号持久化到 config.json，
        // 插在中间会导致老用户的动作映射错位。
        ToggleMaximize,
        ToggleTopMost,
        CloseWindow
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
        public XuanKongSiTriggerKey TriggerKey { get; set; } = XuanKongSiTriggerKey.LeftShift;
        public int HoldDurationMs { get; set; } = 300;

        // “悬空寺”展示内容设置
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

        // New grouped options (either left or right key).
        Alt = 8,
        Shift = 9,
        Ctrl = 10
    }
}