using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinLoop.Models;

namespace WinLoop.Menus
{
    public abstract class RadialMenu : Canvas
    {
        // ========== 扇区约定：所有样式共用这一份，不许各自再立一套 ==========

        /// <summary>扇区数：8 个方向。</summary>
        protected const int SectorCount = 8;

        /// <summary>每个扇区的张角：45°。</summary>
        protected const double SectorDeg = 360.0 / SectorCount;

        /// <summary>半个扇区：22.5°。</summary>
        protected const double HalfSectorDeg = SectorDeg / 2.0;

        /// <summary>
        /// 第 0 个扇区（<see cref="MenuItemPosition.Position1"/>）的**中心方向**：正上方。
        /// 其后顺时针依次为 Position2..Position8。
        ///
        /// 它是**绘制与判定共用的唯一角度基准**：各样式画扇区/星尖时必须由它推导
        /// （扇区起始边 = 本值 − <see cref="HalfSectorDeg"/>），判定同样以它为准。
        /// 从前各样式各自写一份角度常量（`BASE_ANGLE_DEG` / `BASE_ANGLE` / `START_ANGLE`），
        /// 任何一边改了另一边不改就会整体错位半个扇区 —— 现在只有这一个来源。
        /// </summary>
        protected const double FirstSectorAxisDeg = -90.0;

        /// <summary>
        /// 中心死区：指针落在「菜单中心 → 图形内缘」这个范围内时，
        /// **不高亮任何扇区，松开也不触发任何动作**。
        ///
        /// 各样式一律**只按方向选、不设半径上限**：指针移出菜单图形之外
        /// （甚至移到屏幕另一角）依然按它所在的方向高亮对应选区，不会丢高亮。
        ///
        /// 唯一的例外就是这圈死区。它有两重意义：
        ///
        /// 1. **数值保护**：圆心处方向无定义（<c>atan2(0,0)</c> 恒等于 0°）。
        ///    菜单是以**指针位置**为中心弹出的，弹出的头一帧指针恰好落在圆心，
        ///    不挡就会立刻闪出一格高亮；
        /// 2. **体验死区**（2026-09-17 起）：死区边界**贴合各样式自己图形的内缘**，
        ///    指针退回到中心那片空白区域就当作「反悔」—— 高亮清掉、松手不动作。
        ///    圆环就是内圆之内、八卦就是太极范围、蜘蛛网就是中心第一个八边形、
        ///    八角星就是底层同心圆的内圆之内。
        ///
        /// 边界由各样式通过 <see cref="CenterDeadZoneRadius"/> 报告，
        /// 因此用户改设置里的「大小」时死区会跟着缩放，形状永远与画出来的图形一致。
        /// </summary>
        /// <remarks>
        /// 归一化半径（1.0 = <see cref="VisualRadius"/>）下的兜底值。
        /// 只在 <see cref="CenterDeadZoneRadius"/> 未给出有效值时启用，
        /// 保留一点点数值保护，避免退回成"方向无定义却硬算一个扇区"。
        /// </remarks>
        protected const double HitMinDistance = 1e-6;

        protected Point CenterPoint { get; private set; }
        protected AppConfig Config { get; private set; }

        /// <summary>
        /// 菜单自身的半尺寸（逻辑坐标）。
        /// 各样式绘制时的留白不同（例如蜘蛛网会外扩 1.1 倍、八卦 1.2 倍），
        /// 菜单中心并不是 Width/2，因此必须由菜单自己报告，
        /// 外部（MenuOverlayWindow）不得再用配置半径自行推算，否则定位与命中都会错位。
        /// </summary>
        public double VisualRadius { get; protected set; }

        /// <summary>
        /// 菜单在自身坐标系中的中心点，等价于 (VisualRadius, VisualRadius)。
        /// </summary>
        public Point VisualCenter => new Point(VisualRadius, VisualRadius);

        /// <summary>
        /// 中心死区的半径（**绝对长度**，与 <see cref="VisualRadius"/> 同单位）。
        /// 指针到中心的距离小于等于本值时，不选任何扇区。
        ///
        /// **这是各样式图形内缘的半径，必须由各样式自己报告** —— 基类算不出来：
        ///   - 圆环：内圆半径（<c>OuterRadius − Thickness</c>）
        ///   - 八角星：底层同心圆内圈的外缘（<c>_scale × RingInnerHigh</c>）
        ///   - 蜘蛛网：中心第一个八边形（<c>_OuterRadius / (Rings + 1)</c>）
        ///   - 八卦：中心太极图（<c>_OuterRadius × 0.32</c>）
        ///
        /// 返回 0（或不重写）表示"没有死区"，只有 <see cref="HitMinDistance"/>
        /// 那点数值保护生效。
        /// </summary>
        /// <remarks>
        /// ⚠️ 子类必须用 <see cref="VisualRadius"/> 的**同一套缩放**来算这个值，
        /// 不要直接写配置里的原始半径 —— 八角星/蜘蛛网/八卦的 VisualRadius
        /// 都带自己的绘制外扩系数（1.0346 / 1.1 / 1.2），
        /// 两边不同源会让死区相对图形整体偏大或偏小。
        /// </remarks>
        public virtual double CenterDeadZoneRadius => 0.0;

        public void Initialize(AppConfig config, Point centerPoint)
        {
            Config = config;
            CenterPoint = centerPoint;
            InitializeMenu();
        }

