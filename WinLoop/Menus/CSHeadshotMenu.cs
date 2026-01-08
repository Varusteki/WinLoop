using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinLoop.Models;

namespace WinLoop.Menus
{
    #if false
    /// <summary>
    /// CF爆头图标菜单 - 中心骷髅头图片 + 外围八角星
    /// 参考CF穿越火线爆头图标样式
    /// </summary>
    public class CSHeadshotMenu : RadialMenu
    {
        private const int ITEM_COUNT = 8;
        private const double ANGLE_STEP = 2 * Math.PI / ITEM_COUNT;
        
        private MenuItemPosition? _highlightedPosition;
        private double _radius;
        private Point _center;
        private DrawingImage _skullImage;

        protected override void InitializeMenu()
        {
            _radius = Config.CSHeadshotMenuConfig.Radius;
            
            this.Width = _radius * 2.6;
            this.Height = _radius * 2.6;
            _center = new Point(_radius * 1.3, _radius * 1.3);
            
            // 加载骷髅头资源
            try
            {
                var dict = new ResourceDictionary();
                dict.Source = new Uri("pack://application:,,,/WinLoop;component/Resources/skull.xaml");
                _skullImage = dict["SkullIcon"] as DrawingImage;
            }
            catch { }
            
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            
            if (Config == null) return;
            
            Color lineColor = (Color)ColorConverter.ConvertFromString(Config.CSHeadshotMenuConfig.LineColor);
            Color highlightColor = (Color)ColorConverter.ConvertFromString(Config.CSHeadshotMenuConfig.HighlightColor);
            
            // 创建金属渐变效果
            Color brightColor = Color.FromArgb(255, 
                (byte)Math.Min(255, lineColor.R + 80), 
                (byte)Math.Min(255, lineColor.G + 80), 
                (byte)Math.Min(255, lineColor.B + 80));
            Color darkColor = Color.FromArgb(255,
                (byte)(lineColor.R * 0.5),
                (byte)(lineColor.G * 0.5),
                (byte)(lineColor.B * 0.5));
            
            Brush lineBrush = new SolidColorBrush(lineColor);
            Brush highlightBrush = new SolidColorBrush(Color.FromArgb(200, highlightColor.R, highlightColor.G, highlightColor.B));
            
            // 金属渐变画刷
            LinearGradientBrush metalBrush = new LinearGradientBrush();
            metalBrush.StartPoint = new Point(0, 0);
            metalBrush.EndPoint = new Point(1, 1);
            metalBrush.GradientStops.Add(new GradientStop(brightColor, 0.0));
            metalBrush.GradientStops.Add(new GradientStop(lineColor, 0.5));
            metalBrush.GradientStops.Add(new GradientStop(darkColor, 1.0));
            metalBrush.Freeze();
            
            Pen starPen = new Pen(lineBrush, 2.0);
            starPen.Freeze();
            
            Pen outlinePen = new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 3.5);
            outlinePen.Freeze();
            
            // 1. 绘制外发光效果（阴影）
            DrawGlowEffect(dc, lineColor);
            
            // 2. 绘制高亮区域
            if (_highlightedPosition.HasValue)
            {
                DrawHighlightSpike(dc, _highlightedPosition.Value, highlightBrush, lineBrush);
            }
            
            // 3. 绘制外围八角星尖角（带金属效果）
            DrawEightSpikes(dc, starPen, outlinePen, metalBrush, lineColor);
            
            // 4. 绘制中心骷髅头
            DrawSkullCenter(dc, lineBrush, metalBrush);
        }

        /// <summary>
        /// 绘制外发光效果
        /// </summary>
        private void DrawGlowEffect(DrawingContext dc, Color baseColor)
        {
            // 绘制多层光晕
            Color glowColor = Color.FromArgb(40, baseColor.R, baseColor.G, baseColor.B);
            for (int i = 3; i >= 1; i--)
            {
                Pen glowPen = new Pen(new SolidColorBrush(glowColor), i * 4);
                glowPen.Freeze();
                dc.DrawEllipse(null, glowPen, _center, _radius * 0.52, _radius * 0.52);
            }
        }

        /// <summary>
        /// 绘制八个尖角（从圆环边缘向外延伸）- CF风格锐利尖角
        /// </summary>
        private void DrawEightSpikes(DrawingContext dc, Pen pen, Pen outlinePen, Brush metalBrush, Color lineColor)
        {
            double innerR = _radius * 0.50;   // 圆环内径
            double outerR = _radius * 1.18;   // 尖角顶点（更尖锐）
            double spikeWidth = Math.PI / 14; // 尖角底部宽度（更窄更锐利）
            double midR = _radius * 0.75;     // 尖角中间凹陷点
            
            // 绘制内圆环（金属质感）
            Pen ringOutlinePen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 5);
            ringOutlinePen.Freeze();
            dc.DrawEllipse(null, ringOutlinePen, _center, innerR, innerR);
            
