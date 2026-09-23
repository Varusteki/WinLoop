using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using WinLoop.Models;

namespace WinLoop.Menus
{
    /// <summary>
    /// 八角星菜单（CS「爆头」徽记风格）。
    ///
    /// **三层都是可以独立成立的完整图元**，叠印时上层的白底把下层盖住：
    ///   ① 同心圆：两圈**完整**圆环
    ///   ② 八角星：**严格几何构造** —— 标准星多边形（尖 / 谷交替，全直线边）
    ///      再挖掉 8 个**三角孔**（每支尖被斜削一块，形成风车感）
    ///   ③ 骷髅头：描摹自用户提供的 `抠图骷髅头.png`（剪影白底 + 墨迹 EvenOdd）
    ///
    /// 参考图只作**样式参考**（见 <see cref="CSHeadshotLayers"/> 的生成脚本）：
    ///   - 星尖轴角用规整的 -90°+45°k；
    ///   - 星完全由几何构造，**不含任何描摹数据**（8 个尖等半径、8 个谷等半径、
    ///     8 个三角孔同参数），逐像素比对参考图差异率 3.0%；
    ///   - 骷髅的轮廓、眼、鼻、齿都按参考图描摹。
    ///
    /// 选中高亮只发生在第二层，取该格扇区内的星（画在黑星之上、三角孔之下），
    /// 再被第三层（骷髅）裁掉：
    ///   高亮 = 星多边形 ∩ 该格 45° 扇区（随后三角孔与骷髅会各裁掉一部分）
    /// 所以三角孔依然压在金色之上；纯色填充，不做渐变、不套辉光。
    ///
    /// 坐标系：原点 = 菜单中心，1.0 = 星尖顶点半径 R，y 轴向下为正。
    /// </summary>
    public class CSHeadshotMenu : RadialMenu
    {
        private const int ITEM_COUNT = SectorCount;             // 8 个方向

        // 角度基准与扇区张角全部取自基类 RadialMenu：
        // 判定与绘制因此共用同一份来源，不会再出现"一边改了另一边没改"而错位半个扇区。
        private const double SECTOR_DEG = SectorDeg;            // 45°
        private const double HALF_SECTOR_DEG = HalfSectorDeg;   // 22.5°

        /// <summary>第 0 项（Position1）在屏幕坐标下的方向：正上方。</summary>
        private const double BASE_ANGLE_DEG = FirstSectorAxisDeg;   // -90°

        // ---- 命中判定 ----
        // 本类不再自己实现判定：GetSelectedItem 由基类 RadialMenu 统一提供
        // （「中心 → 指针」的方向落在哪个扇区就选哪个，与距离无关）。
        // 八角星在这里没有任何特殊之处 —— 星臂、圆环空隙、中心骷髅都不参与判定。

        /// <summary>菜单半尺寸在图形最大半径上的额外留白，防止尖角被裁。</summary>
        private const double EXTENT_MARGIN = 1.02;

        /// <summary>盖接缝用的描边宽度（归一化单位）。</summary>
        private const double SEAM_STROKE = 0.006;

        /// <summary>高亮格内那道缝的暗金系数（对高亮色做线性压暗，0 = 全黑，1 = 同色）。</summary>
        private const double HIGHLIGHT_SHADOW_FACTOR = 0.62;

        // ---------- 静态几何缓存（与半径无关，只建一次） ----------
        private static readonly object Gate = new object();
        private static bool _geometryReady;

        private static Geometry _bodyGeometry;   // 整体外轮廓（底衬）
        private static Geometry _ringBody;       // ① 白底：环带
        private static Geometry _ringInk;        // ① 墨迹：两圈完整圆环
        private static Geometry _starBody;       // ② 黑：**严格几何**标准星多边形（尖/谷交替，全直线边）
        private static Geometry _starHoles;      // ② 白：8 个三角孔之并（底色填，会顺带把圆环切开）
        private static Geometry[] _starHoleShapes; // ② 每格那个三角孔（高亮时用它改画成暗金）
        private static Geometry _skullEdge;      // ③ 剪影整块（先填黑，做等宽描边的底色）
        private static Geometry _skullBody;      // ③ 剪影向内缩一圈（填白，露出一圈等宽黑边）
        private static Geometry _skullInk;       // ③ 墨迹：轮廓 + 五官（EvenOdd）
        private static Geometry[] _highlight;    // 每格高亮 = 星 ∩ 该格扇区（随后白孔会把它削掉）

