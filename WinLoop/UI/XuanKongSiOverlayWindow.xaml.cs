using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using WinLoop.Models;

namespace WinLoop.UI
{
    public partial class XuanKongSiOverlayWindow : Window
    {
        public event EventHandler HiddenCompleted;

        private bool _isHiding;
        private SlideEdge _edge = SlideEdge.Left;
        private bool _pendingShowAnimation;
        private DispatcherTimer _showFailSafeTimer;

        public XuanKongSiOverlayWindow()
        {
            InitializeComponent();
            Loaded += XuanKongSiOverlayWindow_Loaded;
        }

        private void XuanKongSiOverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_pendingShowAnimation) return;
            _pendingShowAnimation = false;
            BeginShowAnimation(_edge);
        }

        public void ShowLayout(XuanKongSiConfig config)
        {
            var fallback = new XuanKongSiConfig();
            var cfg = config ?? fallback;

            _edge = GetSlideEdge(cfg.TriggerKey);
            _pendingShowAnimation = true;

            try
            {
                App.Log($"XuanKongSiOverlay ShowLayout: trigger={cfg.TriggerKey}, edge={_edge}, content={cfg.ContentType}");
            }
            catch { }

            try
            {
                OperationHint.Text = $"双击 {FormatTriggerKey(cfg.TriggerKey)} 显示，按 ESC 隐藏";
                OperationHint.Opacity = 0.95;
            }
            catch { }

            PrepareCardPosition(_edge);

            RenderXuanKongSiContent(cfg);

            var screenBounds = GetActiveScreenBoundsDip();
            Left = screenBounds.Left;
            Top = screenBounds.Top;
            Width = screenBounds.Width;
            Height = screenBounds.Height;

            ApplyContentViewportConstraints();

            Show();
            Activate();

            StartShowFailSafe();

            // If already loaded, animate immediately; otherwise Loaded event will do it.
            try
            {
                if (IsLoaded)
                {
                    _pendingShowAnimation = false;
                    BeginShowAnimation(_edge);
                }
            }
            catch { }
        }

        private void ApplyContentViewportConstraints()
        {
            try
            {
                // The card has padding + title + hint + margins; reserve some space so content area
                // never grows beyond the screen height. This enables vertical scrolling inside.
                var h = Height;
                if (h <= 0) h = SystemParameters.PrimaryScreenHeight;

                // Conservative reserved height for header/padding.
                var max = h - 240;
                if (max < 220) max = 220;

                try { ImageContainer.MaxHeight = max; } catch { }
                try { TextContainer.MaxHeight = max; } catch { }
                try { LayoutImage.MaxHeight = max; } catch { }
            }
            catch { }
        }

        private void RenderXuanKongSiContent(XuanKongSiConfig cfg)
        {
            try
            {
                // Reset containers
                try { ImageContainer.Visibility = Visibility.Collapsed; } catch { }
                try { TextContainer.Visibility = Visibility.Collapsed; } catch { }

                switch (cfg.ContentType)
                {
                    case XuanKongSiContentType.Text:
                        RenderText(cfg);
                        break;
                    case XuanKongSiContentType.Image:
                    default:
                        RenderImage(cfg);
                        break;
                }
            }
            catch { }
        }

        private static string GetConfigRootDir()
        {
            try
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(baseDir, "WinLoop");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetMediaDir()
        {
            var root = GetConfigRootDir();
            if (string.IsNullOrEmpty(root)) return string.Empty;
            return Path.Combine(root, "Media");
        }

        private void RenderImage(XuanKongSiConfig cfg)
        {
            try
            {
                ImageContainer.Visibility = Visibility.Visible;
                TextContainer.Visibility = Visibility.Collapsed;

                // 1) Custom image from config dir
                if (!string.IsNullOrWhiteSpace(cfg.ImageFileName))
                {
                    var mediaDir = GetMediaDir();
                    var p = string.IsNullOrEmpty(mediaDir) ? null : Path.Combine(mediaDir, cfg.ImageFileName);
                    if (!string.IsNullOrEmpty(p) && File.Exists(p) && TrySetImageSource(p))
                    {
                        return;
                    }
                }

                // 2) Default: Xiaohe keyboard image from app resources folder
                var appBase = AppDomain.CurrentDomain.BaseDirectory;
                var defaultDir = Path.Combine(appBase, "Resources", "XuanKongSi");
                var xiaohe = Path.Combine(defaultDir, "小鹤双拼-键位图.png");
                if (File.Exists(xiaohe) && TrySetImageSource(xiaohe))
                {
                    return;
                }

                // 3) Any image in Resources\XuanKongSi
                try
                {
                    if (Directory.Exists(defaultDir))
                    {
                        var any = Directory.GetFiles(defaultDir, "*.png");
                        if (any != null && any.Length > 0)
                        {
                            TrySetImageSource(any[0]);
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        private bool TrySetImageSource(string filePath)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(filePath, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                LayoutImage.Source = bi;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RenderText(XuanKongSiConfig cfg)
        {
            try
            {
                ImageContainer.Visibility = Visibility.Collapsed;
                TextContainer.Visibility = Visibility.Visible;

                FlowDocument doc = null;
                if (!string.IsNullOrWhiteSpace(cfg.TextXaml))
                {
                    try
                    {
                        var t = cfg.TextXaml.TrimStart();
                        if (t.StartsWith("<FlowDocument", StringComparison.OrdinalIgnoreCase))
                        {
                            var parsed = XamlReader.Parse(cfg.TextXaml);
                            doc = parsed as FlowDocument;
                        }
                        else
                        {
                            doc = BuildFlowDocumentFromMarkdown(cfg.TextXaml);
                        }
                    }
                    catch { }
                }

                if (doc == null)
                {
                    doc = new FlowDocument();
                    doc.Blocks.Add(new Paragraph(new Run("(未配置文字内容)")));
                }

                RichTextViewer.Document = doc;
            }
            catch { }
        }

        private FlowDocument BuildFlowDocumentFromMarkdown(string markdown)
        {
            var doc = new FlowDocument();

            var md = markdown ?? string.Empty;
            var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.None);

            bool inCodeBlock = false;
            var codeLines = new List<string>();
            List bulletList = null;

            Action flushCode = () =>
            {
                if (!inCodeBlock) return;
                inCodeBlock = false;
                var text = string.Join("\n", codeLines);
                codeLines.Clear();

                var p = new Paragraph();
                var run = new Run(text);
                run.FontFamily = new FontFamily("Consolas");
                p.Inlines.Add(run);
                doc.Blocks.Add(p);
            };

            Action flushList = () =>
            {
                if (bulletList == null) return;
                doc.Blocks.Add(bulletList);
                bulletList = null;
            };

            foreach (var raw in lines)
            {
                var line = raw ?? string.Empty;
                var trimmed = line.TrimEnd();

                if (trimmed.Trim() == "```")
                {
                    if (inCodeBlock)
                    {
                        flushCode();
                    }
                    else
                    {
                        flushList();
                        inCodeBlock = true;
                        codeLines.Clear();
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeLines.Add(line);
                    continue;
                }

                // Empty line => paragraph break
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    flushList();
                    continue;
                }

                // Headings
                if (trimmed.StartsWith("### "))
                {
                    flushList();
                    doc.Blocks.Add(MakeHeading(trimmed.Substring(4), 16));
                    continue;
                }
                if (trimmed.StartsWith("## "))
                {
                    flushList();
                    doc.Blocks.Add(MakeHeading(trimmed.Substring(3), 18));
                    continue;
                }
                if (trimmed.StartsWith("# "))
                {
                    flushList();
                    doc.Blocks.Add(MakeHeading(trimmed.Substring(2), 20));
                    continue;
                }

                // Bullet list
                var ltrim = trimmed.TrimStart();
                if (ltrim.StartsWith("- ") || ltrim.StartsWith("* ") || ltrim.StartsWith("+ "))
                {
                    var itemText = ltrim.Substring(2);
                    if (bulletList == null) bulletList = new List();
                    var li = new ListItem();
                    li.Blocks.Add(MakeParagraphFromMarkdownInlines(itemText));
                    bulletList.ListItems.Add(li);
                    continue;
                }

                flushList();
                doc.Blocks.Add(MakeParagraphFromMarkdownInlines(trimmed));
            }

            flushList();
            if (inCodeBlock) flushCode();

            if (doc.Blocks.Count == 0)
            {
                doc.Blocks.Add(new Paragraph(new Run("(未配置文字内容)")));
            }

            return doc;
        }

        private Paragraph MakeHeading(string text, double fontSize)
        {
            var p = new Paragraph();
            p.Margin = new Thickness(0, 0, 0, 8);
            var run = new Run((text ?? string.Empty).Trim());
            run.FontSize = fontSize;
            run.FontWeight = FontWeights.SemiBold;
            p.Inlines.Add(run);
            return p;
        }

        private Paragraph MakeParagraphFromMarkdownInlines(string text)
        {
            var p = new Paragraph();
            foreach (var inline in ParseMarkdownInlines(text ?? string.Empty))
            {
                p.Inlines.Add(inline);
            }
            return p;
        }

        private IEnumerable<Inline> ParseMarkdownInlines(string text)
        {
            var inlines = new List<Inline>();
            var s = text ?? string.Empty;
            int i = 0;

            void addText(string t)
            {
                if (string.IsNullOrEmpty(t)) return;
                inlines.Add(new Run(t));
            }

            while (i < s.Length)
            {
                // Inline code `code`
                if (s[i] == '`')
                {
                    var end = s.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        var code = s.Substring(i + 1, end - i - 1);
                        var run = new Run(code) { FontFamily = new FontFamily("Consolas") };
                        inlines.Add(run);
                        i = end + 1;
                        continue;
                    }
                }

                // Link [text](url)
                if (s[i] == '[')
                {
                    var close = s.IndexOf(']', i + 1);
                    if (close > i && close + 1 < s.Length && s[close + 1] == '(')
                    {
                        var close2 = s.IndexOf(')', close + 2);
                        if (close2 > close)
                        {
                            var label = s.Substring(i + 1, close - i - 1);
                            var url = s.Substring(close + 2, close2 - (close + 2));
                            var link = new Hyperlink(new Run(label));
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(url)) link.NavigateUri = new Uri(url, UriKind.RelativeOrAbsolute);
                            }
                            catch { }
                            link.RequestNavigate += (sender, e) =>
                            {
                                try
                                {
                                    if (e.Uri != null)
                                    {
                                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                                    }
                                }
                                catch { }
                            };
                            inlines.Add(link);
                            i = close2 + 1;
                            continue;
                        }
                    }
                }

                // Bold **text**
                if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*')
                {
                    var end = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i)
                    {
                        var inner = s.Substring(i + 2, end - (i + 2));
                        var run = new Run(inner) { FontWeight = FontWeights.Bold };
                        inlines.Add(run);
                        i = end + 2;
                        continue;
                    }
                }

                // Italic *text*
                if (s[i] == '*')
                {
                    var end = s.IndexOf('*', i + 1);
                    if (end > i)
                    {
                        var inner = s.Substring(i + 1, end - i - 1);
                        var run = new Run(inner) { FontStyle = FontStyles.Italic };
                        inlines.Add(run);
                        i = end + 1;
                        continue;
                    }
                }

                // Plain text chunk
                var next = NextSpecialIndex(s, i);
                if (next < 0) next = s.Length;

                // If current char is "special" but didn't match any supported construct,
                // consume it as plain text to avoid an infinite loop.
                if (next == i)
                {
                    addText(s[i].ToString());
                    i++;
                    continue;
                }

                addText(s.Substring(i, next - i));
                i = next;
            }

            return inlines;
        }

        private int NextSpecialIndex(string s, int start)
        {
            if (string.IsNullOrEmpty(s) || start >= s.Length) return -1;
            for (int i = start; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '`' || c == '[' || c == '*') return i;
            }
            return -1;
        }

        private System.Drawing.Rectangle GetActiveScreenBounds()
        {
            try
            {
                var pos = System.Windows.Forms.Cursor.Position;
                var screen = System.Windows.Forms.Screen.FromPoint(pos);
                return screen != null ? screen.Bounds : System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            }
            catch
            {
                try
                {
                    return System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                }
                catch
                {
                    // Fallback to WPF primary screen metrics.
                    return new System.Drawing.Rectangle(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
                }
            }
        }

        private Rect GetActiveScreenBoundsDip()
        {
            // WinForms Screen.Bounds is in physical pixels; WPF Window.Left/Top/Width/Height expects DIPs.
            // Use system DPI scale as a practical conversion.
            var px = GetActiveScreenBounds();
            var scale = GetSystemDpiScale();
            if (scale <= 0) scale = 1.0;

            return new Rect(px.Left / scale, px.Top / scale, px.Width / scale, px.Height / scale);
        }

        private double GetSystemDpiScale()
        {
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / 96.0;
                }
            }
            catch
            {
                return 1.0;
            }
        }

        private void StartShowFailSafe()
        {
            try
            {
                if (_showFailSafeTimer != null)
                {
                    _showFailSafeTimer.Stop();
                    _showFailSafeTimer = null;
                }

                // Give the slide-in animation enough time to complete before applying a fail-safe.
                _showFailSafeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                _showFailSafeTimer.Tick += (s, e) =>
                {
                    try
                    {
                        _showFailSafeTimer.Stop();
                        _showFailSafeTimer = null;

                        // If card is still off-screen, force it into view.
                        var stillOffscreen = false;
                        try { stillOffscreen = Math.Abs(CardTransform.X) > 200; } catch { }

                        if (stillOffscreen)
                        {
                            CardTransform.X = 0;
                            CardHost.Opacity = 0.97;
                            try { OperationHint.Opacity = 0.95; } catch { }
                            App.Log("XuanKongSiOverlay fail-safe applied: forced card into view");
                        }
                    }
                    catch { }
                };
                _showFailSafeTimer.Start();
            }
            catch { }
        }

        public void HideOverlayAnimated()
        {
            if (_isHiding) return;
            _isHiding = true;

            try
            {
                BeginHideAnimation(_edge, () =>
                {
                    try { Hide(); } catch { }
                    try
                    {
                        var handler = HiddenCompleted;
                        if (handler != null) handler(this, EventArgs.Empty);
                    }
                    catch { }
                    finally
                    {
                        _isHiding = false;
                    }
                });
            }
            catch
            {
                try { Hide(); } catch { }
                try
                {
                    var handler = HiddenCompleted;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
                catch { }
                finally
                {
                    _isHiding = false;
                }
            }
        }

        private bool TryLoadSchemeImage(XuanKongSiScheme scheme)
        {
            try
            {
                // Candidates are tried in order; this allows users to use their own file names.
                foreach (var candidate in GetSchemeImageFileNameCandidates(scheme))
                {
                    if (string.IsNullOrEmpty(candidate)) continue;

                    // 1) Try as WPF pack resource (if user sets Build Action=Resource).
                    //    pack://application:,,,/Resources/XuanKongSi/<file>
                    try
                    {
                        var packUri = new Uri("pack://application:,,,/Resources/XuanKongSi/" + candidate, UriKind.Absolute);
                        var packStreamInfo = Application.GetResourceStream(packUri);
                        if (packStreamInfo != null)
                        {
                            LayoutImage.Source = LoadBitmapFromStream(packStreamInfo.Stream);
                            if (LayoutImage.Source != null) return true;
                        }
                    }
                    catch
                    {
                        // Ignore and continue.
                    }
                }

                // 2) Try as external file near app base directory (publish/installer-friendly).
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dirPath = Path.Combine(baseDir, "Resources", "XuanKongSi");
                if (!Directory.Exists(dirPath)) return false;

                // Prefer best match for the current scheme.
                var selected = SelectBestSchemeImageFile(dirPath, scheme);
                if (!string.IsNullOrEmpty(selected) && File.Exists(selected))
                {
                    try { App.Log($"XuanKongSiOverlay image selected: {selected}"); } catch { }
                    LayoutImage.Source = LoadBitmapFromFile(selected);
                    try { App.Log($"XuanKongSiOverlay image load result: {(LayoutImage.Source != null ? "ok" : "null")}"); } catch { }
                    return LayoutImage.Source != null;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<string> GetSchemeImageFileNameCandidates(XuanKongSiScheme scheme)
        {
            switch (scheme)
            {
                case XuanKongSiScheme.Microsoft:
                    return new[] { "microsoft.png", "Microsoft.png", "weiruan.png", "微软双拼.png", "微软双拼键位图.png" };
                case XuanKongSiScheme.Xiaohe:
                    return new[] { "xiaohe.png", "Xiaohe.png", "小鹤.png", "小鹤双拼.png", "小鹤双拼键位图.png" };
                case XuanKongSiScheme.Ziguang:
                    return new[] { "ziguang.png", "Ziguang.png", "紫光.png", "紫光双拼.png", "紫光双拼键位图.png", "智能ABC.png", "智能ABC键位图.png" };
                case XuanKongSiScheme.Ziranma:
                    return new[] { "ziranma.png", "Ziranma.png", "自然码.png", "自然码双拼.png", "自然码键位图.png" };
                default:
                    return new[] { "xiaohe.png" };
            }
        }

        private string SelectBestSchemeImageFile(string dirPath, XuanKongSiScheme scheme)
        {
            try
            {
                var files = Directory.GetFiles(dirPath, "*.png", SearchOption.TopDirectoryOnly);
                if (files == null || files.Length == 0) return null;

                // If there's only one image, use it.
                if (files.Length == 1) return files[0];

                var keywords = GetSchemeKeywords(scheme);
                var bestScore = int.MinValue;
                string best = null;
                foreach (var f in files)
                {
                    var name = Path.GetFileNameWithoutExtension(f) ?? "";
                    var score = 0;

                    // Prefer canonical names.
                    foreach (var candidate in GetSchemeImageFileNameCandidates(scheme))
                    {
                        if (string.Equals(Path.GetFileName(candidate), Path.GetFileName(f), StringComparison.OrdinalIgnoreCase))
                        {
                            score += 100;
                            break;
                        }
                    }

                    // Keyword matching (Chinese/English). More hits => higher score.
                    foreach (var kw in keywords)
                    {
                        if (string.IsNullOrEmpty(kw)) continue;
                        if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
                    }

                    // Tie-breaker: shorter file name looks more intentional.
                    score -= Math.Min(name.Length, 50);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = f;
                    }
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        private string[] GetSchemeKeywords(XuanKongSiScheme scheme)
        {
            switch (scheme)
            {
                case XuanKongSiScheme.Microsoft:
                    return new[] { "microsoft", "微软" };
                case XuanKongSiScheme.Xiaohe:
                    return new[] { "xiaohe", "小鹤" };
                case XuanKongSiScheme.Ziguang:
                    return new[] { "ziguang", "紫光", "智能abc", "abc" };
                case XuanKongSiScheme.Ziranma:
                    return new[] { "ziranma", "自然码" };
                default:
                    return Array.Empty<string>();
            }
        }

        private ImageSource LoadBitmapFromFile(string filePath)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private ImageSource LoadBitmapFromStream(Stream stream)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public void HideOverlay()
        {
            HideOverlayAnimated();
        }

        private SlideEdge GetSlideEdge(XuanKongSiTriggerKey triggerKey)
        {
            switch (triggerKey)
            {
                case XuanKongSiTriggerKey.RightCtrl:
                case XuanKongSiTriggerKey.RightShift:
                case XuanKongSiTriggerKey.RightAlt:
                case XuanKongSiTriggerKey.RightWin:
                    return SlideEdge.Right;
                default:
                    return SlideEdge.Left;
            }
        }

        private void PrepareCardPosition(SlideEdge edge)
        {
            // Align card to edge; slide distance is handled by animations.
            if (edge == SlideEdge.Right)
            {
                CardHost.HorizontalAlignment = HorizontalAlignment.Right;
                CardHost.Margin = new Thickness(0);
            }
            else
            {
                CardHost.HorizontalAlignment = HorizontalAlignment.Left;
                CardHost.Margin = new Thickness(0);
            }

            // Pure translation animation: start off-screen so it slides in cleanly.
            // Use a large offset here; exact value will be computed once ActualWidth is known.
            CardHost.Opacity = 0.97;
            try { OperationHint.Opacity = 0.95; } catch { }
            CardTransform.X = (edge == SlideEdge.Right) ? 2400 : -2400;
        }

        private void BeginShowAnimation(SlideEdge edge)
        {
            try
            {
                var distance = GetSlideDistance();
                var fromX = (edge == SlideEdge.Right) ? distance : -distance;
                try { App.Log($"XuanKongSiOverlay BeginShowAnimation: edge={edge}, distance={distance}, fromX={fromX}"); } catch { }

                // Stop any previous animation and force off-screen start.
                CardTransform.BeginAnimation(TranslateTransform.XProperty, null);
                CardTransform.X = fromX;
                var slide = new DoubleAnimation
                {
                    From = fromX,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(500),
                    EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                };

                CardTransform.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
            }
            catch
            {
                CardTransform.X = 0;
                CardHost.Opacity = 0.97;
                try { OperationHint.Opacity = 0.95; } catch { }
            }
        }

        private void BeginHideAnimation(SlideEdge edge, Action completed)
        {
            var distance = GetSlideDistance();
            var toX = (edge == SlideEdge.Right) ? distance : -distance;

            var slide = new DoubleAnimation
            {
                From = CardTransform.X,
                To = toX,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseIn }
            };

            slide.Completed += (s, e) =>
            {
                try { completed(); } catch { }
            };

            CardTransform.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
        }

        private double GetSlideDistance()
        {
            try
            {
                var w = ActualWidth;
                if (w <= 0) w = Width;
                if (w <= 0) w = SystemParameters.PrimaryScreenWidth;

                var distance = w + 200;
                if (distance < 1200) distance = 1200;
                return distance;
            }
            catch
            {
                return 1600;
            }
        }

        private string FormatTriggerKey(XuanKongSiTriggerKey triggerKey)
        {
            switch (triggerKey)
            {
                case XuanKongSiTriggerKey.LeftCtrl: return "左Ctrl";
                case XuanKongSiTriggerKey.RightCtrl: return "右Ctrl";
                case XuanKongSiTriggerKey.LeftShift: return "左Shift";
                case XuanKongSiTriggerKey.RightShift: return "右Shift";
                case XuanKongSiTriggerKey.LeftAlt: return "左Alt";
                case XuanKongSiTriggerKey.RightAlt: return "右Alt";
                case XuanKongSiTriggerKey.LeftWin: return "左Win";
                case XuanKongSiTriggerKey.RightWin: return "右Win";
                case XuanKongSiTriggerKey.Alt: return "左右Alt";
                case XuanKongSiTriggerKey.Shift: return "左右Shift";
                case XuanKongSiTriggerKey.Ctrl: return "左右Ctrl";
                default: return "左右Alt";
            }
        }

    }

    internal enum SlideEdge
    {
        Left,
        Right
    }

}
