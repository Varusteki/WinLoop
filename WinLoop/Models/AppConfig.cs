using System.Collections.Generic;

namespace WinLoop.Models
{
    public class AppConfig
    {
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

        // (Removed accent color options — using explicit Wheel/Ring/Highlight colors)
    }

    public class BasicRadialMenuConfig
    {
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
        public double Radius { get; set; } = 80;             // 外圈半径，稍大一些
        public string LineColor { get; set; } = "#C0C0C0";   // 银灰色轮廓
        public string HighlightColor { get; set; } = "#007AFF"; // 蓝色高亮
    }

    public class SpiderWebMenuConfig
    {
        public double OuterRadius { get; set; } = 70;
        public int Rings { get; set; } = 3;
        public string LineColor { get; set; } = "#C0C0C0";       // 银灰色
        public string HighlightColor { get; set; } = "#007AFF";  // 亮蓝色
    }

    public class BaguaMenuConfig
    {
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
        RightTwoThirds
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