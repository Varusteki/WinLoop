using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinLoop.Models;

namespace WinLoop.Menus
{
    public class BasicRadialMenu : RadialMenu
    {
        private readonly List<Path> _menuItems = new List<Path>();
        private Path _highlightedItem;
        private Path _ringPath; // 底层圆环
        private const int ITEM_COUNT = SectorCount;   // 8 个方向
        private const double ANGLE_STEP = 2 * Math.PI / ITEM_COUNT;   // 45°
        // 扇区起始边 = 第 0 个扇区（Position1）的中心 − 半个扇区。
        // 中心方向由基类统一给出（正上方），所以判定与绘制不会再错位半个扇区。
        private const double BASE_ANGLE = (FirstSectorAxisDeg - HalfSectorDeg) * Math.PI / 180.0;

        protected override void InitializeMenu()
        {
            double outerRadius = Scaled(Config.BasicRadialMenuConfig.OuterRadius);
            this.Width = outerRadius * 2;
            this.Height = outerRadius * 2;
            this.VisualRadius = outerRadius;

            // 内圆半径要与 DrawMenu 用的是同一个（厚度无效时退化为 外半径−内半径）
            double thickness = Scaled(Config.BasicRadialMenuConfig.Thickness);
            if (thickness <= 0) thickness = Math.Max(0, outerRadius - Scaled(Config.BasicRadialMenuConfig.InnerRadius));
            _innerRadius = Math.Max(0, outerRadius - thickness);

            DrawMenu();
        }

        /// <summary>
        /// 中心死区 = 内圆之内。圆环的可见图形就是「外圆 − 内圆」这条环带，
        /// 指针退回到内圆里面就当作反悔。
        /// </summary>
        public override double CenterDeadZoneRadius => _innerRadius;

        private double _innerRadius;

        private void DrawMenu()
        {
            this.Children.Clear();
            _menuItems.Clear();
            
            double outerRadius = Scaled(Config.BasicRadialMenuConfig.OuterRadius);
            double thickness = Scaled(Config.BasicRadialMenuConfig.Thickness);
            if (thickness <= 0) thickness = Math.Max(0, outerRadius - Scaled(Config.BasicRadialMenuConfig.InnerRadius));
            double innerRadius = Math.Max(0, outerRadius - thickness);
            
            // 解析颜色
            Brush ringBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Config.BasicRadialMenuConfig.RingColor));
            
            // 1. 首先绘制底层的完整圆环（类似Loop的样式）
            _ringPath = CreateRingPath(outerRadius, innerRadius, ringBrush);
            this.Children.Add(_ringPath);

            // 2. 创建透明的扇区用于检测点击和高亮（初始透明，高亮时填充）
            for (int i = 0; i < ITEM_COUNT; i++)
            {
                double startAngle = BASE_ANGLE + i * ANGLE_STEP;
                double endAngle = BASE_ANGLE + (i + 1) * ANGLE_STEP;
                
                Path path = CreateMenuSegment(startAngle, endAngle, outerRadius, innerRadius, Brushes.Transparent);
                path.Tag = (MenuItemPosition)i;
                
                _menuItems.Add(path);
                this.Children.Add(path);
            }
        }

        /// <summary>
        /// 创建一个完整的圆环Path
        /// </summary>
        private Path CreateRingPath(double outerRadius, double innerRadius, Brush fillBrush)
        {
            // 使用两个圆弧组成完整圆环
            PathGeometry geometry = new PathGeometry();
            
            // 外圆弧（上半圆）
            PathFigure outerFigure = new PathFigure();
            outerFigure.StartPoint = new Point(0, outerRadius); // 左侧
            outerFigure.Segments.Add(new ArcSegment(
                new Point(outerRadius * 2, outerRadius), // 右侧
                new Size(outerRadius, outerRadius),
                0, true, SweepDirection.Clockwise, true));
            outerFigure.Segments.Add(new ArcSegment(
                new Point(0, outerRadius), // 回到左侧
                new Size(outerRadius, outerRadius),
                0, true, SweepDirection.Clockwise, true));
            outerFigure.IsClosed = true;
            geometry.Figures.Add(outerFigure);
            
            // 内圆（镂空）- 使用 CombinedGeometry 或 GeometryGroup
            EllipseGeometry outerEllipse = new EllipseGeometry(new Point(outerRadius, outerRadius), outerRadius, outerRadius);
            EllipseGeometry innerEllipse = new EllipseGeometry(new Point(outerRadius, outerRadius), innerRadius, innerRadius);
            
            CombinedGeometry ringGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, outerEllipse, innerEllipse);
            
            Path path = new Path
            {
                Data = ringGeometry,
                Fill = fillBrush,
                Stroke = Brushes.Transparent,
                StrokeThickness = 0
            };
            
            return path;
        }

        private Path CreateMenuSegment(double startAngle, double endAngle, double outerRadius, double innerRadius, Brush fillBrush)
        {
            Point outerStart = new Point(
                outerRadius + outerRadius * Math.Cos(startAngle),
                outerRadius + outerRadius * Math.Sin(startAngle)
            );
            
            Point outerEnd = new Point(
                outerRadius + outerRadius * Math.Cos(endAngle),
                outerRadius + outerRadius * Math.Sin(endAngle)
            );
            
            Point innerStart = new Point(
                outerRadius + innerRadius * Math.Cos(startAngle),
                outerRadius + innerRadius * Math.Sin(startAngle)
            );
            
            Point innerEnd = new Point(
                outerRadius + innerRadius * Math.Cos(endAngle),
                outerRadius + innerRadius * Math.Sin(endAngle)
            );
            
            PathGeometry geometry = new PathGeometry();
            PathFigure figure = new PathFigure();
            
            figure.StartPoint = outerStart;
            figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(innerEnd, true));
            figure.Segments.Add(new ArcSegment(innerStart, new Size(innerRadius, innerRadius), 0, false, SweepDirection.Counterclockwise, true));
            figure.Segments.Add(new LineSegment(outerStart, true));
            figure.IsClosed = true;
            
            geometry.Figures.Add(figure);
            
            Path path = new Path
            {
                Data = geometry,
                Fill = fillBrush,
                Stroke = Brushes.Transparent,
                StrokeThickness = 0
            };
            
            return path;
        }

        // 命中判定不在这里实现：GetSelectedItem 由基类 RadialMenu 统一提供
        // （「中心 → 指针」的方向落在哪个扇区就选哪个，与距离无关）。
        // 圆环的判定因此和八角星、蜘蛛网、八卦是**同一份代码**，
        // 本类只负责「高亮画成环带扇区」这个展示形式。

        public override void HighlightItem(MenuItemPosition itemPosition)
        {
            // 先清除之前的高亮
            if (_highlightedItem != null)
            {
                ResetHighlight(_highlightedItem);
            }
            
            int index = (int)itemPosition;
            if (index >= 0 && index < _menuItems.Count)
            {
                _highlightedItem = _menuItems[index];
                // 高亮时用高亮色填充该扇区
                Brush highlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Config.BasicRadialMenuConfig.HighlightColor));
                _highlightedItem.Fill = highlightBrush;
            }
        }

        private void ResetHighlight(Path path)
        {
            // 重置为透明，露出底层的圆环
            path.Fill = Brushes.Transparent;
        }

        public override void ClearHighlight()
        {
            if (_highlightedItem != null)
            {
                ResetHighlight(_highlightedItem);
                _highlightedItem = null;
            }
        }
    }
}