        // ---------- 实例状态 ----------
        private double _scale;
        private double _deadZoneRadius;
        private Point _center;
        private MatrixTransform _toScreen;

        private Brush _inkBrush;
        private Pen _seamPen;            // 同色细描边：盖住层与层之间的抗锯齿接缝
        private Brush _bodyBrush;
        private Brush _highlightBrush;
        private Brush _highlightShadowBrush;   // 高亮格内那道缝的暗金色（= 高亮色压暗）

        private MenuItemPosition? _highlightedPosition;

        protected override void InitializeMenu()
        {
            var cfg = Config.CSHeadshotMenuConfig;
            double radius = Scaled(cfg.Radius > 0 ? cfg.Radius : CSHeadshotMenuConfig.BaseRadius);
            _scale = radius;

            // VisualRadius 是外部定位与命中的唯一依据，必须真实反映绘制范围
            double half = _scale * CSHeadshotPathData.MaxRadius * EXTENT_MARGIN;
            this.Width = half * 2;
            this.Height = half * 2;
            this.VisualRadius = half;
            _center = new Point(half, half);

            // 中心死区 = 底层同心圆内圈的外缘（归一化 RingInnerHigh），换算到本菜单的像素尺度。
            // 注意用 _scale 而不是 VisualRadius —— 同心圆是用 _scale 的矩阵画出来的，
            // VisualRadius 另乘了 MaxRadius × EXTENT_MARGIN（≈1.0346），两者不同源。
            _deadZoneRadius = _scale * CSHeadshotLayers.RingInnerHigh;

            var m = new MatrixTransform(_scale, 0, 0, _scale, _center.X, _center.Y);
            m.Freeze();
            _toScreen = m;

            _inkBrush = CreateBrush(cfg.LineColor, Colors.Black);
            _bodyBrush = CreateBrush(cfg.BodyColor, Color.FromRgb(0xFC, 0xFC, 0xFC));

            // 上层白底与下层墨迹交界处会有抗锯齿留下的细白缝，用同色细描边盖掉
            _seamPen = new Pen(_inkBrush, SEAM_STROKE) { LineJoin = PenLineJoin.Round };
            _seamPen.Freeze();

            // 高亮 = 纯色填充：不沿轴向做渐变，也不在外面套辉光
            Color hlColor = ParseColor(cfg.HighlightColor, Color.FromRgb(0xD4, 0xAF, 0x37));
            var highlight = new SolidColorBrush(hlColor);
            highlight.Freeze();
            _highlightBrush = highlight;

            // 高亮格内那道「缝」也一并处理：改画成暗金（阴影跟着臂一起变金），
            // 否则白色压在金色上就不像倒角、像个洞。系数固定，跟随 HighlightColor。
            var shadow = new SolidColorBrush(Darken(hlColor, HIGHLIGHT_SHADOW_FACTOR));
            shadow.Freeze();
            _highlightShadowBrush = shadow;

            EnsureGeometry();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (_toScreen == null || _starBody == null) return;

            dc.PushTransform(_toScreen);

            // 底衬：已置空（见 EnsureGeometry 里的说明），这段保留是为了配置项还能开关
            if (_bodyGeometry != null && _bodyGeometry != Geometry.Empty
                && _bodyBrush != null && _bodyBrush.Opacity > 0)
            {
                dc.DrawGeometry(_bodyBrush, null, _bodyGeometry);
            }

            // ① 同心圆：白底 -> 两圈完整圆环
            dc.DrawGeometry(_bodyBrush, null, _ringBody);
            dc.DrawGeometry(_inkBrush, _seamPen, _ringInk);

            // ② 八角星（严格几何）：标准星多边形(黑) -> [该格高亮] -> 8 个三角孔
            //    孔用底色填，所以会连圆环一起切开（与参考图一致）
            dc.DrawGeometry(_inkBrush, null, _starBody);

            if (_highlightedPosition.HasValue)
            {
                int index = (int)_highlightedPosition.Value;
                if (index >= 0 && index < ITEM_COUNT && _highlightBrush != null)
                {
                    dc.DrawGeometry(_highlightBrush, null, _highlight[index]);
                }
            }

            dc.DrawGeometry(_bodyBrush, null, _starHoles);

            // 高亮格那道缝改画成暗金：让「立体倒角」跟着臂一起变金
            if (_highlightedPosition.HasValue)
            {
                int hi = (int)_highlightedPosition.Value;
                if (hi >= 0 && hi < ITEM_COUNT && _highlightShadowBrush != null)
                {
                    dc.DrawGeometry(_highlightShadowBrush, null, _starHoleShapes[hi]);
                }
            }

            // ③ 骷髅头：整块填黑 -> 内缩一圈的白本体（露出等宽黑边）-> 墨迹
            //    白本体同时把下层墨迹与高亮一并裁掉
            dc.DrawGeometry(_inkBrush, null, _skullEdge);
            dc.DrawGeometry(_bodyBrush, null, _skullBody);
            dc.DrawGeometry(_inkBrush, _seamPen, _skullInk);

            dc.Pop();
        }

