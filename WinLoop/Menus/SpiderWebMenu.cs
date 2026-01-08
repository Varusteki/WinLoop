using System;
using System.Windows;
using System.Windows.Media;
using WinLoop.Models;

namespace WinLoop.Menus
{
    #if false
    /// <summary>
    /// 蜘蛛网菜单 - 参考真实蜘蛛网样式
    /// 8条辐射线 + 向内凹陷的弧形环线
    /// </summary>
    public class SpiderWebMenu : RadialMenu
    {
        private const int ITEM_COUNT = 8;
        private const double ANGLE_STEP = 2 * Math.PI / ITEM_COUNT;
        // 起始角度：让位置1（Top）的对称轴在12点钟方向
        // 辐射线在扇区边界，位置1中心需在-π/2，所以起始角度要减去半个扇区
        private const double START_ANGLE = -Math.PI / 2 - ANGLE_STEP / 2;
        
        private MenuItemPosition? _highlightedPosition;
        private double _outerRadius;
        private Point _center;

        protected override void InitializeMenu()
        {
            _outerRadius = Config.SpiderWebMenuConfig.OuterRadius;
            
            this.Width = _outerRadius * 2.2;
            this.Height = _outerRadius * 2.2;
            _center = new Point(_outerRadius * 1.1, _outerRadius * 1.1);
            
            InvalidateVisual();
        }

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

        public override MenuItemPosition? GetSelectedItem(Point mousePosition)
        {
            // 菜单中心点是 (_outerRadius * 1.1, _outerRadius * 1.1)
            double centerX = _outerRadius * 1.1;
            double centerY = _outerRadius * 1.1;
            
            double dx = mousePosition.X - centerX;
            double dy = mousePosition.Y - centerY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            
            // 太靠近中心或太远都不选中
            if (distance < _outerRadius * 0.15 || distance > _outerRadius * 1.1)
                return null;
            
            // 计算角度，考虑START_ANGLE的偏移
            double angle = Math.Atan2(dy, dx);
            // 将角度转换为相对于START_ANGLE的位置
            double relativeAngle = angle - START_ANGLE;
            if (relativeAngle < 0) relativeAngle += 2 * Math.PI;
            
            int sector = (int)(relativeAngle / ANGLE_STEP) % 8;
            return (MenuItemPosition)sector;
        }

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
    #endif

    // Stubbed to disable non-ring style while keeping compile compatibility
    public class SpiderWebMenu : RadialMenu
    {
        protected override void InitializeMenu()
        {
            // Spider web menu disabled
        }

        public override MenuItemPosition? GetSelectedItem(Point mousePosition) => null;

        public override void HighlightItem(MenuItemPosition itemPosition)
        {
            // No-op
        }

        public override void ClearHighlight()
        {
            // No-op
        }
    }
}