        /// <summary>
        /// 把配置里的**基准尺寸**（100% 缩放下的逻辑像素）换算成**本屏实际逻辑像素**。
        ///
        /// 子类读任何半径/粗细都必须经过这里，不许直接写
        /// <c>Config.XxxMenuConfig.OuterRadius</c> —— 那样在 125%/150%/200% 缩放下
        /// 菜单会画得比用户设定的小，而且在 4K 上小得离谱。
        ///
        /// 缩放系数来自 <see cref="AppConfig.SizingScale"/>，由 MenuOverlayWindow
        /// 在弹出前按当前显示器 DPI 设定；设置面板的预览固定传 1.0。
        /// </summary>
        /// <param name="baseValue">配置里的基准值（100% 缩放下的逻辑像素）。</param>
        protected double Scaled(double baseValue)
        {
            if (double.IsNaN(baseValue) || double.IsInfinity(baseValue)) return 0;
            double s = Config?.SizingScale ?? 1.0;
            if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0) s = 1.0;
            return baseValue * s;
        }
        
        protected abstract void InitializeMenu();
        
        // ========== 高亮更新的唯一路径 ==========
        //
        // ⚠ 这里**故意不重写** OnMouseMove / OnMouseUp。
        //
        // 曾经这里有一份「兜底」判定：`GetSelectedItem(e.GetPosition(this))` 直接调
        // HighlightItem。它与 MenuOverlayWindow 的路径各自持有一份高亮状态
        // （菜单类的 `_highlightedPosition` vs 覆盖窗口的 `_highlightedPosition`），
        // 于是同一个指针位置被两条路径用**不同坐标系**各算一次、交替落到画面上 ——
        // 表现就是高亮在两个扇区之间稳定乱跳（两个扇区可以相隔 90°，绝非边界抖动）。
        //
        // 更糟的是它**绕过覆盖窗口的状态记录**，所以「当前高亮是哪个扇区」这件事
        // 有两份真相，ExecuteAction 读到的未必是屏幕上显示的那个。
        //
        // 现在高亮只由 MenuOverlayWindow 统一更新：
        //   全局钩子（主路径，WH_MOUSE_LL 推送物理坐标 → UpdateHighlightFromScreen）
        //   窗口内 MouseMove（兜底，Canvas 坐标 → UpdateHighlight）
        // 两条都收敛到 UpdateHighlight 里的同一个 CanvasToMenu 换算，写同一份状态。
        // 子类只需要负责「高亮画成什么形状」，不负责「什么时候高亮」。

        /// <summary>
        /// **命中判定的唯一实现**。
        ///
        /// 规则：取「菜单中心 → 指针」这条**射线所在的方向**，看它落在哪个 45° 扇区内。
        ///   - 与指针离中心多远**无关**：图形之内、图形之外、甚至屏幕另一角，结果都一样；
        ///   - 与指针是否压在该扇区的可见图形上**无关**：圆环的空心部分、
        ///     八角星与圆环之间的空隙，一样参与选择；
        ///   - 扇区角度基准见 <see cref="FirstSectorAxisDeg"/>（Position1 朝正上方）。
        ///
        /// 唯一例外：指针落在中心死区内（距离 ≤ <see cref="CenterDeadZoneRadius"/>）
        /// 时返回 null —— 不高亮、松开也不动作。理由见该属性与
        /// <see cref="HitMinDistance"/>。
        /// </summary>
        protected MenuItemPosition? SelectSectorByDirection(Point mousePosition)
        {
            if (VisualRadius <= 0) return null;

            double dx = mousePosition.X - VisualCenter.X;
            double dy = mousePosition.Y - VisualCenter.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // 中心死区：图形内缘之内一律不选。
            // 死区半径由各样式报告；退回 0 时仍有 HitMinDistance 兜住圆心退化点
            // （atan2(0,0) 方向无定义，不能让它硬算出一个扇区）。
            double deadZone = CenterDeadZoneRadius;
            if (deadZone <= 0) deadZone = HitMinDistance * VisualRadius;
            if (distance <= deadZone) return null;

            // 方向角：+x 为 0°，y 轴向下为正，范围 (-180°, 180°]
            double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;

            // 折算成「相对第 0 个扇区中心」的角，归一到 [0, 360)
            double rel = deg - FirstSectorAxisDeg;
            rel %= 360.0;
            if (rel < 0) rel += 360.0;

            // 加半个扇区再向下取整 = 「离哪个扇区中心最近就归哪个扇区」
            int sector = (int)Math.Floor((rel + HalfSectorDeg) / SectorDeg) % SectorCount;
            if (sector < 0) sector += SectorCount;
            return (MenuItemPosition)sector;
        }

        /// <summary>
        /// 指针指向哪个扇区。
        ///
        /// **判定逻辑只有这一处**：各样式只允许在「高亮画成什么形状」上不同
        /// （圆环画环带扇区、八角星画星∩扇区、八卦画该卦的格子……），
        /// 绝不允许在「选哪个扇区」上不同。子类不需要、也不应该重写本方法 ——
        /// 要改判定就改 <see cref="SelectSectorByDirection"/>，四种样式一起变。
        /// </summary>
        public virtual MenuItemPosition? GetSelectedItem(Point mousePosition)
        {
            return SelectSectorByDirection(mousePosition);
        }

        public abstract void HighlightItem(MenuItemPosition itemPosition);
        public abstract void ClearHighlight();
    }
}