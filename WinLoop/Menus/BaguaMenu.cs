using System;
using System.Windows;
using System.Windows.Media;
using WinLoop.Models;

namespace WinLoop.Menus
{
    #if false
    /// <summary>
    /// 八卦图菜单 - 正八边形样式（参考传统八卦图）
    /// 中心太极图 + 双线正八边形框架 + 填充矩形爻
    /// </summary>
    public class BaguaMenu : RadialMenu
    {
        private const int ITEM_COUNT = 8;
        private const double ANGLE_STEP = 2 * Math.PI / ITEM_COUNT;
        // 起始角度：让位置1（Top/北）的对称轴在12点钟方向
        private const double START_ANGLE = -Math.PI / 2 - ANGLE_STEP / 2;
        
        private MenuItemPosition? _highlightedPosition;
        private double _outerRadius;
        private Point _center;

        // 八卦爻码：1=阳爻(实线)，0=阴爻(断线)，从下到上
        // 后天八卦顺时针从北开始：坎、艮、震、巽、离、坤、兑、乾
        private static readonly int[][] Trigrams = new int[][]
        {
            new[] { 0, 1, 0 }, // 坎 ☵ (北/位置1-上)
            new[] { 1, 0, 0 }, // 艮 ☶ (东北/位置2)
            new[] { 0, 0, 1 }, // 震 ☳ (东/位置3)
            new[] { 0, 1, 1 }, // 巽 ☴ (东南/位置4)
            new[] { 1, 0, 1 }, // 离 ☲ (南/位置5-下)
            new[] { 0, 0, 0 }, // 坤 ☷ (西南/位置6)
            new[] { 1, 1, 0 }, // 兑 ☱ (西/位置7)
            new[] { 1, 1, 1 }, // 乾 ☰ (西北/位置8)
        };

        // 八卦名称
        private static readonly string[] TrigramNames = new string[]
        {
            "坎", "艮", "震", "巽", "离", "坤", "兑", "乾"
        };

        protected override void InitializeMenu()
        {
            _outerRadius = Config.BaguaMenuConfig.OuterRadius;
            
            this.Width = _outerRadius * 2.4;
            this.Height = _outerRadius * 2.4;
            _center = new Point(_outerRadius * 1.2, _outerRadius * 1.2);
            
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            
            if (Config == null) return;
            
            Color lineColor = (Color)ColorConverter.ConvertFromString(Config.BaguaMenuConfig.LineColor);
            
            // 透明度转换为不透明度 (transparency 0=不透明, 100=完全透明)
            // alpha = 255 * (100 - transparency) / 100
            byte defaultAlpha = (byte)((100 - Config.BaguaMenuConfig.SectorTransparency) * 255 / 100);
            byte highlightAlpha = (byte)((100 - Config.BaguaMenuConfig.HighlightTransparency) * 255 / 100);
            
            // 绘制正八边形框架（根据选中状态调整透明度）
            DrawOctagonFrame(dc, lineColor, defaultAlpha, highlightAlpha);
            
            // 绘制八卦符号（根据选中状态调整透明度）
            DrawTrigrams(dc, lineColor, defaultAlpha, highlightAlpha);
            
            // 绘制卦象名称（根据选中状态调整透明度）
            DrawTrigramNames(dc, lineColor, defaultAlpha, highlightAlpha);
            
            // 绘制中心太极图（始终100%不透明度）
            DrawTaiChi(dc, lineColor);
        }