        // ===================== 几何构建 =====================

        private static void EnsureGeometry()
        {
            lock (Gate)
            {
                if (_geometryReady) return;
                _geometryReady = true;

                // ---- ① 同心圆：完整两圈 ----
                _ringBody = BuildAnnulus(CSHeadshotLayers.RingInnerLow, CSHeadshotLayers.RingOuterHigh);
                _ringInk = Combine(GeometryCombineMode.Union,
                    BuildAnnulus(CSHeadshotLayers.RingInnerLow, CSHeadshotLayers.RingInnerHigh),
                    BuildAnnulus(CSHeadshotLayers.RingOuterLow, CSHeadshotLayers.RingOuterHigh));

                // ---- ② 八角星（严格几何构造） ----
                // 黑 = 标准星多边形(尖 StarTipRadius / 谷 StarValleyRadius，全直线边)
                // 白 = 每支尖被斜削掉的一块三角孔
                //      [ (StarHoleTipRadius, 尖轴) ,
                //        (StarHoleMidRadius, 尖轴 + StarHoleAngleDeg) , (圆心) ]
                _starBody = BuildStarPolygon();
                _starHoleShapes = new Geometry[ITEM_COUNT];
                var holeGroup = new GeometryGroup { FillRule = FillRule.Nonzero };
                for (int i = 0; i < ITEM_COUNT; i++)
                {
                    _starHoleShapes[i] = BuildStarHole(i);
                    holeGroup.Children.Add(_starHoleShapes[i]);
                }
                holeGroup.Freeze();
                _starHoles = holeGroup;

                // ---- 底衬：按用户要求去掉 ----
                // 原来那层浅色打底会在尖角两侧露出一圈白边（深色壁纸上很明显），
                // 高亮时也没法干净地染金。直接置空，OnRender 里的底衬绘制会自动跳过。
                _bodyGeometry = Geometry.Empty;

                // ---- ③ 骷髅头：剪影（黑底 + 内缩白本体）+ 墨迹轮廓（EvenOdd） ----
                // 描摹出来的轮廓厚薄不均（顶部薄、两侧厚），外面兜一圈等宽黑边
                // （剪影填黑 -> 内缩 SkullStrokeWidth 的白本体）读起来才像参考图那样均匀。
                Geometry skullRim = BuildGeometry(
                    CSHeadshotLayers.Parse(CSHeadshotLayers.SkullSilhouetteLoops), FillRule.Nonzero)
                    ?? Geometry.Empty;
                double sw = CSHeadshotLayers.SkullStrokeWidth;
                var widenPen = new Pen(Brushes.Black, sw * 2.0) { LineJoin = PenLineJoin.Round };
                Geometry rimBand = skullRim.GetWidenedPathGeometry(widenPen);
                _skullEdge = skullRim;                                       // 先整块填黑
                _skullBody = Combine(GeometryCombineMode.Exclude, skullRim, rimBand);  // 内缩一圈填白
                _skullInk = BuildGeometry(
                    CSHeadshotLayers.Parse(CSHeadshotLayers.SkullInkLoops), FillRule.EvenOdd)
                    ?? Geometry.Empty;

                // ---- 每格高亮：星 ∩ 该格 45° 扇区（随后白孔会把它削掉） ----
                _highlight = new Geometry[ITEM_COUNT];
                for (int i = 0; i < ITEM_COUNT; i++)
                {
                    _highlight[i] = Combine(GeometryCombineMode.Intersect,
                        _starBody, BuildSector(BASE_ANGLE_DEG + i * SECTOR_DEG));
                }
            }
        }