            Pen ringPen = new Pen(metalBrush, 3);
            ringPen.Freeze();
            dc.DrawEllipse(null, ringPen, _center, innerR, innerR);
            
            // 绘制8个锐利尖角
            for (int i = 0; i < 8; i++)
            {
                double angle = -Math.PI / 2 + i * ANGLE_STEP; // 从12点钟开始
                
                // 创建更锐利的尖角形状
                Point tip = GetPoint(outerR, angle);
                Point baseLeft = GetPoint(innerR, angle - spikeWidth);
                Point baseRight = GetPoint(innerR, angle + spikeWidth);
                
                // 中间凹陷点（让尖角看起来更像CF风格）
                Point midLeft = GetPoint(midR, angle - spikeWidth * 0.4);
                Point midRight = GetPoint(midR, angle + spikeWidth * 0.4);
                
                // 创建尖角渐变（从底部到顶点的渐变）
                Point tipScreen = tip;
                Point baseCenter = new Point((baseLeft.X + baseRight.X) / 2, (baseLeft.Y + baseRight.Y) / 2);
                
                LinearGradientBrush spikeBrush = new LinearGradientBrush();
                spikeBrush.StartPoint = new Point(0.5, 1);
                spikeBrush.EndPoint = new Point(0.5, 0);
                spikeBrush.GradientStops.Add(new GradientStop(Color.FromArgb(200, lineColor.R, lineColor.G, lineColor.B), 0.0));
                spikeBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 
                    (byte)Math.Min(255, lineColor.R + 60),
                    (byte)Math.Min(255, lineColor.G + 60),
                    (byte)Math.Min(255, lineColor.B + 60)), 0.7));
                spikeBrush.GradientStops.Add(new GradientStop(Colors.White, 1.0));
                spikeBrush.Freeze();
                
                // 绘制锐利的菱形尖角
                StreamGeometry geom = new StreamGeometry();
                using (var ctx = geom.Open())
                {
                    ctx.BeginFigure(baseLeft, true, true);
                    ctx.LineTo(midLeft, true, false);
                    ctx.LineTo(tip, true, false);
                    ctx.LineTo(midRight, true, false);
                    ctx.LineTo(baseRight, true, false);
                }
                geom.Freeze();
                
                // 先绘制描边（阴影效果）
                dc.DrawGeometry(null, outlinePen, geom);
                // 再绘制填充和边框
                dc.DrawGeometry(spikeBrush, pen, geom);
            }
            
            // 绘制小型装饰尖角（在主尖角之间）
            DrawDecorativeSpikes(dc, innerR, lineColor);
        }
        
        /// <summary>
        /// 绘制装饰性小尖角
        /// </summary>
        private void DrawDecorativeSpikes(DrawingContext dc, double innerR, Color lineColor)
        {
            double smallOuterR = _radius * 0.68;  // 小尖角顶点
            double smallWidth = Math.PI / 28;     // 小尖角宽度
            
            Brush smallBrush = new SolidColorBrush(Color.FromArgb(180, lineColor.R, lineColor.G, lineColor.B));
            Pen smallPen = new Pen(new SolidColorBrush(lineColor), 1);
            smallPen.Freeze();
            
            // 在主尖角之间绘制小尖角
            for (int i = 0; i < 8; i++)
            {
                double angle = -Math.PI / 2 + i * ANGLE_STEP + ANGLE_STEP / 2;
                
                Point tip = GetPoint(smallOuterR, angle);
                Point baseLeft = GetPoint(innerR, angle - smallWidth);
                Point baseRight = GetPoint(innerR, angle + smallWidth);
                
                StreamGeometry geom = new StreamGeometry();
                using (var ctx = geom.Open())
                {
                    ctx.BeginFigure(tip, true, true);
                    ctx.LineTo(baseLeft, true, false);
                    ctx.LineTo(baseRight, true, false);
                }
                geom.Freeze();
                
                dc.DrawGeometry(smallBrush, smallPen, geom);
            }
        }

        /// <summary>
        /// 绘制中心骷髅头
        /// </summary>
        private void DrawSkullCenter(DrawingContext dc, Brush defaultBrush, Brush metalBrush)
        {
            double skullSize = _radius * 0.90;
            
            // 绘制骷髅头底部阴影
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), null,
                new Point(_center.X + 2, _center.Y + 2), skullSize * 0.42, skullSize * 0.42);
            
            if (_skullImage != null)
            {
                // 使用矢量骷髅头图片
                Rect skullRect = new Rect(
                    _center.X - skullSize / 2,
                    _center.Y - skullSize / 2,
                    skullSize,
                    skullSize
                );
                dc.DrawImage(_skullImage, skullRect);
            }
            else
            {
                // 备用：代码绘制骷髅头
                DrawFallbackSkull(dc, defaultBrush, skullSize);
            }
        }

        /// <summary>
        /// 备用骷髅头绘制 - CF风格
        /// </summary>
        private void DrawFallbackSkull(DrawingContext dc, Brush brush, double size)
        {
            double r = size * 0.45;
            
            // 阴影
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), null,
                new Point(_center.X + 2, _center.Y + 2), r, r * 0.95);
            
            // 头部 - 使用渐变
            RadialGradientBrush skullBrush = new RadialGradientBrush();
            skullBrush.GradientOrigin = new Point(0.3, 0.3);
            skullBrush.Center = new Point(0.5, 0.5);
            skullBrush.RadiusX = 0.5;
            skullBrush.RadiusY = 0.5;
            skullBrush.GradientStops.Add(new GradientStop(Colors.White, 0.0));
            skullBrush.GradientStops.Add(new GradientStop(Color.FromRgb(230, 230, 230), 0.7));
            skullBrush.GradientStops.Add(new GradientStop(Color.FromRgb(180, 180, 180), 1.0));
            skullBrush.Freeze();
            
            dc.DrawEllipse(skullBrush, new Pen(Brushes.Gray, 1), _center, r, r * 0.95);
            
            // 下颌
            StreamGeometry jawGeom = new StreamGeometry();
            using (var ctx = jawGeom.Open())
            {
                ctx.BeginFigure(new Point(_center.X - r * 0.65, _center.Y + r * 0.3), true, true);
                ctx.LineTo(new Point(_center.X + r * 0.65, _center.Y + r * 0.3), true, false);
                ctx.LineTo(new Point(_center.X + r * 0.5, _center.Y + r * 0.9), true, false);
                ctx.LineTo(new Point(_center.X - r * 0.5, _center.Y + r * 0.9), true, false);
            }
            jawGeom.Freeze();
            dc.DrawGeometry(skullBrush, new Pen(Brushes.Gray, 1), jawGeom);
            
            // 眼睛（深黑色带渐变）
            RadialGradientBrush eyeBrush = new RadialGradientBrush();
            eyeBrush.GradientOrigin = new Point(0.5, 0.5);
            eyeBrush.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
            eyeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(30, 30, 30), 0.8));
            eyeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(50, 50, 50), 1.0));
            eyeBrush.Freeze();
            
            double eyeR = r * 0.28;
            double eyeX = r * 0.38;
            double eyeY = r * 0.1;
            
            dc.DrawEllipse(eyeBrush, null, new Point(_center.X - eyeX, _center.Y - eyeY), eyeR, eyeR * 1.15);
            dc.DrawEllipse(eyeBrush, null, new Point(_center.X + eyeX, _center.Y - eyeY), eyeR, eyeR * 1.15);
            
            // 右眼五角星（CF标志性）
            DrawStar(dc, Brushes.White, new Point(_center.X + eyeX, _center.Y - eyeY), eyeR * 0.7);
            
            // 鼻孔
            StreamGeometry noseGeom = new StreamGeometry();
            using (var ctx = noseGeom.Open())
            {
                ctx.BeginFigure(new Point(_center.X - r * 0.12, _center.Y + r * 0.2), true, true);
                ctx.LineTo(new Point(_center.X + r * 0.12, _center.Y + r * 0.2), true, false);
                ctx.LineTo(new Point(_center.X, _center.Y + r * 0.45), true, false);
            }
            noseGeom.Freeze();
            dc.DrawGeometry(eyeBrush, null, noseGeom);
            
            // 牙齿线
            Pen toothPen = new Pen(Brushes.DarkGray, 1.5);
            double teethY = _center.Y + r * 0.55;
            dc.DrawLine(toothPen, new Point(_center.X - r * 0.45, teethY), new Point(_center.X + r * 0.45, teethY));
            
            // 牙齿分隔
            for (int i = -2; i <= 2; i++)
            {
                double x = _center.X + i * r * 0.15;
                dc.DrawLine(toothPen, new Point(x, teethY), new Point(x, _center.Y + r * 0.85));
            }
        }

        /// <summary>
        /// 绘制五角星 - 更精致的CF风格
        /// </summary>
        private void DrawStar(DrawingContext dc, Brush fill, Point center, double size)
        {
            StreamGeometry geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                double inner = size * 0.4;
                bool first = true;
                for (int i = 0; i < 5; i++)
                {
                    double outerA = -Math.PI / 2 + i * (2 * Math.PI / 5);
                    double innerA = outerA + Math.PI / 5;
                    
                    Point outerP = new Point(center.X + size * Math.Cos(outerA), center.Y + size * Math.Sin(outerA));
                    Point innerP = new Point(center.X + inner * Math.Cos(innerA), center.Y + inner * Math.Sin(innerA));
                    
                    if (first) { ctx.BeginFigure(outerP, true, true); first = false; }
                    else { ctx.LineTo(outerP, true, false); }
                    ctx.LineTo(innerP, true, false);
                }
            }
            geom.Freeze();
            
            // 添加发光效果
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), null, geom);
            dc.DrawGeometry(fill, new Pen(Brushes.White, 0.5), geom);
        }

        /// <summary>
        /// 绘制高亮尖角 - 增强视觉效果
        /// </summary>
        private void DrawHighlightSpike(DrawingContext dc, MenuItemPosition pos, Brush brush, Brush outline)
        {
            int idx = (int)pos;
            double angle = -Math.PI / 2 + idx * ANGLE_STEP;
            
            double innerR = _radius * 0.50;
            double outerR = _radius * 1.22;  // 高亮时稍微放大
            double spikeWidth = Math.PI / 14;
            double midR = _radius * 0.75;
            
            Point tip = GetPoint(outerR, angle);
            Point baseLeft = GetPoint(innerR, angle - spikeWidth);
            Point baseRight = GetPoint(innerR, angle + spikeWidth);
            Point midLeft = GetPoint(midR, angle - spikeWidth * 0.4);
            Point midRight = GetPoint(midR, angle + spikeWidth * 0.4);
            
            // 绘制发光效果
            Color highlightColor = ((SolidColorBrush)brush).Color;
            for (int i = 3; i >= 1; i--)
            {
                StreamGeometry glowGeom = new StreamGeometry();
                using (var ctx = glowGeom.Open())
                {
                    double scale = 1 + i * 0.03;
                    Point gTip = GetPoint(outerR * scale, angle);
                    Point gBaseLeft = GetPoint(innerR, angle - spikeWidth * scale);
                    Point gBaseRight = GetPoint(innerR, angle + spikeWidth * scale);
                    
                    ctx.BeginFigure(gTip, true, true);
                    ctx.LineTo(gBaseLeft, true, false);
                    ctx.LineTo(gBaseRight, true, false);
                }
                glowGeom.Freeze();
                
                Color glowColor = Color.FromArgb((byte)(40 + i * 20), highlightColor.R, highlightColor.G, highlightColor.B);
                dc.DrawGeometry(new SolidColorBrush(glowColor), null, glowGeom);
            }
            
            // 绘制主高亮尖角
            StreamGeometry geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(baseLeft, true, true);
                ctx.LineTo(midLeft, true, false);
                ctx.LineTo(tip, true, false);
                ctx.LineTo(midRight, true, false);
                ctx.LineTo(baseRight, true, false);
            }
            geom.Freeze();
            
            // 高亮渐变
            LinearGradientBrush highlightGradient = new LinearGradientBrush();
            highlightGradient.StartPoint = new Point(0.5, 1);
            highlightGradient.EndPoint = new Point(0.5, 0);
            highlightGradient.GradientStops.Add(new GradientStop(highlightColor, 0.0));
            highlightGradient.GradientStops.Add(new GradientStop(Color.FromArgb(255,
                (byte)Math.Min(255, highlightColor.R + 50),
                (byte)Math.Min(255, highlightColor.G + 50),
                (byte)Math.Min(255, highlightColor.B + 50)), 0.6));
            highlightGradient.GradientStops.Add(new GradientStop(Colors.White, 1.0));
            highlightGradient.Freeze();
            
            dc.DrawGeometry(highlightGradient, new Pen(outline, 2), geom);
        }

        private Point GetPoint(double r, double angle)
        {
            return new Point(_center.X + r * Math.Cos(angle), _center.Y + r * Math.Sin(angle));
        }

        public override MenuItemPosition? GetSelectedItem(Point mousePosition)
        {
            // 菜单中心点是 (_radius * 1.3, _radius * 1.3)
            double centerX = _radius * 1.3;
            double centerY = _radius * 1.3;
            
            double dx = mousePosition.X - centerX;
            double dy = mousePosition.Y - centerY;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            
            // 在骷髅头内不选中，在外圈外也不选中
            if (dist < _radius * 0.45 || dist > _radius * 1.25)
                return null;
            
            // 计算角度（从12点钟方向开始，顺时针）
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
            if (angle < 0) angle += 360;
            
            int sector = (int)((angle + 22.5) / 45.0) % 8;
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
    public class CSHeadshotMenu : RadialMenu
    {
        protected override void InitializeMenu()
        {
            // Headshot menu disabled
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