        /// <summary>
        /// 绘制正八边形框架（根据选中状态调整各扇区边框透明度）
        /// </summary>
        private void DrawOctagonFrame(DrawingContext dc, Color lineColor, byte defaultAlpha, byte highlightAlpha)
        {
            // 绘制外层和内层八边形边框（按扇区分段，根据选中状态调整透明度）
            // 移除了最外层 _outerRadius，保留三层
            double[] radii = new double[] { _outerRadius * 0.95, _outerRadius * 0.52, _outerRadius * 0.38 };
            
            foreach (double radius in radii)
            {
                for (int i = 0; i < ITEM_COUNT; i++)
                {
                    double angle1 = START_ANGLE + i * ANGLE_STEP;
                    double angle2 = START_ANGLE + (i + 1) * ANGLE_STEP;
                    
                    Point p1 = GetOctagonPoint(radius, angle1);
                    Point p2 = GetOctagonPoint(radius, angle2);
                    
                    // 这条边属于扇区 i
                    bool isHighlighted = _highlightedPosition.HasValue && (int)_highlightedPosition.Value == i;
                    byte alpha = isHighlighted ? highlightAlpha : defaultAlpha;
                    
                    Brush brush = new SolidColorBrush(Color.FromArgb(alpha, lineColor.R, lineColor.G, lineColor.B));
                    Pen pen = new Pen(brush, 2);
                    dc.DrawLine(pen, p1, p2);
                }
            }
            
            // 绘制8条分割线（根据选中状态调整透明度）
            for (int i = 0; i < ITEM_COUNT; i++)
            {
                double angle = START_ANGLE + i * ANGLE_STEP;
                Point outer = GetOctagonPoint(_outerRadius * 0.95, angle);
                Point inner = GetOctagonPoint(_outerRadius * 0.38, angle);
                
                // 分割线属于两个相邻扇区，如果其中一个被选中则亮起
                bool leftHighlighted = _highlightedPosition.HasValue && (int)_highlightedPosition.Value == i;
                bool rightHighlighted = _highlightedPosition.HasValue && (int)_highlightedPosition.Value == (i + ITEM_COUNT - 1) % ITEM_COUNT;
                byte lineAlpha = (leftHighlighted || rightHighlighted) ? highlightAlpha : defaultAlpha;
                
                Brush lineBrush = new SolidColorBrush(Color.FromArgb(lineAlpha, lineColor.R, lineColor.G, lineColor.B));
                Pen linePen = new Pen(lineBrush, 2);
                dc.DrawLine(linePen, outer, inner);
            }
        }

        /// <summary>
        /// 绘制正八边形
        /// </summary>
        private void DrawOctagon(DrawingContext dc, Pen pen, double radius)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                Point first = GetOctagonPoint(radius, START_ANGLE);
                ctx.BeginFigure(first, false, true);
                