        /// <summary>
        /// ② 八角星的黑体：标准星多边形 —— 8 个尖（半径 StarTipRadius，在 8 个格轴上）
        /// 与 8 个谷（半径 StarValleyRadius，在格轴 + 22.5° 的角平分线上）交替，**全部是直线边**。
        /// 这是「严格几何画法」的主体，不含任何描摹数据。
        /// </summary>
        private static Geometry BuildStarPolygon()
        {
            var figure = new PathFigure { IsClosed = true, IsFilled = true };
            for (int k = 0; k < ITEM_COUNT; k++)
            {
                double axis = BASE_ANGLE_DEG + k * SECTOR_DEG;
                Point tip = Polar(CSHeadshotLayers.StarTipRadius, axis);
                Point valley = Polar(CSHeadshotLayers.StarValleyRadius, axis + HALF_SECTOR_DEG);

                if (k == 0) figure.StartPoint = tip;
                else figure.Segments.Add(new LineSegment(tip, true));
                figure.Segments.Add(new LineSegment(valley, true));
            }

            var path = new PathGeometry();
            path.Figures.Add(figure);
            path.Freeze();
            return path;
        }

        /// <summary>
        /// ② 每支尖被斜削掉的那块**三角孔**：三个顶点依次为
        ///   (StarHoleTipRadius, 尖轴) -> (StarHoleMidRadius, 尖轴 + StarHoleAngleDeg) -> (圆心)。
        /// 画完黑星再用底色填这 8 块，每支尖都是「一边满、一边被斜切」，形成风车感；
        /// 因为是用底色填，它也会顺带把圆环切开（与参考图一致）。
        /// </summary>
        private static Geometry BuildStarHole(int index)
        {
            double axis = BASE_ANGLE_DEG + index * SECTOR_DEG;
            var figure = new PathFigure
            {
                StartPoint = new Point(0, 0),
                IsClosed = true,
                IsFilled = true
            };
            figure.Segments.Add(new LineSegment(
                Polar(CSHeadshotLayers.StarHoleTipRadius, axis), true));
            figure.Segments.Add(new LineSegment(
                Polar(CSHeadshotLayers.StarHoleMidRadius, axis + CSHeadshotLayers.StarHoleAngleDeg), true));

            var path = new PathGeometry();
            path.Figures.Add(figure);
            path.Freeze();
            return path;
        }

        /// <summary>某格的 45° 扇区（一个足够大的三角形，用来裁出该格的高亮）。</summary>
        private static Geometry BuildSector(double axisDeg)
        {
            const double reach = 4.0;   // 足够远，把整支尖罩住
            var figure = new PathFigure
            {
                StartPoint = new Point(0, 0),
                IsClosed = true,
                IsFilled = true
            };
            figure.Segments.Add(new LineSegment(Polar(reach, axisDeg - HALF_SECTOR_DEG), true));
            figure.Segments.Add(new LineSegment(Polar(reach, axisDeg + HALF_SECTOR_DEG), true));

            var path = new PathGeometry();
            path.Figures.Add(figure);
            path.Freeze();
            return path;
        }

