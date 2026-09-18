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
        /// v1：尺寸字段是**固定像素**。菜单在任何缩放下都画这么大，
        /// 于是在 200% 缩放的 4K 屏上看起来只有 1080p 的一半，占屏比例只剩 1/4。
        /// </summary>
        public const int SizingUnitV1 = 0;

        /// <summary>
        /// v2（当前）：尺寸字段是**100% 缩放下的逻辑像素（DIP 基准）**。
        /// 菜单弹出时乘以「本屏 DPI / 96」，因此在任何缩放比例下视觉大小恒定。
        ///
        /// 数值上与 v1 恰好同义（100% 缩放时 DPI/96 = 1，换算结果等于原值），
        /// 所以从 v1 迁移到 v2 **不需要改动任何数值**，只是补一个标记。
        /// </summary>
        public const int SizingUnitCurrent = 2;

        public MenuStyle MenuStyle { get; set; } = MenuStyle.BasicRadial;
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

        /// <summary>
        /// **运行时**的尺寸缩放系数 = 本屏 DPI / 96（100% 缩放时 = 1.0）。
        ///
        /// 只影响本次弹出，**不落盘**：菜单弹出前由 MenuOverlayWindow 按当前显示器 DPI 设好，
        /// 菜单 InitializeMenu 时用它把「100% 基准的逻辑像素」换算成「本屏实际逻辑像素」。
        /// 设置面板的预览固定传 1.0（预览只表达相对大小，不该随用户当前屏幕变来变去）。
        ///
        /// 标 <see cref="System.Text.Json.Serialization.JsonIgnoreAttribute"/> 是必须的：
        /// 它是运行时状态而非用户配置，写进 config.json 会在换屏后留下脏值。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double SizingScale { get; set; } = 1.0;

        // (Removed accent color options — using explicit Wheel/Ring/Highlight colors)
    }

    public class BasicRadialMenuConfig
    {
        // 以下尺寸字段单位 = 100% 缩放下的逻辑像素（DIP 基准），见 AppConfig.SizingUnitCurrent。
        // 菜单渲染时统一乘以 AppConfig.SizingScale（本屏 DPI/96）。
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