                for (int i = 1; i < ITEM_COUNT; i++)
                {
                    double angle = START_ANGLE + i * ANGLE_STEP;
                    Point p = GetOctagonPoint(radius, angle);
                    ctx.LineTo(p, true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        /// <summary>
        /// 获取正八边形顶点坐标
        /// </summary>
        private Point GetOctagonPoint(double radius, double angle)
        {
            return new Point(
                _center.X + radius * Math.Cos(angle),
                _center.Y + radius * Math.Sin(angle)
            );
        }

        /// <summary>
        /// 绘制八卦符号（填充矩形爻，根据选中状态调整不透明度）
        /// </summary>
        private void DrawTrigrams(DrawingContext dc, Color lineColor, byte defaultAlpha, byte highlightAlpha)
        {
            // 梯形边框范围是 0.52 到 0.95，让爻在其中居中并向内移动
            double innerR = _outerRadius * 0.54;  // 向内移动
            double outerR = _outerRadius * 0.85;  // 向内移动
            
            for (int i = 0; i < ITEM_COUNT; i++)
            {
                double centerAngle = START_ANGLE + i * ANGLE_STEP + ANGLE_STEP / 2;
                
                // 判断当前卦象是否被选中
                bool isHighlighted = _highlightedPosition.HasValue && (int)_highlightedPosition.Value == i;
                byte alpha = isHighlighted ? highlightAlpha : defaultAlpha;
                Brush fillBrush = new SolidColorBrush(Color.FromArgb(alpha, lineColor.R, lineColor.G, lineColor.B));
                
                DrawSingleTrigram(dc, fillBrush, Trigrams[i], centerAngle, innerR, outerR);
            }
        }

        /// <summary>
        /// 绘制单个卦象（3条爻，使用填充矩形）
        /// </summary>
        private void DrawSingleTrigram(DrawingContext dc, Brush fillBrush, int[] yao, double centerAngle, 
            double innerR, double outerR)
        {
            double totalHeight = outerR - innerR;
            double yaoHeight = totalHeight * 0.18;  // 爻的厚度
            double yaoSpacing = totalHeight * 0.08; // 爻之间的间距
            double yaoWidth = _outerRadius * 0.30;  // 爻的宽度
            
            // 计算三爻的起始位置（从内到外）
            double startR = innerR + totalHeight * 0.15;
            
            for (int j = 0; j < 3; j++)
            {
                double r = startR + j * (yaoHeight + yaoSpacing) + yaoHeight / 2;
                DrawYaoRect(dc, fillBrush, yao[j] == 1, centerAngle, r, yaoWidth, yaoHeight);
            }
        }

        /// <summary>
        /// 绘制一条爻（填充矩形，阴爻中间断开）
        /// </summary>
        private void DrawYaoRect(DrawingContext dc, Brush fillBrush, bool isYang, double centerAngle, double radius, double width, double height)
        {
            double halfWidth = width / 2;
            double halfHeight = height / 2;
            
            // 爻的方向垂直于径向
            double perpAngle = centerAngle + Math.PI / 2;
            Point center = GetOctagonPoint(radius, centerAngle);
            
            // 沿径向的偏移（爻的厚度方向）
            double radialDx = Math.Cos(centerAngle);
            double radialDy = Math.Sin(centerAngle);
            
            // 沿垂直方向的偏移（爻的宽度方向）
            double perpDx = Math.Cos(perpAngle);
            double perpDy = Math.Sin(perpAngle);
            
            if (isYang)
            {
                // 阳爻：完整矩形
                DrawRotatedRect(dc, fillBrush, center, halfWidth, halfHeight, perpDx, perpDy, radialDx, radialDy);
            }
            else
            {
                // 阴爻：两个矩形，中间断开
                double gapWidth = width * 0.15; // 中间间隙
                double sideWidth = (width - gapWidth) / 2;
                double sideHalfWidth = sideWidth / 2;
                double offset = (sideWidth + gapWidth) / 2;
                
                // 左边矩形
                Point leftCenter = new Point(
                    center.X + offset * perpDx,
                    center.Y + offset * perpDy
                );
                DrawRotatedRect(dc, fillBrush, leftCenter, sideHalfWidth, halfHeight, perpDx, perpDy, radialDx, radialDy);
                
                // 右边矩形
                Point rightCenter = new Point(
                    center.X - offset * perpDx,
                    center.Y - offset * perpDy
                );
                DrawRotatedRect(dc, fillBrush, rightCenter, sideHalfWidth, halfHeight, perpDx, perpDy, radialDx, radialDy);
            }
        }

        /// <summary>
        /// 绘制旋转的矩形
        /// </summary>
        private void DrawRotatedRect(DrawingContext dc, Brush fillBrush, Point center, 
            double halfWidth, double halfHeight, double perpDx, double perpDy, double radialDx, double radialDy)
        {
            // 四个角点
            Point p1 = new Point(
                center.X + halfWidth * perpDx + halfHeight * radialDx,
                center.Y + halfWidth * perpDy + halfHeight * radialDy
            );
            Point p2 = new Point(
                center.X - halfWidth * perpDx + halfHeight * radialDx,
                center.Y - halfWidth * perpDy + halfHeight * radialDy
            );
            Point p3 = new Point(
                center.X - halfWidth * perpDx - halfHeight * radialDx,
                center.Y - halfWidth * perpDy - halfHeight * radialDy
            );
            Point p4 = new Point(
                center.X + halfWidth * perpDx - halfHeight * radialDx,
                center.Y + halfWidth * perpDy - halfHeight * radialDy
            );
            
            StreamGeometry geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(p1, true, true);
                ctx.LineTo(p2, true, false);
                ctx.LineTo(p3, true, false);
                ctx.LineTo(p4, true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(fillBrush, null, geometry);
        }

        /// <summary>
        /// 绘制中心太极图
        /// </summary>
        private void DrawTaiChi(DrawingContext dc, Color lineColor)
        {
            double taichiRadius = _outerRadius * 0.32;
            
            Brush blackBrush = new SolidColorBrush(lineColor);
            Brush whiteBrush = Brushes.White;
            Pen outlinePen = new Pen(blackBrush, 1.5);
            
            // 外圆（白底）
            dc.DrawEllipse(whiteBrush, outlinePen, _center, taichiRadius, taichiRadius);
            
            // 黑色半圆（S曲线分割）
            StreamGeometry blackHalf = new StreamGeometry();
            using (var ctx = blackHalf.Open())
            {
                ctx.BeginFigure(new Point(_center.X, _center.Y - taichiRadius), true, true);
                // 大弧到底部（左半边）
                ctx.ArcTo(
                    new Point(_center.X, _center.Y + taichiRadius),
                    new Size(taichiRadius, taichiRadius),
                    0, false, SweepDirection.Counterclockwise, true, false);
                // 小弧回中心
                ctx.ArcTo(
                    new Point(_center.X, _center.Y),
                    new Size(taichiRadius / 2, taichiRadius / 2),
                    0, false, SweepDirection.Clockwise, true, false);
                // 小弧到顶部
                ctx.ArcTo(
                    new Point(_center.X, _center.Y - taichiRadius),
                    new Size(taichiRadius / 2, taichiRadius / 2),
                    0, false, SweepDirection.Counterclockwise, true, false);
            }
            blackHalf.Freeze();
            dc.DrawGeometry(blackBrush, null, blackHalf);
            
            // 阴阳眼
            double dotRadius = taichiRadius * 0.12;
            dc.DrawEllipse(whiteBrush, null, new Point(_center.X, _center.Y - taichiRadius / 2), dotRadius, dotRadius);
            dc.DrawEllipse(blackBrush, null, new Point(_center.X, _center.Y + taichiRadius / 2), dotRadius, dotRadius);
        }

        /// <summary>
        /// 绘制卦象名称（在阴阳鱼与各卦象之间的区域，文字朝向对应选区，根据选中状态调整不透明度）
        /// </summary>
        private void DrawTrigramNames(DrawingContext dc, Color lineColor, byte defaultAlpha, byte highlightAlpha)
        {
            double nameRadius = _outerRadius * 0.42; // 名称位置在太极图外、内框之间
            
            Typeface typeface = new Typeface(new FontFamily("SimSun, Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            double fontSize = _outerRadius * 0.10;
            
            for (int i = 0; i < ITEM_COUNT; i++)
            {
                double centerAngle = START_ANGLE + i * ANGLE_STEP + ANGLE_STEP / 2;
                Point namePos = GetOctagonPoint(nameRadius, centerAngle);
                
                // 判断当前卦象是否被选中
                bool isHighlighted = _highlightedPosition.HasValue && (int)_highlightedPosition.Value == i;
                byte alpha = isHighlighted ? highlightAlpha : defaultAlpha;
                Brush textBrush = new SolidColorBrush(Color.FromArgb(alpha, lineColor.R, lineColor.G, lineColor.B));
                
                FormattedText formattedText = new FormattedText(
                    TrigramNames[i],
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    textBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                
                // 计算旋转角度：使文字朝向对应选区（即朝外）
                // centerAngle 是从中心指向选区的角度，文字需要旋转使其朝向该方向
                double rotationDegrees = centerAngle * 180 / Math.PI + 90; // 加90度使文字底部朝向中心
                
                // 使用变换绘制旋转的文字
                dc.PushTransform(new TranslateTransform(namePos.X, namePos.Y));
                dc.PushTransform(new RotateTransform(rotationDegrees));
                dc.DrawText(formattedText, new Point(-formattedText.Width / 2, -formattedText.Height / 2));
                dc.Pop();
                dc.Pop();
            }
        }

        /// <summary>
        /// 绘制扇区填充（八边形扇区）- 目前未使用，保留备用
        /// </summary>
        private void DrawSectorFill(DrawingContext dc, MenuItemPosition pos, Brush brush)
        {
            int idx = (int)pos;
            double angle1 = START_ANGLE + idx * ANGLE_STEP;
            double angle2 = angle1 + ANGLE_STEP;
            
            double innerR = _outerRadius * 0.38;
            double outerR = _outerRadius * 0.95;
            
            // 八边形扇区
            StreamGeometry geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                Point inner1 = GetOctagonPoint(innerR, angle1);
                Point inner2 = GetOctagonPoint(innerR, angle2);
                Point outer1 = GetOctagonPoint(outerR, angle1);
                Point outer2 = GetOctagonPoint(outerR, angle2);
                
                ctx.BeginFigure(inner1, true, true);
                ctx.LineTo(outer1, true, false);
                ctx.LineTo(outer2, true, false);
                ctx.LineTo(inner2, true, false);
            }
            geometry.Freeze();
            
            dc.DrawGeometry(brush, null, geometry);
        }

        public override MenuItemPosition? GetSelectedItem(Point mousePosition)
        {
            // 菜单中心点是 (_outerRadius * 1.2, _outerRadius * 1.2)
            double centerX = _outerRadius * 1.2;
            double centerY = _outerRadius * 1.2;
            
            double dx = mousePosition.X - centerX;
            double dy = mousePosition.Y - centerY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            
            // 太极图区域或太远都不选中
            if (distance < _outerRadius * 0.35 || distance > _outerRadius * 1.1)
                return null;
            
            // 计算角度
            double angle = Math.Atan2(dy, dx);
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
    public class BaguaMenu : RadialMenu
    {
        protected override void InitializeMenu()
        {
            // Bagua menu disabled
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