        /// <summary>极坐标 -> 菜单坐标（deg 以 +x 为 0°，y 轴向下为正）。</summary>
        private static Point Polar(double r, double deg)
        {
            double a = deg * Math.PI / 180.0;
            return new Point(r * Math.Cos(a), r * Math.Sin(a));
        }

        /// <summary>做一次几何布尔运算并冻结。</summary>
        /// <summary>把颜色按 <paramref name="f"/> 线性压暗（0 = 全黑，1 = 原色）。</summary>
        private static Color Darken(Color c, double f)
        {
            if (f < 0) f = 0; else if (f > 1) f = 1;
            return Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
        }

        /// <summary>做一次几何布尔运算并冻结。</summary>
        private static Geometry Combine(GeometryCombineMode mode, Geometry a, Geometry b)
        {
            if (a == null || b == null) return Geometry.Empty;
            var g = new CombinedGeometry(mode, a, b);
            g.Freeze();
            return g;
        }

        /// <summary>圆环带：大圆挖掉小圆。</summary>
        private static Geometry BuildAnnulus(double rIn, double rOut)
        {
            var outer = new EllipseGeometry(new Point(0, 0), rOut, rOut);
            var inner = new EllipseGeometry(new Point(0, 0), rIn, rIn);
            var g = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            g.Freeze();
            return g;
        }

        /// <summary>把闭合折线数组拼成一个可填充的 Geometry。</summary>
        private static Geometry BuildGeometry(List<Point[]> loops, FillRule rule)
        {
            if (loops == null || loops.Count == 0) return null;

            var group = new GeometryGroup { FillRule = rule };
            foreach (Point[] pts in loops)
            {
                if (pts == null || pts.Length < 3) continue;

                var figure = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
                for (int i = 1; i < pts.Length; i++)
                {
                    figure.Segments.Add(new LineSegment(pts[i], true));
                }

                var path = new PathGeometry();
                path.Figures.Add(figure);
                group.Children.Add(path);
            }

            if (group.Children.Count == 0) return null;
            group.Freeze();
            return group;
        }

        // ===================== 颜色 / 画刷 =====================

        private static Brush CreateBrush(string text, Color fallback)
        {
            var brush = new SolidColorBrush(ParseColor(text, fallback));
            brush.Freeze();
            return brush;
        }

        /// <summary>解析 #RRGGBB / #AARRGGBB 之类的颜色文本，失败时返回 fallback。</summary>
        private static Color ParseColor(string text, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            try
            {
                var parsed = ColorConverter.ConvertFromString(text.Trim());
                if (parsed is Color color) return color;
            }
            catch
            {
                // 配置里写了非法颜色时退回默认值，不影响菜单可用性
            }
            return fallback;
        }

        // ===================== 交互 =====================

        // 命中判定不在这里实现：GetSelectedItem 由基类 RadialMenu 统一提供，
        // 四种样式的「选哪个扇区」因此天然完全一致，只有下面的「高亮画成什么形状」各不相同。

        /// <summary>
        /// 中心死区 = 底层同心圆的**内圆**（内圈环的外缘）之内，
        /// 也就是指针退回到那个"里圈"以里就取消选择。
        /// 取的是 <c>RingInnerHigh</c>（内圈的外边界），不是 RingInnerLow ——
        /// "内圆"指的是能被看见的那个圆的轮廓。
        /// </summary>
        public override double CenterDeadZoneRadius => _deadZoneRadius;

        /// <summary>
        /// 同心圆 / 星 / 剪影都是用 <c>_scale</c> 的矩阵画出来的，最外沿到归一化半径
        /// <c>CSHeadshotPathData.MaxRadius</c> 为止；<see cref="VisualRadius"/> 另乘了
        /// EXTENT_MARGIN（1.02），比"看得见的边"略大 —— 与 <c>_deadZoneRadius</c>
        /// 一样必须用 <c>_scale</c> 而不是 VisualRadius 做基准。
        /// </summary>
        public override double DrawnRadius => _scale * CSHeadshotPathData.MaxRadius;

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
