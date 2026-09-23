using System;
using System.Windows;
using System.Windows.Media;
using WinLoop.Models;

namespace WinLoop.Menus
{
    /// <summary>
    /// 蜘蛛网菜单 - 参考真实蜘蛛网样式
    /// 8条辐射线 + 向内凹陷的弧形环线
    /// </summary>
    public class SpiderWebMenu : RadialMenu
    {
        private const int ITEM_COUNT = SectorCount;   // 8 个方向
        private const double ANGLE_STEP = 2 * Math.PI / ITEM_COUNT;   // 45°
        // 起始角度：辐射线画在扇区边界上，所以起始边 = 第 0 个扇区（Position1）
        // 的中心 − 半个扇区。中心方向由基类统一给出（正上方），判定与绘制同源。
        private const double START_ANGLE = (FirstSectorAxisDeg - HalfSectorDeg) * Math.PI / 180.0;

        // 绘制留白系数：外扩到 1.1 倍，给最外层环线留出边距
        private const double RADIUS_SCALE = 1.1;

        private MenuItemPosition? _highlightedPosition;
        private double _outerRadius;
        private Point _center;

        protected override void InitializeMenu()
        {
            _outerRadius = Scaled(Config.SpiderWebMenuConfig.OuterRadius);

            this.Width = _outerRadius * RADIUS_SCALE * 2;
            this.Height = _outerRadius * RADIUS_SCALE * 2;
            _center = new Point(_outerRadius * RADIUS_SCALE, _outerRadius * RADIUS_SCALE);

            // 把自己的真实半尺寸报告给外部，供定位与命中使用
            this.VisualRadius = _outerRadius * RADIUS_SCALE;

            // 中心死区 = 中心第一个八边形。环线间距是 _outerRadius / (rings + 1)，
            // 第一条环线（ring = 1）就落在该半径上；rings 配置异常时按 1 兜底。
            int rings = Config.SpiderWebMenuConfig.Rings;
            if (rings < 1) rings = 1;
            _deadZoneRadius = _outerRadius / (rings + 1);

            InvalidateVisual();
        }

        /// <summary>
        /// 中心死区 = 蛛网中心第一个八边形之内。
        /// </summary>
        public override double CenterDeadZoneRadius => _deadZoneRadius;

        /// <summary>
        /// 蛛网最外一圈就画在 <c>_outerRadius</c> 上；<see cref="VisualRadius"/> 额外乘了
        /// 1.1 的留白，比"看得见的边"大一圈，不能拿来贴锚点。
        /// </summary>
        public override double DrawnRadius => _outerRadius;

        private double _deadZoneRadius;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            
            if (Config == null) return;
            
            Color lineColor = (Color)ColorConverter.ConvertFromString(Config.SpiderWebMenuConfig.LineColor);
            Color highlightColor = (Color)ColorConverter.ConvertFromString(Config.SpiderWebMenuConfig.HighlightColor);
            
            Pen linePen = new Pen(new SolidColorBrush(lineColor), 2.5);
            linePen.Freeze();
            
            Brush highlightBrush = new SolidColorBrush(Color.FromArgb(180, highlightColor.R, highlightColor.G, highlightColor.B));
            
            // 绘制高亮扇区
            if (_highlightedPosition.HasValue)
            {
                DrawHighlightSector(dc, _highlightedPosition.Value, highlightBrush);
            }
            
            // 绘制蜘蛛网
            DrawSpiderWeb(dc, linePen);
        }

        /// <summary>
        /// 绘制蜘蛛网 - 8条辐射线 + 向内凹陷的弧形环线
        /// </summary>
        private void DrawSpiderWeb(DrawingContext dc, Pen pen)
        {
            int rings = Config.SpiderWebMenuConfig.Rings;
            
            // 绘制8条辐射线（从中心到外边缘）
            for (int i = 0; i < ITEM_COUNT; i++)
            {
                double angle = START_ANGLE + i * ANGLE_STEP;
                Point outerPoint = GetPoint(_outerRadius, angle);
                dc.DrawLine(pen, _center, outerPoint);
            }
            
            // 绘制环线（向内凹陷的弧形）
            double ringStep = _outerRadius / (rings + 1);
            
            for (int ring = 1; ring <= rings; ring++)
            {
                double radius = ring * ringStep;
                DrawConcaveRing(dc, pen, radius);
            }
            
            // 绘制最外层环线
            DrawConcaveRing(dc, pen, _outerRadius);
        }

        /// <summary>
        /// 绘制向内凹陷的环线
        /// </summary>
        private void DrawConcaveRing(DrawingContext dc, Pen pen, double radius)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                // 从第一个点开始
                Point firstPoint = GetPoint(radius, START_ANGLE);
                ctx.BeginFigure(firstPoint, false, true);
                
                // 每段弧线连接两个相邻的辐射点，使用二次贝塞尔曲线向内凹陷
                for (int i = 0; i < ITEM_COUNT; i++)
                {
                    double angle1 = START_ANGLE + i * ANGLE_STEP;
                    double angle2 = START_ANGLE + (i + 1) * ANGLE_STEP;
                    double midAngle = (angle1 + angle2) / 2;
                    
                    Point endPoint = GetPoint(radius, angle2);
                    
                    // 控制点向内凹陷（半径减小）
                    double concaveRadius = radius * 0.75; // 凹陷程度
                    Point controlPoint = GetPoint(concaveRadius, midAngle);
                    
                    ctx.QuadraticBezierTo(controlPoint, endPoint, true, false);
                }
            }
            geometry.Freeze();
            
            dc.DrawGeometry(null, pen, geometry);
        }

        /// <summary>
        /// 绘制高亮扇区
        /// </summary>
        private void DrawHighlightSector(DrawingContext dc, MenuItemPosition pos, Brush brush)
        {
            int idx = (int)pos;
            double angle1 = START_ANGLE + idx * ANGLE_STEP;
            double angle2 = angle1 + ANGLE_STEP;
            double midAngle = (angle1 + angle2) / 2;
            
            // 扇形区域（从中心到外边缘）
            StreamGeometry geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(_center, true, true);
                
                Point outer1 = GetPoint(_outerRadius, angle1);
                Point outer2 = GetPoint(_outerRadius, angle2);
                
                // 凹陷的外弧控制点
                double concaveRadius = _outerRadius * 0.75;
                Point controlPoint = GetPoint(concaveRadius, midAngle);
                
                ctx.LineTo(outer1, true, false);
                ctx.QuadraticBezierTo(controlPoint, outer2, true, false);
            }
            geometry.Freeze();
            
            dc.DrawGeometry(brush, null, geometry);
        }

        private Point GetPoint(double radius, double angle)
        {
            return new Point(
                _center.X + radius * Math.Cos(angle),
                _center.Y + radius * Math.Sin(angle)
            );
        }

        // 命中判定不在这里实现：GetSelectedItem 由基类 RadialMenu 统一提供
        // （「中心 → 指针」的方向落在哪个扇区就选哪个，与距离无关）。
        // 蛛网的判定因此和其余三种样式是**同一份代码**，本类只负责展示形式。

        public override void HighlightItem(MenuItemPosition itemPosition)
        {
            _highlightedPosition = itemPosition;
            InvalidateVisual();
        }

        public override void ClearHighlight()
        {
            _highlightedPosition = null;
            InvalidateVisual();
        }
    }
}