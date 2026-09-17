using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinLoop.UI
{
    /// <summary>
    /// 自绘颜色选择器（参照 PixPin）。
    ///
    /// 相比原先用的系统 <c>System.Windows.Forms.ColorDialog</c>，优势在于：
    /// - **SV 二维调色板**：饱和度/明度一次到位，所见即所得，不用在 RGB 分页里试数；
    /// - **色相滑杆**带实时游标，拖动时调色板跟着换色；
    /// - 色值与色块双向同步，粘贴色值立刻生效。
    ///
    /// 设计约束（与用户确认过）：
    /// - **不做屏幕吸管** —— 需要全局热键 + 截屏取色，工作量与风险都大，
    ///   且可能与 WinLoop 自身的热键机制冲突，先不引入；
    /// - **只输出 6 位不透明色** —— 渲染层（<c>ColorConverter.ConvertFromString</c>）
    ///   与配置字段都按 #RRGGBB 处理，不给渲染链路引入 alpha 复杂度。
    ///
    /// 输出契约：<see cref="SelectedHex"/> 一定是 "#RRGGBB" 大写 6 位格式。
    /// </summary>
    public partial class ColorPickerWindow : Window
    {
        // ── 调色板可用尺寸：**运行时实测**，不硬编码 ──
        // 窗口宽度受客户端边框、Padding 影响，写死数值会导致
        // 「鼠标能到的最右边」与「代码以为的最右边」不一致，
        // 表现为最右侧一整条取不到纯饱和色。改成 Loaded 后量 ActualWidth/Height。
        private double _svW = 1.0;
        private double _svH = 1.0;

        // ── 当前颜色状态（HSV 是"真相"，RGB 由它派生）──
        private double _hue;          // 0..360
        private double _sat;          // 0..1
        private double _val;          // 0..1
        private bool _suppressHexSync; // 防止 HexBox.TextChanged 与内部更新互相触发

        // ── 当前展示/解析格式 ──
        private enum ColorFormat { Hex, Rgb, Hsv, Hsl }
        private ColorFormat _format = ColorFormat.Hex;

        /// <summary>确认后的颜色，格式 "#RRGGBB"。取消时保持未设置。</summary>
        public string SelectedHex { get; private set; }

        /// <summary>
        /// 构造颜色选择器。
        /// </summary>
        /// <param name="initialHex">初始颜色（"#RRGGBB"），解析失败则退回红色。</param>
        public ColorPickerWindow(string initialHex)
        {
            InitializeComponent();

            SelectedHex = Normalize(initialHex);

            Color c = Parse(SelectedHex, Colors.Red);
            SetFromColor(c);

            Loaded += (s, e) =>
            {
                // 尺寸在 Loaded 后才可测，标记位置与布局都必须放到这时算
                MeasurePalette();

                // 默认格式 HEX（下拉第 0 项）。放在 Loaded 里赋值，
                // 避免 InitializeComponent 期间 SelectionChanged 拿到未完成布局的控件。
                ModeCombo.SelectedIndex = 0;

                RefreshSvBase();
                RefreshMarkers();
                SyncFormatBox();
                HexBox.Focus();
                HexBox.SelectAll();
            };

            // 窗口尺寸变化（含多屏拖动 DPI 切换）后重新测量
            SizeChanged += (s, e) =>
            {
                MeasurePalette();
                RefreshSvBase();
                RefreshMarkers();
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { DialogResult = false; Close(); }
                else if (e.Key == Key.Enter) { Accept(); }
            };
        }

        /// <summary>
        /// 读取调色板与色相条的实际可用尺寸。
        /// Canvas 不设 Width 时会被父级 StackPanel 横向撑满，
        /// 此时 ActualWidth 就是真实的可用宽度。
        /// </summary>
        private void MeasurePalette()
        {
            try
            {
                double w = SvCanvas.ActualWidth;
                double h = SvCanvas.ActualHeight;
                if (w > 1 && h > 1)
                {
                    _svW = w;
                    _svH = h;
                }

                // 三层视觉元素跟随 Canvas 实际尺寸铺满
                double hw = HueCanvas.ActualWidth > 1 ? HueCanvas.ActualWidth : _svW;
                HueBar.Width = hw;

                App.Log(string.Format(CultureInfo.InvariantCulture,
                    "ColorPicker measures: sv={0}x{1} hue={2} client={3}x{4}",
                    _svW, _svH, hw, ActualWidth, ActualHeight));
            }
            catch (Exception ex) { App.Log("ColorPicker MeasurePalette error: " + ex.Message); }
        }

        // ==================== 状态往返 ====================

        /// <summary>从 RGB 反推 HSV，写进内部状态。</summary>
        private void SetFromColor(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;

            _val = max;
            _sat = max <= 0 ? 0 : d / max;

            if (d <= 0) _hue = 0;
            else if (max == r) _hue = 60 * (((g - b) / d) % 6);
            else if (max == g) _hue = 60 * (((b - r) / d) + 2);
            else _hue = 60 * (((r - g) / d) + 4);

            if (_hue < 0) _hue += 360;
        }

        /// <summary>由 HSV 算出当前 RGB。</summary>
        private Color CurrentColor()
        {
            double h = _hue, s = _sat, v = _val;
            if (s <= 0)
            {
                byte g0 = (byte)Math.Round(v * 255);
                return Color.FromRgb(g0, g0, g0);
            }

            double hh = (h % 360) / 60.0;
            int i = (int)Math.Floor(hh);
            double f = hh - i;
            double p = v * (1 - s);
            double q = v * (1 - s * f);
            double t = v * (1 - s * (1 - f));

            double r, g, b;
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return Color.FromRgb(
                (byte)Math.Round(Math.Max(0, Math.Min(1, r)) * 255),
                (byte)Math.Round(Math.Max(0, Math.Min(1, g)) * 255),
                (byte)Math.Round(Math.Max(0, Math.Min(1, b)) * 255));
        }

        /// <summary>把 HSV 的"纯色相"换算成 Color，用于给调色板打底。</summary>
        private Color HueColor()
        {
            double h = _hue;
            double hh = (h % 360) / 60.0;
            int i = (int)Math.Floor(hh);
            double f = hh - i;
            double v = 1.0, s = 1.0;
            double p = v * (1 - s);      // = 0
            double q = v * (1 - s * f);
            double t = v * (1 - s * (1 - f));

            double r, g, b;
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromRgb(
                (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        // ==================== 界面刷新 ====================

        /// <summary>色相变化时重刷调色板底色（白色/黑色两层叠加是固定的，只换底）。</summary>
        private void RefreshSvBase()
        {
            try
            {
                // 三层 Rectangle/Border 会被 Canvas 自动拉伸，这里只保证不小于可用区，
                // 防止极端情况下（首次布局前）出现留白。
                SvBase.Width = _svW;
                SvBase.Height = _svH;
                SvWhite.Width = _svW;
                SvWhite.Height = _svH;
                SvBlack.Width = _svW;
                SvBlack.Height = _svH;

                var brush = new SolidColorBrush(HueColor());
                SvBase.Background = brush;
            }
            catch (Exception ex) { App.Log("ColorPicker RefreshSvBase error: " + ex.Message); }
        }

        /// <summary>把两个游标摆到当前 HSV 对应的位置。</summary>
        private void RefreshMarkers()
        {
            try
            {
                double mx = _sat * _svW;
                double my = (1 - _val) * _svH;
                Canvas.SetLeft(SvMarker, mx - SvMarker.Width / 2);
                Canvas.SetTop(SvMarker, my - SvMarker.Height / 2);

                double hw = HueBar.Width > 1 ? HueBar.Width : _svW;
                double hx = (_hue / 360.0) * hw;
                Canvas.SetLeft(HueMarker, hx - HueMarker.Width / 2);
            }
            catch (Exception ex) { App.Log("ColorPicker RefreshMarkers error: " + ex.Message); }
        }

        /// <summary>
        /// 把内部颜色按当前格式写回输入控件（带抑制标记，避免回环）。
        /// HEX 用单框，RGB/HSV/HSL 用三个分量框。
        /// </summary>
        private void SyncFormatBox()
        {
            try
            {
                _suppressHexSync = true;

                if (_format == ColorFormat.Hex)
                {
                    HexBox.Text = ToHex(CurrentColor());
                }
                else
                {
                    double[] v = CurrentComponents();
                    Comp1Box.Text = Fmt(v[0]);
                    Comp2Box.Text = Fmt(v[1]);
                    Comp3Box.Text = Fmt(v[2]);
                }

                UpdateHint();
            }
            finally { _suppressHexSync = false; }
        }

        /// <summary>当前格式下的三个分量值（HEX 不适用）。</summary>
        private double[] CurrentComponents()
        {
            Color c = CurrentColor();
            switch (_format)
            {
                case ColorFormat.Rgb:
                    return new double[] { c.R, c.G, c.B };

                case ColorFormat.Hsv:
                    return new double[] { _hue, _sat * 100, _val * 100 };

                case ColorFormat.Hsl:
                {
                    double h, s, l;
                    RgbToHsl(c, out h, out s, out l);
                    return new double[] { h, s * 100, l * 100 };
                }

                default:
                    return new double[] { 0, 0, 0 };
            }
        }

        /// <summary>分量显示：一律取整，不带小数位。</summary>
        private static string Fmt(double v)
        {
            return ((long)Math.Round(v, MidpointRounding.AwayFromZero))
                .ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>按当前格式切换「单框 / 三框」并刷新分量名。</summary>
        private void UpdateHint()
        {
            try
            {
                bool tri = _format != ColorFormat.Hex;
                HexPanel.Visibility = tri ? Visibility.Collapsed : Visibility.Visible;
                TriPanel.Visibility = tri ? Visibility.Visible : Visibility.Collapsed;
                CompLabelRow.Visibility = tri ? Visibility.Visible : Visibility.Collapsed;

                switch (_format)
                {
                    case ColorFormat.Rgb:
                        Comp1Label.Text = "R";
                        Comp2Label.Text = "G";
                        Comp3Label.Text = "B";
                        break;

                    case ColorFormat.Hsv:
                        Comp1Label.Text = "H";
                        Comp2Label.Text = "S";
                        Comp3Label.Text = "V";
                        break;

                    case ColorFormat.Hsl:
                        Comp1Label.Text = "H";
                        Comp2Label.Text = "S";
                        Comp3Label.Text = "L";
                        break;
                }
            }
            catch (Exception ex) { App.Log("ColorPicker UpdateHint error: " + ex.Message); }
        }

        /// <summary>按当前格式解析输入文本，成功则写进内部 HSV 状态。</summary>
        private bool TryParseByFormat(string text, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();

            switch (_format)
            {
                case ColorFormat.Rgb:
                {
                    double[] v;
                    if (!TryReadComponents(t, "rgb", 3, out v)) return false;
                    color = Color.FromRgb(Clamp255(v[0]), Clamp255(v[1]), Clamp255(v[2]));
                    return true;
                }

                case ColorFormat.Hsv:
                {
                    double[] v;
                    if (!TryReadComponents(t, "hsv", 3, out v)) return false;
                    color = HsvToColor(v[0], v[1] / 100.0, v[2] / 100.0);
                    return true;
                }

                case ColorFormat.Hsl:
                {
                    double[] v;
                    if (!TryReadComponents(t, "hsl", 3, out v)) return false;
                    color = HslToColor(v[0], v[1] / 100.0, v[2] / 100.0);
                    return true;
                }

                default:
                    return TryParse(t, out color);
            }
        }

        /// <summary>
        /// 从单个分量框读一个数（三个框各自调用）。
        /// 允许带 % 后缀、允许空（空当 0 处理，便于用户逐格清空重填）。
        /// </summary>
        private static bool TryReadOne(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return true;   // 空 = 0，不打断输入
            try
            {
                string s = text.Trim().TrimEnd('%').Trim();
                if (s.Length == 0) return true;
                return double.TryParse(s, NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out value);
            }
            catch { return false; }
        }

        /// <summary>
        /// 从字符串里抠出 3 个数字（供测试与粘贴整串时使用）。
        /// 函数名前缀可有可无、大小写不敏感；数字之间允许逗号或空格。
        /// </summary>
        private static bool TryReadComponents(string text, string fnName, int count, out double[] values)
        {
            values = null;
            try
            {
                string body = text;

                // 去掉可选的 "rgb(" / "hsv(" 前缀和右括号
                int lp = body.IndexOf('(');
                if (lp >= 0)
                {
                    string head = body.Substring(0, lp).Trim();
                    if (head.Length > 0 &&
                        !string.Equals(head, fnName, StringComparison.OrdinalIgnoreCase))
                        return false;
                    body = body.Substring(lp + 1);
                }
                body = body.TrimEnd(')', ' ');

                var parts = body.Split(new[] { ',', ' ', '\t', '/' },
                                       StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != count) return false;

                var arr = new double[count];
                for (int i = 0; i < count; i++)
                {
                    double d;
                    if (!double.TryParse(parts[i].TrimEnd('%'), NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out d))
                        return false;
                    arr[i] = d;
                }
                values = arr;
                return true;
            }
            catch { return false; }
        }

        /// <summary>由三个分量值构造颜色（分量框输入路径）。</summary>
        private bool TryColorFromComponents(double a, double b, double c, out Color color)
        {
            color = Colors.Black;
            switch (_format)
            {
                case ColorFormat.Rgb:
                    color = Color.FromRgb(Clamp255(a), Clamp255(b), Clamp255(c));
                    return true;
                case ColorFormat.Hsv:
                    color = HsvToColor(a, b / 100.0, c / 100.0);
                    return true;
                case ColorFormat.Hsl:
                    color = HslToColor(a, b / 100.0, c / 100.0);
                    return true;
                default:
                    return false;
            }
        }

        private static byte Clamp255(double v)
        {
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)Math.Round(v);
        }

        /// <summary>HSV(h 0–360, s 0–1, v 0–1) → Color。</summary>
        private static Color HsvToColor(double h, double s, double v)
        {
            if (s < 0) s = 0; if (s > 1) s = 1;
            if (v < 0) v = 0; if (v > 1) v = 1;
            h = ((h % 360) + 360) % 360;

            if (s <= 0)
            {
                byte g = Clamp255(v * 255);
                return Color.FromRgb(g, g, g);
            }

            double hh = h / 60.0;
            int i = (int)Math.Floor(hh);
            double f = hh - i;
            double p = v * (1 - s);
            double q = v * (1 - s * f);
            double t = v * (1 - s * (1 - f));

            double r, gg, b;
            switch (i)
            {
                case 0: r = v; gg = t; b = p; break;
                case 1: r = q; gg = v; b = p; break;
                case 2: r = p; gg = v; b = t; break;
                case 3: r = p; gg = q; b = v; break;
                case 4: r = t; gg = p; b = v; break;
                default: r = v; gg = p; b = q; break;
            }
            return Color.FromRgb(Clamp255(r * 255), Clamp255(gg * 255), Clamp255(b * 255));
        }

        /// <summary>HSL(h 0–360, s 0–1, l 0–1) → Color。</summary>
        private static Color HslToColor(double h, double s, double l)
        {
            if (s < 0) s = 0; if (s > 1) s = 1;
            if (l < 0) l = 0; if (l > 1) l = 1;
            h = ((h % 360) + 360) % 360;

            if (s <= 0)
            {
                byte g = Clamp255(l * 255);
                return Color.FromRgb(g, g, g);
            }

            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;

            double r = HueToRgb(p, q, h / 360.0 + 1.0 / 3.0);
            double gg = HueToRgb(p, q, h / 360.0);
            double b = HueToRgb(p, q, h / 360.0 - 1.0 / 3.0);

            return Color.FromRgb(Clamp255(r * 255), Clamp255(gg * 255), Clamp255(b * 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        /// <summary>Color → HSL(h 0–360, s 0–1, l 0–1)。</summary>
        private static void RgbToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;

            l = (max + min) / 2.0;
            if (d <= 0) { h = 0; s = 0; return; }

            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);

            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);

            if (h < 0) h += 360;
        }

        // ==================== 交互 ====================

        private bool _svDragging;

        private void SvCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _svDragging = true;
            ((UIElement)sender).CaptureMouse();
            ApplySvPoint(e.GetPosition(SvCanvas));
        }

        private void SvCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_svDragging) return;
            ApplySvPoint(e.GetPosition(SvCanvas));
        }

        private void SvCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _svDragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }

        /// <summary>把鼠标点换算成 饱和度/明度，并刷新标记与色值框。</summary>
        private void ApplySvPoint(Point p)
        {
            try
            {
                double x = Math.Max(0, Math.Min(_svW, p.X));
                double y = Math.Max(0, Math.Min(_svH, p.Y));
                _sat = x / _svW;
                _val = 1 - (y / _svH);
                RefreshMarkers();
                SyncFormatBox();
            }
            catch (Exception ex) { App.Log("ColorPicker ApplySvPoint error: " + ex.Message); }
        }

        private bool _hueDragging;

        private void HueCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _hueDragging = true;
            ((UIElement)sender).CaptureMouse();
            ApplyHuePoint(e.GetPosition(HueCanvas));
        }

        private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_hueDragging) return;
            ApplyHuePoint(e.GetPosition(HueCanvas));
        }

        private void HueCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _hueDragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }

        /// <summary>色相变化 → 重刷调色板底色 + 标记 + 色值。</summary>
        private void ApplyHuePoint(Point p)
        {
            try
            {
                double hw = HueBar.Width > 1 ? HueBar.Width : _svW;
                double x = Math.Max(0, Math.Min(hw, p.X));
                _hue = (x / hw) * 360.0;
                if (_hue >= 360) _hue = 359.999;
                RefreshSvBase();
                RefreshMarkers();
                SyncFormatBox();
            }
            catch (Exception ex) { App.Log("ColorPicker ApplyHuePoint error: " + ex.Message); }
        }

        /// <summary>
        /// 切换格式下拉：把当前颜色按新格式重写进输入框。
        ///
        /// 关键：**只重新渲染，不重新解析**。
        /// 内部 `_hue/_sat/_val` 始终是真相，切格式时直接由它算出新串显示。
        /// 如果反过来「读旧框里的文本 → 解析 → 覆盖状态」，那么整数显示
        /// （如 hsv(215, 73%, 84%)，真实值是 214.8/72.8/83.5）会在切换瞬间
        /// 把精度丢掉的近视值写回状态，颜色漂移 1 个色阶。
        /// 只要渲染方向是单向的，整数显示就不会造成任何损失。
        /// </summary>
        private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ModeCombo == null) return;

                switch (ModeCombo.SelectedIndex)
                {
                    case 1: _format = ColorFormat.Rgb; break;
                    case 2: _format = ColorFormat.Hsv; break;
                    case 3: _format = ColorFormat.Hsl; break;
                    default: _format = ColorFormat.Hex; break;
                }

                if (!IsLoaded) return;   // 初始化阶段还没布局，等 Loaded 统一刷

                SyncFormatBox();      // 单向：状态 -> 界面
                FocusPrimaryInput();
            }
            catch (Exception ex) { App.Log("ColorPicker ModeCombo_SelectionChanged error: " + ex.Message); }
        }

        /// <summary>焦点落到当前可见的第一个输入框。</summary>
        private void FocusPrimaryInput()
        {
            try
            {
                var tb = _format == ColorFormat.Hex ? HexBox : Comp1Box;
                tb.Focus();
                tb.SelectAll();
            }
            catch (Exception ex) { App.Log("ColorPicker FocusPrimaryInput error: " + ex.Message); }
        }

        /// <summary>
        /// 输入框手改（HEX 单框与三个分量框共用）。
        ///
        /// 关键点：**只把可见框的值当作真相来源**。
        /// 分量框三格各自触发 TextChanged，每次都用三格当前值整体重算，
        /// 这样改任意一格都能立即看到效果，且三格之间不会互相覆盖。
        /// 敲到一半（如只打了 "3"）不报错也不回退，等用户打完再解析。
        /// </summary>
        private void Component_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_suppressHexSync) return;

                Color c;

                if (_format == ColorFormat.Hex)
                {
                    string raw = (HexBox.Text ?? string.Empty).Trim();
                    if (!raw.StartsWith("#")) raw = "#" + raw;
                    if (raw.Length != 7 && raw.Length != 4) return;

                    if (!TryParse(raw, out c)) return;
                }
                else
                {
                    double a, b, d;
                    // 任一分量解析失败（还在输入中）就整体跳过，不打断用户
                    if (!TryReadOne(Comp1Box.Text, out a)) return;
                    if (!TryReadOne(Comp2Box.Text, out b)) return;
                    if (!TryReadOne(Comp3Box.Text, out d)) return;

                    if (!TryColorFromComponents(a, b, d, out c)) return;
                }

                SetFromColor(c);
                RefreshSvBase();
                RefreshMarkers();
            }
            catch (Exception ex) { App.Log("ColorPicker Component_TextChanged error: " + ex.Message); }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) { Accept(); }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try { DialogResult = false; } catch { }
            Close();
        }

        private void Accept()
        {
            try
            {
                SelectedHex = ToHex(CurrentColor());
                DialogResult = true;
            }
            catch (Exception ex)
            {
                App.Log("ColorPicker Accept error: " + ex.Message);
                Close();
            }
        }

        // ==================== 工具 ====================

        /// <summary>Color → "#RRGGBB"（大写，6 位，丢弃 alpha）。</summary>
        public static string ToHex(Color c)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        private static bool TryParse(string text, out Color color)
        {
            color = Colors.Black;
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return false;
                var t = text.Trim();
                if (!t.StartsWith("#")) t = "#" + t;

                // 只收 3 位简写与 6 位标准写法，避免把 8 位带 alpha 的值也接受进来
                if (t.Length != 4 && t.Length != 7) return false;

                var obj = ColorConverter.ConvertFromString(t);
                if (!(obj is Color)) return false;
                color = (Color)obj;
                return true;
            }
            catch { return false; }
        }

        /// <summary>解析输入色值，失败则返回 fallback。始终输出 6 位大写。</summary>
        public static string Normalize(string text)
        {
            Color c;
            if (TryParse(text, out c)) return ToHex(c);
            return ToHex(Colors.Red);
        }

        private static Color Parse(string text, Color fallback)
        {
            Color c;
            return TryParse(text, out c) ? c : fallback;
        }
    }
}
