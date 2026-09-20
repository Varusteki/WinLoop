using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using System.IO;
using System.Xml;
using WinLoop.Config;
using WinLoop.Menus;
using WinLoop.Models;
// using System.Windows.Forms; (fully-qualify WinForms types where needed)

namespace WinLoop.UI
{
    public partial class SettingsWindow : Window
    {
        private bool _suppressOnStartup = false;
        private readonly ConfigManager _configManager;
        private AppConfig _config;
        private RadialMenuFactory _menuFactory;
        private RadialMenu _previewMenu;
        
        // 循环高亮动画定时器
        private DispatcherTimer _highlightCycleTimer;
        private int _currentHighlightIndex = 0;
        private readonly string[] _positionNames = { "上", "右上", "右", "右下", "下", "左下", "左", "左上" };
        
        // 当前选中的扇区索引 (0-7)
        private int _selectedSectorIndex = 0;

        public SettingsWindow()
        {
            try
            {
                if (Application.Current != null && Application.Current.Properties.Contains("SuppressSettingsOnStartup") && (bool)Application.Current.Properties["SuppressSettingsOnStartup"] == true)
                {
                    _suppressOnStartup = true;
                    App.Log("SettingsWindow constructed during startup; will be suppressed.");
                }
            }
            catch { }

            InitializeComponent();
            _configManager = new ConfigManager();
            _menuFactory = new RadialMenuFactory();
            InitializeXuanKongSiControls();
            
            // 初始化操作配置下拉框
            InitializeActionCombos();

            // 绑定 ComboBox 事件（选择变化与悬停）
            BindPositionComboEvents();

            // 绑定整数文本框输入限制与事件
            BindIntegerTextBox(BasicOuterRadiusBox);
            BindIntegerTextBox(BasicInnerRadiusBox);
            BindIntegerTextBox(TriggerDelayBox);
            BindIntegerTextBox(SpiderWebRadiusBox);
            BindIntegerTextBox(SpiderWebRingsBox);
            BindIntegerTextBox(BaguaRadiusBox);
            BindIntegerTextBox(BaguaSectorTransparencyBox);
            BindIntegerTextBox(BaguaHighlightTransparencyBox);

            // 尺寸类输入框登记实时反馈：
            // Tooltip 显示「逻辑像素 → 本屏实际像素」换算，超出建议上限时描边变橙色。
            // 上限的取值依据 —— 预览画布逻辑尺寸 260，半径超过它的一半就无法完整显示：
            //   圆环/蜘蛛网/八卦 半径 → 130；内半径额外受"必须小于外半径"约束，给 120。
            RegisterSizeField(BasicOuterRadiusBox, "圆环外半径", 130);
            RegisterSizeField(BasicInnerRadiusBox, "圆环内半径", 120);
            RegisterSizeField(SpiderWebRadiusBox, "蜘蛛网外圈半径", 130);
            RegisterSizeField(BaguaRadiusBox, "八卦半径", 130);
            RegisterSizeField(TriggerDelayBox, "触发时长（毫秒）", 2000);
            RegisterSizeField(SpiderWebRingsBox, "蜘蛛网圈数", 8);
            RegisterSizeField(BaguaSectorTransparencyBox, "选区透明度（%）", 100);
            RegisterSizeField(BaguaHighlightTransparencyBox, "高亮透明度（%）", 100);

            // 绑定调色盘按钮
            try
            {
                BasicRingColorPickButton.Click += BasicRingColorPickButton_Click;
                BasicHighlightColorPickButton.Click += BasicHighlightColorPickButton_Click;
                // 绑定颜色文本框失去焦点事件（仅圆环）
                BasicRingColorBox.LostFocus += ColorTextBox_LostFocus;
                BasicHighlightColorBox.LostFocus += ColorTextBox_LostFocus;

                // 其余三种样式的调色盘按钮 → 复用同一套颜色选择对话框
                // （八角星只保留「大小」，不再暴露颜色）
                SpiderWebLineColorPickButton.Click += (s, e) => PickColorInto(SpiderWebLineColorBox, c => _config.SpiderWebMenuConfig.LineColor = c);
                SpiderWebHighlightColorPickButton.Click += (s, e) => PickColorInto(SpiderWebHighlightColorBox, c => _config.SpiderWebMenuConfig.HighlightColor = c);
                BaguaLineColorPickButton.Click += (s, e) => PickColorInto(BaguaLineColorBox, c => _config.BaguaMenuConfig.LineColor = c);
            }
            catch { }

            // 八角星「大小」滑块：拖动时同步百分比文案、写回配置并刷新预览
            try
            {
                CSHeadshotScaleSlider.ValueChanged += (s, e) =>
                {
                    ApplyCSHeadshotScale();
                    UpdatePreview();
                };
            }
            catch { }

            // 加载配置
            LoadConfig();
            
            // 添加滑块值变化事件处理
            AddSliderEventHandlers();
            
            // 添加按钮点击事件（使用新的安全处理器，避免旧版本残留）
            SaveButton.Click += OnSaveButton_Click_New;
            RestoreDefaultsButton.Click += RestoreDefaultsButton_Click;

            // 「取消」：不落盘直接关窗。
            // 本窗口原本就不在 Closing 里自动保存，只有点「确定」才写盘，
            // 所以取消语义天然成立 —— 不需要额外的回滚逻辑。
            if (CancelButton != null)
            {
                CancelButton.Click += (s, e) =>
                {
                    try { App.Log("Settings cancelled by user; nothing persisted."); }
                    catch { }
                    this.Close();
                };
            }

            try
            {
                XuanKongSiContentCombo.SelectionChanged += (s, e) => UpdateXuanKongSiPanels();
                XuanKongSiKeyCombo.SelectionChanged += (s, e) => UpdateXuanKongSiOperationHint();
                XuanKongSiEnabledCheck.Checked += (s, e) => UpdateXuanKongSiOperationHint();
                XuanKongSiEnabledCheck.Unchecked += (s, e) => UpdateXuanKongSiOperationHint();
                XuanKongSiPickImageButton.Click += XuanKongSiPickImageButton_Click;
            }
            catch { }
            
            // 添加菜单样式选择事件
            BasicRadialRadio.Checked += MenuStyle_Checked;
            CSHeadshotRadio.Checked += MenuStyle_Checked;
            SpiderWebRadio.Checked += MenuStyle_Checked;
            BaguaRadio.Checked += MenuStyle_Checked;

            // 左侧导航 ↔ 内容页联动。
            // 标签栏已隐藏（见 XAML 里 TabControl 的自定义 Template），
            // 由左导航的选中状态驱动 SelectedIndex；两个方向都要绑，
            // 否则代码里改 SelectedIndex 时左导航会跟丢。
            BindNavigation();

            // 画布大小改变时更新预览
            PreviewCanvas.SizeChanged += (s, e) => UpdatePreview();
            OpPreviewCanvas.SizeChanged += (s, e) => UpdateOpPreview();
            
            // 初始化循环高亮动画定时器
            _highlightCycleTimer = new DispatcherTimer();
            _highlightCycleTimer.Interval = TimeSpan.FromMilliseconds(800);
            _highlightCycleTimer.Tick += HighlightCycleTimer_Tick;
            
            // 窗口打开后启动循环高亮
            this.ContentRendered += (s, e) =>
            {
                if (!_suppressOnStartup)
                {
                    _highlightCycleTimer.Start();
                }
            };
            
            // 窗口关闭时停止定时器
            this.Closing += (s, e) =>
            {
                _highlightCycleTimer?.Stop();
            };

            // 如果在启动阶段构造的 SettingsWindow，则在加载后立即关闭以避免显示
            this.Loaded += (s, e) =>
            {
                try
                {
                    if (_suppressOnStartup)
                    {
                        App.Log("Suppressing SettingsWindow display during startup (auto-close).");
                        this.Close();
                    }
                }
                catch { }
            };
        }

        private void InitializeActionCombos()
        {
            try
            {
                // 获取所有窗口操作枚举值
                // 使用中文显示映射，同时保持 SelectedValue 为 WindowAction
                var items = new[]
                {
                    new { Key = WindowAction.BackToDesktop, Label = "回到桌面" },
                    new { Key = WindowAction.Minimize, Label = "最小化窗口" },
                    new { Key = WindowAction.Maximize, Label = "最大化窗口" },
                    new { Key = WindowAction.LeftHalf, Label = "左半屏" },
                    new { Key = WindowAction.RightHalf, Label = "右半屏" },
                    new { Key = WindowAction.TopHalf, Label = "上半屏" },
                    new { Key = WindowAction.BottomHalf, Label = "下半屏" },
                    new { Key = WindowAction.TopLeftQuadrant, Label = "左上分屏" },
                    new { Key = WindowAction.BottomLeftQuadrant, Label = "左下分屏" },
                    new { Key = WindowAction.TopRightQuadrant, Label = "右上分屏" },
                    new { Key = WindowAction.BottomRightQuadrant, Label = "右下分屏" },
                    new { Key = WindowAction.LeftTwoThirds, Label = "左三分之二屏" },
                    new { Key = WindowAction.RightTwoThirds, Label = "右三分之二屏" },
                    new { Key = WindowAction.ToggleMaximize, Label = "最大化/还原切换" },
                    new { Key = WindowAction.ToggleTopMost, Label = "窗口置顶切换" },
                    new { Key = WindowAction.CloseWindow, Label = "关闭窗口" }
                };

                // 只初始化当前位置的下拉框
                CurrentPositionCombo.ItemsSource = items;
                CurrentPositionCombo.DisplayMemberPath = "Label";
                CurrentPositionCombo.SelectedValuePath = "Key";
            }
            catch (Exception ex)
            {
                App.Log("InitializeActionCombos error: " + ex.Message);
            }
        }

        private void BindPositionComboEvents()
        {
            // 不再需要绑定8个ComboBox，使用单一的CurrentPositionCombo
        }

        private void InitializeXuanKongSiControls()
        {
            try
            {
                XuanKongSiKeyCombo.ItemsSource = new[]
                {
                    new { Key = XuanKongSiTriggerKey.Alt, Label = "左右Alt" },
                    new { Key = XuanKongSiTriggerKey.Shift, Label = "左右Shift" },
                    new { Key = XuanKongSiTriggerKey.Ctrl, Label = "左右Ctrl" }
                };
                XuanKongSiKeyCombo.DisplayMemberPath = "Label";
                XuanKongSiKeyCombo.SelectedValuePath = "Key";

                XuanKongSiContentCombo.ItemsSource = new[]
                {
                    new { Key = XuanKongSiContentType.Image, Label = "图片" },
                    new { Key = XuanKongSiContentType.Text, Label = "文字" }
                };
                XuanKongSiContentCombo.DisplayMemberPath = "Label";
                XuanKongSiContentCombo.SelectedValuePath = "Key";
            }
            catch (Exception ex)
            {
                App.Log("InitializeXuanKongSiControls error: " + ex.Message);
            }
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

        private void UpdateXuanKongSiPanels()
        {
            try
            {
                var type = XuanKongSiContentType.Image;
                if (XuanKongSiContentCombo.SelectedValue is XuanKongSiContentType t)
                {
                    type = t;
                }

                XuanKongSiTextPanel.Visibility = type == XuanKongSiContentType.Text ? Visibility.Visible : Visibility.Collapsed;
                XuanKongSiImagePanel.Visibility = type == XuanKongSiContentType.Image ? Visibility.Visible : Visibility.Collapsed;

                // 切回图片模式时预览可能还没装（首次打开窗口走这条路径），补一次
                if (type == XuanKongSiContentType.Image) UpdateXuanKongSiImagePreview();
            }
            catch { }
        }

        private void UpdateXuanKongSiOperationHint()
        {
            try
            {
                if (XuanKongSiOperationHintText == null) return;

                var enabled = XuanKongSiEnabledCheck != null && XuanKongSiEnabledCheck.IsChecked == true;
                var key = XuanKongSiTriggerKey.Alt;
                if (XuanKongSiKeyCombo != null && XuanKongSiKeyCombo.SelectedValue is XuanKongSiTriggerKey k)
                {
                    key = k;
                }

                var keyLabel = FormatXuanKongSiTriggerKey(key);
                var text = $"双击 {keyLabel} 显示，按 ESC 隐藏（点击覆盖层不收起）";
                if (!enabled)
                {
                    text = $"已关闭。启用后：{text}";
                }

                XuanKongSiOperationHintText.Text = text;
            }
            catch { }
        }

        private static string FormatXuanKongSiTriggerKey(XuanKongSiTriggerKey key)
        {
            switch (key)
            {
                case XuanKongSiTriggerKey.Alt: return "左右Alt";
                case XuanKongSiTriggerKey.Shift: return "左右Shift";
                case XuanKongSiTriggerKey.Ctrl: return "左右Ctrl";
                case XuanKongSiTriggerKey.LeftAlt: return "左Alt";
                case XuanKongSiTriggerKey.RightAlt: return "右Alt";
                case XuanKongSiTriggerKey.LeftShift: return "左Shift";
                case XuanKongSiTriggerKey.RightShift: return "右Shift";
                case XuanKongSiTriggerKey.LeftCtrl: return "左Ctrl";
                case XuanKongSiTriggerKey.RightCtrl: return "右Ctrl";
                case XuanKongSiTriggerKey.LeftWin: return "左Win";
                case XuanKongSiTriggerKey.RightWin: return "右Win";
                default: return "左右Alt";
            }
        }

        private static bool LooksLikeFlowDocumentXaml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.TrimStart();
            return t.StartsWith("<FlowDocument", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryConvertFlowDocumentXamlToPlainText(string xaml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(xaml)) return string.Empty;
                if (!LooksLikeFlowDocumentXaml(xaml)) return xaml;

                var parsed = XamlReader.Parse(xaml) as FlowDocument;
                if (parsed == null) return string.Empty;

                var range = new TextRange(parsed.ContentStart, parsed.ContentEnd);
                return (range.Text ?? string.Empty).TrimEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        private void XuanKongSiPickImageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择要展示的图片",
                    Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*"
                };

                var r = dlg.ShowDialog();
                if (r != true) return;

                var src = dlg.FileName;
                if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) return;

                var mediaDir = GetMediaDir();
                if (string.IsNullOrEmpty(mediaDir)) return;
                Directory.CreateDirectory(mediaDir);

                var baseName = Path.GetFileName(src);
                if (string.IsNullOrEmpty(baseName)) baseName = "xuankongsi.png";
                var dest = Path.Combine(mediaDir, baseName);

                // Avoid overwriting existing file with different content.
                if (File.Exists(dest))
                {
                    var nameNoExt = Path.GetFileNameWithoutExtension(baseName);
                    var ext = Path.GetExtension(baseName);
                    for (int i = 1; i <= 50; i++)
                    {
                        var candidate = Path.Combine(mediaDir, $"{nameNoExt}_{i}{ext}");
                        if (!File.Exists(candidate)) { dest = candidate; break; }
                    }
                }

                File.Copy(src, dest, false);

                // 更新配置：预览由 UpdateXuanKongSiImagePreview 统一刷新
                //（在 UpdateXuanKongSiPanels 里被调用），这里不直接碰控件。
                if (_config?.XuanKongSi == null) _config.XuanKongSi = new XuanKongSiConfig();
                _config.XuanKongSi.ImageFileName = Path.GetFileName(dest);

                XuanKongSiContentCombo.SelectedValue = XuanKongSiContentType.Image;
                UpdateXuanKongSiPanels();
            }
            catch (Exception ex)
            {
                App.Log("XuanKongSiPickImageButton_Click error: " + ex.Message);
            }
        }

        /// <summary>
        /// 刷新图片预览。
        ///
        /// 预览宽度由外层 Grid 决定（跟随卡片可用宽度），这里只负责装图片源；
        /// 高度不用管 —— Image 用 Stretch=Uniform，会按原始宽高比自动算出来。
        ///
        /// **无论用户选了图还是走默认图都会展示** —— 用户需要直接看到
        /// "现在实际会显示什么"，而不是只看一个文件名。
        /// 找不到任何图时才隐藏（不留空占位）。
        /// </summary>
        private void UpdateXuanKongSiImagePreview()
        {
            try
            {
                string path = ResolvePreviewImagePath();
                if (string.IsNullOrEmpty(path))
                {
                    HideImagePreview();
                    return;
                }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                // OnLoad：立刻把文件读进内存并释放文件句柄，
                // 否则用户换图时旧文件被锁住、File.Copy 会失败。
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                XuanKongSiImagePreview.Source = bmp;
                XuanKongSiImagePreviewBorder.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Log("UpdateXuanKongSiImagePreview error: " + ex.Message);
                HideImagePreview();
            }
        }

        /// <summary>
        /// 决定预览该显示哪张图，优先级与悬空寺覆盖层的实际渲染一致：
        ///   ① 用户选定的图片（配置目录 Media/&lt;ImageFileName&gt;）
        ///   ② 随包分发的默认图 Resources/XuanKongSi/小鹤双拼-键位图.png
        ///   ③ 该目录下任意一张图（兜底）
        /// 顺序必须和 XuanKongSiOverlayWindow.RenderImage 保持一致，
        /// 否则预览会与实际显示不符 —— 预览的意义就在于"所见即所得"。
        /// </summary>
        private string ResolvePreviewImagePath()
        {
            // ① 用户选定的图
            var name = _config?.XuanKongSi?.ImageFileName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                var mediaDir = GetMediaDir();
                if (!string.IsNullOrEmpty(mediaDir))
                {
                    var p = Path.Combine(mediaDir, name);
                    if (File.Exists(p)) return p;
                }
            }

            // ② 默认图
            try
            {
                var defaultDir = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources", "XuanKongSi");

                var xiaohe = Path.Combine(defaultDir, "小鹤双拼-键位图.png");
                if (File.Exists(xiaohe)) return xiaohe;

                // ③ 兜底：目录里第一张 png
                if (Directory.Exists(defaultDir))
                {
                    var any = Directory.GetFiles(defaultDir, "*.png");
                    if (any != null && any.Length > 0) return any[0];
                }
            }
            catch { }

            return null;
        }

        private void HideImagePreview()
        {
            try
            {
                XuanKongSiImagePreview.Source = null;
                XuanKongSiImagePreviewBorder.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        /// <summary>
        /// 尺寸类输入框的元信息：用于实时 Tooltip 与越界软提醒。
        /// MaxRecommended 是"预览画布能完整容纳"的经验上限，超过只提醒不限制 ——
        /// 高级用户可能确实想要 400px 半径，我们不能替他做决定。
        /// </summary>
        private class SizeFieldMeta
        {
            public string Label;          // 用于提示文案
            public int MaxRecommended;    // 建议上限（逻辑像素）
            public double DpiScale = 1.0; // 本屏 DPI 缩放，用于换算"实际像素"
        }

        private readonly Dictionary<TextBox, SizeFieldMeta> _sizeFields = new Dictionary<TextBox, SizeFieldMeta>();

        /// <summary>
        /// 把尺寸输入框登记进实时反馈表，并挂上 TextChanged。
        /// 逻辑像素 → 实际像素的换算是 ×本屏 DPI 缩放，
        /// 这样用户在 100% 屏上看到的值就是屏幕上真实占据的像素数。
        /// </summary>
        private void RegisterSizeField(TextBox box, string label, int maxRecommended)
        {
            if (box == null) return;
            try
            {
                var meta = new SizeFieldMeta { Label = label, MaxRecommended = maxRecommended };
                _sizeFields[box] = meta;
                box.TextChanged += (s, e) => RefreshSizeFieldFeedback(box);
                RefreshSizeFieldFeedback(box);
            }
            catch (Exception ex)
            {
                App.Log("RegisterSizeField error: " + ex.Message);
            }
        }

        /// <summary>
        /// 刷新单个尺寸输入框的 Tooltip 与越界状态。
        /// 越界只改描边颜色，不弹窗、不阻止输入 —— 属于"软提醒"。
        /// </summary>
        private void RefreshSizeFieldFeedback(TextBox box)
        {
            try
            {
                if (box == null) return;
                SizeFieldMeta meta;
                if (!_sizeFields.TryGetValue(box, out meta) || meta == null) return;

                int v;
                bool parsed = int.TryParse(box.Text, out v);

                // 本屏缩放：预览与运行时都按它换算成实际像素
                double scale = 1.0;
                try
                {
                    scale = SizingScale.FromVisual(this);
                    if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) scale = 1.0;
                }
                catch { }
                meta.DpiScale = scale;

                if (!parsed)
                {
                    box.ToolTip = meta.Label + "：请输入数字";
                    box.ClearValue(TextBox.BorderBrushProperty);
                    return;
                }

                int actual = (int)Math.Round(v * scale);
                string tip = string.Format(
                    "{0}\n逻辑像素：{1}\n本屏缩放：{2}%\n实际显示：约 {3}px",
                    meta.Label, v, (int)Math.Round(scale * 100), actual);

                if (v > meta.MaxRecommended)
                {
                    tip += string.Format(
                        "\n\n⚠ 超过建议上限 {0}（逻辑像素）。\n预览会自动缩小以完整显示，实际菜单会比你预期的大。",
                        meta.MaxRecommended);
                    // 软提醒：只把描边染成警示色，输入照常接受
                    box.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFD08A3A"));
                }
                else
                {
                    box.ClearValue(TextBox.BorderBrushProperty);
                }

                box.ToolTip = tip;
            }
            catch (Exception ex)
            {
                App.Log("RefreshSizeFieldFeedback error: " + ex.Message);
            }
        }

        private void BindIntegerTextBox(TextBox box)
        {
            if (box == null) return;
            box.PreviewTextInput += IntegerTextBox_PreviewTextInput;
            DataObject.AddPastingHandler(box, IntegerTextBox_Pasting);
            box.LostFocus += IntegerTextBox_LostFocus;
        }
        
        private void HighlightCycleTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // 循环高亮各扇区以展示高亮颜色效果
                var pos = (MenuItemPosition)_currentHighlightIndex;
                if (_previewMenu != null)
                {
                    _previewMenu.HighlightItem(pos);
                }
                
                // 循环到下一个位置
                _currentHighlightIndex = (_currentHighlightIndex + 1) % 8;
            }
            catch (Exception ex)
            {
                App.Log("HighlightCycleTimer_Tick error: " + ex.Message);
            }
        }

        private void IntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void IntegerTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string));
                if (!Regex.IsMatch(text, "^[0-9]+$"))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private void IntegerTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                var tb = sender as TextBox;
                if (tb == null) return;
                if (string.IsNullOrWhiteSpace(tb.Text)) tb.Text = "0";

                // 更新配置并刷新预览
                if (_config != null)
                {
                    if (tb == BasicOuterRadiusBox && int.TryParse(tb.Text, out var v1)) _config.BasicRadialMenuConfig.OuterRadius = v1;
                    if (tb == BasicInnerRadiusBox && int.TryParse(tb.Text, out var v2)) _config.BasicRadialMenuConfig.InnerRadius = v2;
                    if (tb == TriggerDelayBox && int.TryParse(tb.Text, out var v7)) _config.TriggerDelay = v7;
                }

                UpdatePreview();
                UpdateOpPreview();
            }
            catch (Exception ex)
            {
                App.Log("IntegerTextBox_LostFocus error: " + ex.Message);
            }
        }

        private void ColorTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                var tb = sender as TextBox;
                if (tb == null || _config == null) return;
                
                string colorValue = tb.Text.Trim();
                if (string.IsNullOrEmpty(colorValue)) return;

                // 校验必须用**渲染层同一套**解析器（WPF ColorConverter）。
                // 原先用 System.Drawing.ColorTranslator.FromHtml 校验，它的接受范围更宽
                // （例如 rgb(255,0,0) 也认），会放过渲染层解析不了的值，
                // 一路存进配置后在绘制时抛异常。两者统一后这个口子就堵上了。
                string normalized;
                try
                {
                    var obj = System.Windows.Media.ColorConverter.ConvertFromString(
                        colorValue.StartsWith("#") ? colorValue : "#" + colorValue);
                    if (!(obj is System.Windows.Media.Color)) return;
                    normalized = ColorPickerWindow.ToHex((System.Windows.Media.Color)obj);
                }
                catch
                {
                    return; // 无效颜色格式，不更新
                }

                // 回写规范化后的值，让界面显示与配置一致（统一 6 位大写）
                tb.Text = normalized;

                // 更新对应的配置
                if (tb == BasicRingColorBox) _config.BasicRadialMenuConfig.RingColor = normalized;
                else if (tb == BasicHighlightColorBox) _config.BasicRadialMenuConfig.HighlightColor = normalized;
                
                UpdatePreview();
                UpdateOpPreview();
            }
            catch (Exception ex)
            {
                App.Log("ColorTextBox_LostFocus error: " + ex.Message);
            }
        }

        private void LoadConfig()
        {
            try
            {
                _config = _configManager.LoadConfig();
                EnsureThicknessFallback();
                PopulateUIFromConfig();
            }
            catch (Exception ex)
            {
                App.Log("LoadConfig error: " + ex.Message);
            }
        }

        private void EnsureThicknessFallback()
        {
            try
            {
                if (_config != null && _config.BasicRadialMenuConfig.Thickness <= 0)
                {
                    _config.BasicRadialMenuConfig.Thickness = _config.BasicRadialMenuConfig.OuterRadius - _config.BasicRadialMenuConfig.InnerRadius;
                }
            }
            catch { }
        }

        /// <summary>
        /// 左侧导航与内容页的双向联动。
        ///
        /// 标签栏在 XAML 里被隐藏了（TabControl 用了自定义 Template，
        /// 第一行高度设 0，只保留 SelectedContent），导航改由左侧那四个
        /// RadioButton 承担。这里做两件事：
        ///   1. 点导航 → 切 SelectedIndex；
        ///   2. SelectedIndex 变了 → 同步导航的选中态。
        ///
        /// 第 2 条不能省：将来若有代码直接改 MainTabControl.SelectedIndex
        /// （例如"保存后跳到某页"），左导航不跟就会和内容对不上。
        /// 用 _suppressNavSync 防止两个方向互相触发形成回环。
        /// </summary>
        private bool _suppressNavSync;

        private void BindNavigation()
        {
            try
            {
                var navs = new[] { NavMenuStyle, NavOperation, NavXuanKongSi, NavMisc };
                if (MainTabControl == null || MainTabControl.Items.Count < navs.Length) return;

                for (int i = 0; i < navs.Length; i++)
                {
                    var nav = navs[i];
                    if (nav == null) continue;

                    int index = i;   // 闭包捕获：不能直接用 i
                    nav.Checked += (s, e) =>
                    {
                        if (_suppressNavSync) return;
                        _suppressNavSync = true;
                        try { MainTabControl.SelectedIndex = index; }
                        finally { _suppressNavSync = false; }
                    };
                }

                MainTabControl.SelectionChanged += (s, e) =>
                {
                    if (_suppressNavSync) return;
                    int idx = MainTabControl.SelectedIndex;
                    if (idx < 0 || idx >= navs.Length) return;

                    _suppressNavSync = true;
                    try { navs[idx].IsChecked = true; }
                    finally { _suppressNavSync = false; }
                };

                // 初始态与当前选中页对齐
                int cur = MainTabControl.SelectedIndex;
                if (cur >= 0 && cur < navs.Length) navs[cur].IsChecked = true;
            }
            catch (Exception ex)
            {
                App.Log("BindNavigation error: " + ex.Message);
            }
        }

        private void PopulateUIFromConfig()
        {
            try
            {
                if (_config == null) return;

                // 恢复配置中的菜单样式选择
                // （面板可见性统一在方法末尾的 UpdateConfigPanels() 里刷新 ——
                //   不要依赖这里赋值触发的 Checked 事件：RadioButton 赋同值不触发事件，
                //   而且本方法在构造函数里、**早于**事件挂接就被调用了。）
                switch (_config.MenuStyle)
                {
                    case MenuStyle.CSHeadshotOctagon:
                        CSHeadshotRadio.IsChecked = true;
                        break;
                    case MenuStyle.SpiderWeb:
                        SpiderWebRadio.IsChecked = true;
                        break;
                    case MenuStyle.Bagua:
                        BaguaRadio.IsChecked = true;
                        break;
                    default:
                        _config.MenuStyle = MenuStyle.BasicRadial;
                        BasicRadialRadio.IsChecked = true;
                        break;
                }

                BasicOuterRadiusBox.Text = ((int)_config.BasicRadialMenuConfig.OuterRadius).ToString();
                BasicInnerRadiusBox.Text = ((int)_config.BasicRadialMenuConfig.InnerRadius).ToString();
                BasicRingColorBox.Text = _config.BasicRadialMenuConfig.RingColor;
                BasicHighlightColorBox.Text = _config.BasicRadialMenuConfig.HighlightColor;

                // 八角星：只回填「大小」（百分比由半径换算）
                double csPercent = _config.CSHeadshotMenuConfig.Radius
                                   / CSHeadshotMenuConfig.BaseRadius * 100.0;
                if (double.IsNaN(csPercent) || csPercent <= 0) csPercent = 100;
                CSHeadshotScaleSlider.Value = Math.Max(CSHeadshotScaleSlider.Minimum,
                                             Math.Min(CSHeadshotScaleSlider.Maximum, csPercent));
                ApplyCSHeadshotScale();

                SpiderWebRadiusBox.Text = ((int)_config.SpiderWebMenuConfig.OuterRadius).ToString();
                SpiderWebRingsBox.Text = _config.SpiderWebMenuConfig.Rings.ToString();
                SpiderWebLineColorBox.Text = _config.SpiderWebMenuConfig.LineColor;
                SpiderWebHighlightColorBox.Text = _config.SpiderWebMenuConfig.HighlightColor;

                BaguaRadiusBox.Text = ((int)_config.BaguaMenuConfig.OuterRadius).ToString();
                BaguaLineColorBox.Text = _config.BaguaMenuConfig.LineColor;
                BaguaSectorTransparencyBox.Text = _config.BaguaMenuConfig.SectorTransparency.ToString();
                BaguaHighlightTransparencyBox.Text = _config.BaguaMenuConfig.HighlightTransparency.ToString();

                TriggerDelayBox.Text = _config.TriggerDelay.ToString();

                if (_config.XuanKongSi == null)
                {
                    _config.XuanKongSi = new XuanKongSiConfig();
                }
                var sp = _config.XuanKongSi;
                XuanKongSiEnabledCheck.IsChecked = sp.Enabled;
                try
                {
                    var tk = sp.TriggerKey;
                    if (tk == XuanKongSiTriggerKey.LeftAlt || tk == XuanKongSiTriggerKey.RightAlt) tk = XuanKongSiTriggerKey.Alt;
                    else if (tk == XuanKongSiTriggerKey.LeftShift || tk == XuanKongSiTriggerKey.RightShift) tk = XuanKongSiTriggerKey.Shift;
                    else if (tk == XuanKongSiTriggerKey.LeftCtrl || tk == XuanKongSiTriggerKey.RightCtrl) tk = XuanKongSiTriggerKey.Ctrl;
                    else if (tk == XuanKongSiTriggerKey.LeftWin || tk == XuanKongSiTriggerKey.RightWin) tk = XuanKongSiTriggerKey.Alt;
                    XuanKongSiKeyCombo.SelectedValue = tk;
                }
                catch
                {
                    XuanKongSiKeyCombo.SelectedValue = XuanKongSiTriggerKey.Alt;
                }
                // Web mode removed: coerce to Text for display.
                var ct = sp.ContentType == XuanKongSiContentType.Web ? XuanKongSiContentType.Text : sp.ContentType;
                XuanKongSiContentCombo.SelectedValue = ct;

                try
                {
                    // Load Markdown (backward compatible with old FlowDocument XAML saved previously)
                    XuanKongSiTextEditor.Text = TryConvertFlowDocumentXamlToPlainText(sp.TextXaml);
                }
                catch { }

                // 图片文件名标签与预览图由 UpdateXuanKongSiImagePreview 统一刷新
                //（在 UpdateXuanKongSiPanels 里被调用）—— 单一状态来源，避免两处不一致。
                UpdateXuanKongSiPanels();
                UpdateXuanKongSiOperationHint();

                // 操作映射 - 默认选中第一个扇区
                _selectedSectorIndex = 0;
                UpdateCurrentSectorUI();

                AutoStartCheck.IsChecked = _config.AutoStart;
                MinimizeToTrayCheck.IsChecked = _config.MinimizeToTray;

                UpdateConfigPanels();
                UpdatePreview();
            }
            catch (Exception ex)
            {
                App.Log("PopulateUIFromConfig error: " + ex.Message);
            }
        }

        private void UpdateCurrentSectorUI()
        {
            try
            {
                if (_config == null) return;

                CurrentPositionLabel.Text = $"位置{_selectedSectorIndex + 1} ({_positionNames[_selectedSectorIndex]})";
                var pos = (MenuItemPosition)_selectedSectorIndex;

                if (_config.ActionMapping.TryGetValue(pos, out var action))
                {
                    CurrentPositionCombo.SelectedValue = action;
                }
                else
                {
                    CurrentPositionCombo.SelectedIndex = -1;
                }

                if (_opPreviewMenu != null)
                {
                    _opPreviewMenu.HighlightItem(pos);
                }
            }
            catch (Exception ex)
            {
                App.Log("UpdateCurrentSectorUI error: " + ex.Message);
            }
        }

        /// <summary>
        /// 把八角星「大小」滑块的百分比换算成尖刺顶点半径写回配置，并刷新百分比文案。
        /// 菜单是矢量绘制的，改这个值相当于整体缩放，不会影响清晰度。
        /// </summary>
        private void ApplyCSHeadshotScale()
        {
            if (CSHeadshotScaleSlider == null || _config == null) return;

            double percent = CSHeadshotScaleSlider.Value;
            _config.CSHeadshotMenuConfig.Radius = CSHeadshotMenuConfig.BaseRadius * percent / 100.0;

            if (CSHeadshotScaleLabel != null)
            {
                CSHeadshotScaleLabel.Text = ((int)Math.Round(percent)) + "%";
            }
        }

        private void SaveConfig()
        {
            try
            {
                // 保存菜单样式
                if (CSHeadshotRadio.IsChecked == true) _config.MenuStyle = MenuStyle.CSHeadshotOctagon;
                else if (SpiderWebRadio.IsChecked == true) _config.MenuStyle = MenuStyle.SpiderWeb;
                else if (BaguaRadio.IsChecked == true) _config.MenuStyle = MenuStyle.Bagua;
                else _config.MenuStyle = MenuStyle.BasicRadial;

                // 保存基础环形菜单配置（从文本框读取正整数）
                if (int.TryParse(BasicOuterRadiusBox.Text, out var bo)) _config.BasicRadialMenuConfig.OuterRadius = bo;
                if (int.TryParse(BasicInnerRadiusBox.Text, out var bi)) _config.BasicRadialMenuConfig.InnerRadius = bi;
                // update Thickness to reflect Outer - Inner (Loop exposes radialMenuThickness)
                try
                {
                    _config.BasicRadialMenuConfig.Thickness = _config.BasicRadialMenuConfig.OuterRadius - _config.BasicRadialMenuConfig.InnerRadius;
                }
                catch { }
                // 保存颜色设置（简单写入，期望为 #RRGGBB）
                _config.BasicRadialMenuConfig.RingColor = BasicRingColorBox.Text.Trim();
                _config.BasicRadialMenuConfig.HighlightColor = BasicHighlightColorBox.Text.Trim();

                // 保存八角星配置：只保存「大小」（颜色是固定美术风格，不开放调节）
                ApplyCSHeadshotScale();

                // 保存蜘蛛网配置
                if (int.TryParse(SpiderWebRadiusBox.Text, out var swr)) _config.SpiderWebMenuConfig.OuterRadius = swr;
                if (int.TryParse(SpiderWebRingsBox.Text, out var swg)) _config.SpiderWebMenuConfig.Rings = swg;
                _config.SpiderWebMenuConfig.LineColor = SpiderWebLineColorBox.Text.Trim();
                _config.SpiderWebMenuConfig.HighlightColor = SpiderWebHighlightColorBox.Text.Trim();

                // 保存八卦配置
                if (int.TryParse(BaguaRadiusBox.Text, out var bgr)) _config.BaguaMenuConfig.OuterRadius = bgr;
                _config.BaguaMenuConfig.LineColor = BaguaLineColorBox.Text.Trim();
                if (int.TryParse(BaguaSectorTransparencyBox.Text, out var bgt)) _config.BaguaMenuConfig.SectorTransparency = bgt;
                if (int.TryParse(BaguaHighlightTransparencyBox.Text, out var bgh)) _config.BaguaMenuConfig.HighlightTransparency = bgh;

                // (accent colors removed)

                // 保存触发时长
                if (int.TryParse(TriggerDelayBox.Text, out var td)) _config.TriggerDelay = td;
                
                // 操作配置已在选择时实时保存到_config.ActionMapping，无需额外处理
                
                // 保存杂项设置
                _config.AutoStart = AutoStartCheck.IsChecked == true;
                _config.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;

                if (_config.XuanKongSi == null)
                {
                    _config.XuanKongSi = new XuanKongSiConfig();
                }
                var sp = _config.XuanKongSi;
                sp.Enabled = XuanKongSiEnabledCheck.IsChecked == true;
                if (XuanKongSiKeyCombo.SelectedValue is XuanKongSiTriggerKey key) sp.TriggerKey = key;
                var contentType = XuanKongSiContentType.Image;
                if (XuanKongSiContentCombo.SelectedValue is XuanKongSiContentType ct2) contentType = ct2;
                // Web mode removed: never persist Web.
                if (contentType == XuanKongSiContentType.Web) contentType = XuanKongSiContentType.Text;
                sp.ContentType = contentType;

                // Markdown plain text (stored in TextXaml field for backward compatibility)
                try
                {
                    sp.TextXaml = (XuanKongSiTextEditor.Text ?? string.Empty);
                }
                catch { }
            }
            catch (Exception ex)
            {
                App.Log("SaveConfig error: " + ex.Message);
            }
        }

        private void OpPreviewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var clickPos = e.GetPosition(OpPreviewCanvas);
                double w = OpPreviewCanvas.ActualWidth > 0 ? OpPreviewCanvas.ActualWidth : 220;
                double h = OpPreviewCanvas.ActualHeight > 0 ? OpPreviewCanvas.ActualHeight : 220;

                double centerX = w / 2;
                double centerY = h / 2;

                double dx = clickPos.X - centerX;
                double dy = clickPos.Y - centerY;

                double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                angle = (angle + 90 + 360) % 360;
                angle = (angle + 22.5) % 360;

                int sectorIndex = (int)(angle / 45) % 8;
                _selectedSectorIndex = sectorIndex;
                UpdateCurrentSectorUI();
            }
            catch (Exception ex)
            {
                App.Log("OpPreviewCanvas_MouseLeftButtonDown error: " + ex.Message);
            }
        }

        private void CurrentPositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_config == null) return;
                var pos = (MenuItemPosition)_selectedSectorIndex;

                if (CurrentPositionCombo.SelectedValue is WindowAction action)
                {
                    _config.ActionMapping[pos] = action;
                    if (_opPreviewMenu != null)
                    {
                        _opPreviewMenu.HighlightItem(pos);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log("CurrentPositionCombo_SelectionChanged error: " + ex.Message);
            }
        }

        private void MenuStyle_Checked(object sender, RoutedEventArgs e)
        {
            if (_config == null) return;

            // 根据被选中的单选按钮更新样式
            if (CSHeadshotRadio.IsChecked == true) _config.MenuStyle = MenuStyle.CSHeadshotOctagon;
            else if (SpiderWebRadio.IsChecked == true) _config.MenuStyle = MenuStyle.SpiderWeb;
            else if (BaguaRadio.IsChecked == true) _config.MenuStyle = MenuStyle.Bagua;
            else _config.MenuStyle = MenuStyle.BasicRadial;
            App.Log($"Menu style selected: {_config.MenuStyle}");

            // 必须在校验 _config.MenuStyle 之后再刷新面板可见性，
            // 否则 UpdateConfigPanels() 读到的是切换前的旧样式。
            UpdateConfigPanels();

            UpdatePreview();
        }

        private void UpdateConfigPanels()
        {
            // 每种样式有自己独立的参数面板，必须按当前样式切换可见性。
            // 原先无条件显示 BasicRadial 面板，导致选中八角星/蜘蛛网/八卦时，
            // 界面仍展示圆环的外半径、内半径等无效参数 —— 看起来就像界面错乱。
            if (BasicRadialConfigGrid == null || _config == null) return;

            bool isBasic = _config.MenuStyle == MenuStyle.BasicRadial;

            BasicRadialConfigGrid.Visibility = isBasic ? Visibility.Visible : Visibility.Collapsed;
            CSHeadshotConfigGrid.Visibility =
                _config.MenuStyle == MenuStyle.CSHeadshotOctagon ? Visibility.Visible : Visibility.Collapsed;
            SpiderWebConfigGrid.Visibility =
                _config.MenuStyle == MenuStyle.SpiderWeb ? Visibility.Visible : Visibility.Collapsed;
            BaguaConfigGrid.Visibility =
                _config.MenuStyle == MenuStyle.Bagua ? Visibility.Visible : Visibility.Collapsed;

            // 圆环的配色单独放了一张卡片（尺寸与配色分组显示），
            // 可见性必须跟 BasicRadialConfigGrid 一致 —— 否则切到其它样式时
            // 尺寸面板收起了、配色面板还留在屏幕上，等于在展示无效参数。
            if (BasicColorCard != null)
            {
                BasicColorCard.Visibility = isBasic ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdatePreview()
        {
            try
            {
                if (_config == null || PreviewCanvas == null) return;

                PreviewCanvas.Children.Clear();

                // 获取画布尺寸
                double w = PreviewCanvas.ActualWidth;
                double h = PreviewCanvas.ActualHeight;
                if (w <= 0) w = 280;
                if (h <= 0) h = 280;
                var center = new Point(w / 2, h / 2);

                // 创建并初始化菜单，用于预览。
                // 预览固定按 100% 缩放绘制（SizingScale = 1.0）：预览的职责是表达
                // "我调大了一点"这种**相对**变化，跟着用户当前屏幕 DPI 走反而会让
                // 同一数值在不同机器上显示不同大小，看不出自己改了什么。
                _config.SizingScale = SizingScale.PreviewScale;
                _previewMenu = _menuFactory.CreateMenu(_config.MenuStyle, _config);
                _previewMenu.Initialize(_config, center);

                // 获取菜单尺寸并计算居中位置
                double menuWidth = _previewMenu.Width;
                double menuHeight = _previewMenu.Height;
                if (double.IsNaN(menuWidth) || menuWidth <= 0) menuWidth = _previewMenu.VisualRadius * 2;
                if (double.IsNaN(menuHeight) || menuHeight <= 0) menuHeight = _previewMenu.VisualRadius * 2;
                if (double.IsNaN(menuWidth) || menuWidth <= 0) menuWidth = _config.BasicRadialMenuConfig.OuterRadius * 2;
                if (double.IsNaN(menuHeight) || menuHeight <= 0) menuHeight = _config.BasicRadialMenuConfig.OuterRadius * 2;

                // ===== 自动适配缩放（B 档）=====
                // 用户把半径调大后，菜单会超出画布被裁掉 —— 那样预览就失去了意义
                // （看不到完整轮廓，也不知道整体什么形状）。
                // 这里按"能完整容纳"的比例整体缩小渲染，并在画布下方标注实际比例。
                // ⚠️ 只影响**显示**，绝不回写用户填的数值。
                double fit = ApplyPreviewFit(menuWidth, menuHeight, w, h);

                // 将菜单居中放置在 Canvas 中
                double left = (w - menuWidth) / 2;
                double top = (h - menuHeight) / 2;
                Canvas.SetLeft(_previewMenu, left);
                Canvas.SetTop(_previewMenu, top);

                // 将预览菜单加入画布（仅展示样式，不显示动作标签）
                PreviewCanvas.Children.Add(_previewMenu);
                // 不在样式预览中绘制动作标签，以便只展示菜单样式
                UpdatePreviewFitLabel(fit);

                // 更新 操作配置 页 的预览（该页仍然显示动作标签）
                UpdateOpPreview();
            }
            catch (Exception ex)
            {
                App.Log("UpdatePreview error: " + ex.Message);
            }
        }

        /// <summary>
        /// 按画布可用空间把预览菜单整体缩放"装进去"，返回实际使用的比例。
        /// 装得下时返回 1.0（不做任何缩放），避免小半径时被无谓放大。
        /// 留 8px 边距，防止描边正好贴住画布边缘被切掉半个像素。
        /// </summary>
        private double ApplyPreviewFit(double menuWidth, double menuHeight, double canvasW, double canvasH)
        {
            try
            {
                if (_previewMenu == null) return 1.0;
                if (menuWidth <= 0 || menuHeight <= 0) return 1.0;

                const double pad = 8.0;
                double availW = Math.Max(1, canvasW - pad * 2);
                double availH = Math.Max(1, canvasH - pad * 2);

                double fit = Math.Min(availW / menuWidth, availH / menuHeight);

                // 只缩小不放大：半径很小时保持原尺寸，用户才能直观感到"我改小了"
                if (fit >= 1.0) fit = 1.0;
                // 极端值保护，避免算出 0 或 NaN 让整个图形消失
                if (double.IsNaN(fit) || double.IsInfinity(fit) || fit <= 0.01) fit = 0.01;

                if (fit < 1.0)
                {
                    // 以中心为原点做 RenderTransform 缩放：
                    // 位置仍按原始尺寸居中算，缩放围绕中心，所以居中性不受影响。
                    var tg = new TransformGroup();
                    tg.Children.Add(new ScaleTransform(fit, fit));
                    _previewMenu.RenderTransform = tg;
                    _previewMenu.RenderTransformOrigin = new Point(0.5, 0.5);
                }
                else
                {
                    _previewMenu.RenderTransform = null;
                }

                return fit;
            }
            catch (Exception ex)
            {
                App.Log("ApplyPreviewFit error: " + ex.Message);
                return 1.0;
            }
        }

        /// <summary>
        /// 在预览下方标注当前显示比例。100% 时清空，不显示多余文字。
        /// </summary>
        private void UpdatePreviewFitLabel(double fit)
        {
            try
            {
                if (PreviewFitLabel == null) return;
                if (fit >= 0.999)
                {
                    PreviewFitLabel.Text = string.Empty;
                }
                else
                {
                    PreviewFitLabel.Text = string.Format(
                        "预览按 {0}% 缩放显示，以便完整展示；实际菜单尺寸不受影响。",
                        (int)Math.Round(fit * 100));
                }
            }
            catch (Exception ex)
            {
                App.Log("UpdatePreviewFitLabel error: " + ex.Message);
            }
        }

        private void DrawActionLabelsOnCanvas(Canvas canvas, Point center, RadialMenu menu = null)
        {
            try
            {
                if (canvas == null || _config == null) return;

                const int ITEM_COUNT = 8;
                double baseAngle = -Math.PI / 2;
                double angleStep = 2 * Math.PI / ITEM_COUNT;

                // 标签环半径必须跟随菜单实际尺寸。
                // 原先固定用 BasicRadial 的 OuterRadius 推算，八角星/蜘蛛网/八卦
                // 的真实半尺寸分别是 1.0 / 1.1 / 1.2 倍，标签会整体飘到菜单外面。
                double baseRadius = (menu != null && menu.VisualRadius > 0)
                    ? menu.VisualRadius
                    : _config.BasicRadialMenuConfig.OuterRadius;
                double labelRadius = baseRadius * 0.65;

                for (int i = 0; i < ITEM_COUNT; i++)
                {
                    double angle = baseAngle + i * angleStep;

                    double lx = center.X + labelRadius * Math.Cos(angle);
                    double ly = center.Y + labelRadius * Math.Sin(angle);

                    // 文本放置在圆外一些位置，连线从扇区内侧点到文本中心
                    double innerPointRadius = labelRadius * 0.8;
                    double innerX = center.X + innerPointRadius * Math.Cos(angle);
                    double innerY = center.Y + innerPointRadius * Math.Sin(angle);

                    double outerLabelRadius = labelRadius * 1.25;
                    double labelX = center.X + outerLabelRadius * Math.Cos(angle);
                    double labelY = center.Y + outerLabelRadius * Math.Sin(angle);

                    var labelText = _config.ActionMapping.TryGetValue((MenuItemPosition)i, out var act) ? MapActionToChinese(act) : string.Empty;

                    var tb = new TextBlock
                    {
                        Text = labelText,
                        Foreground = System.Windows.Media.Brushes.Black,
                        FontSize = 12,
                        Padding = new Thickness(6, 3, 6, 3)
                    };

                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var tw = tb.DesiredSize.Width;
                    var th = tb.DesiredSize.Height;

                    // Create rounded Border to host the TextBlock
                    var border = new System.Windows.Controls.Border
                    {
                        Child = tb,
                        Background = System.Windows.Media.Brushes.White,
                        CornerRadius = new CornerRadius(6),
                        BorderBrush = System.Windows.Media.Brushes.LightGray,
                        BorderThickness = new Thickness(1),
                        Opacity = 0.98
                    };

                    // Position label outside circle, align left/right based on angle
                    double labelLeft;
                    if (Math.Cos(angle) >= 0)
                    {
                        // right side: left aligned slightly offset
                        labelLeft = labelX + 8;
                    }
                    else
                    {
                        // left side: right aligned
                        labelLeft = labelX - tw - 8;
                    }
                    Canvas.SetLeft(border, labelLeft);
                    Canvas.SetTop(border, labelY - th / 2);

                    // leader line from inner point to label edge
                    double lineEndX = (Math.Cos(angle) >= 0) ? labelLeft : (labelLeft + tw);
                    double lineEndY = labelY;

                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = innerX,
                        Y1 = innerY,
                        X2 = lineEndX,
                        Y2 = lineEndY,
                        Stroke = System.Windows.Media.Brushes.Gray,
                        StrokeThickness = 1
                    };

                    // arrow head (small triangle) near the label end
                    var arrow = new System.Windows.Shapes.Polygon
                    {
                        Fill = System.Windows.Media.Brushes.Gray,
                        Stroke = System.Windows.Media.Brushes.Gray,
                        StrokeThickness = 0.5
                    };

                    // compute arrow points pointing horizontally towards/away from label
                    double arrowSize = 6;
                    if (Math.Cos(angle) >= 0)
                    {
                        // arrow pointing right -> place slightly before line end
                        arrow.Points = new System.Windows.Media.PointCollection(new[] {
                            new Point(lineEndX - arrowSize, lineEndY - arrowSize/2),
                            new Point(lineEndX - arrowSize, lineEndY + arrowSize/2),
                            new Point(lineEndX, lineEndY)
                        });
                    }
                    else
                    {
                        // arrow pointing left
                        arrow.Points = new System.Windows.Media.PointCollection(new[] {
                            new Point(lineEndX + arrowSize, lineEndY - arrowSize/2),
                            new Point(lineEndX + arrowSize, lineEndY + arrowSize/2),
                            new Point(lineEndX, lineEndY)
                        });
                    }

                    // Tag elements with position index for event handlers
                    border.Tag = i;
                    line.Tag = i;
                    arrow.Tag = i;

                    // Add hover handlers to highlight sector and emphasize line/arrow
                    RoutedEventHandler onEnter = (s, ev) =>
                    {
                        try
                        {
                            int idx = (int)((FrameworkElement)s is FrameworkElement fe ? fe.Tag : i);
                            var posIdx = (MenuItemPosition)idx;
                            if (canvas == PreviewCanvas && _previewMenu != null) _previewMenu.HighlightItem(posIdx);
                            if (canvas == OpPreviewCanvas && _opPreviewMenu != null) _opPreviewMenu.HighlightItem(posIdx);
                            // emphasize line/arrow
                            line.Stroke = System.Windows.Media.Brushes.OrangeRed;
                            line.StrokeThickness = 2;
                            arrow.Fill = System.Windows.Media.Brushes.OrangeRed;
                        }
                        catch { }
                    };

                    RoutedEventHandler onLeave = (s, ev) =>
                    {
                        try
                        {
                            // restore preview
                            if (canvas == PreviewCanvas) UpdatePreview();
                            else if (canvas == OpPreviewCanvas) UpdateOpPreview();
                        }
                        catch { }
                    };

                    border.MouseEnter += (s, e) => onEnter(s, e);
                    border.MouseLeave += (s, e) => onLeave(s, e);
                    line.MouseEnter += (s, e) => onEnter(s, e);
                    line.MouseLeave += (s, e) => onLeave(s, e);
                    arrow.MouseEnter += (s, e) => onEnter(s, e);
                    arrow.MouseLeave += (s, e) => onLeave(s, e);

                    canvas.Children.Add(line);
                    canvas.Children.Add(arrow);
                    canvas.Children.Add(border);
                }
            }
            catch (Exception ex)
            {
                App.Log("DrawActionLabelsOnCanvas error: " + ex.Message);
            }
        }

        private string MapActionToChinese(WindowAction act)
        {
            switch (act)
            {
                case WindowAction.BackToDesktop: return "回到桌面";
                case WindowAction.Minimize: return "最小化窗口";
                case WindowAction.Maximize: return "最大化窗口";
                case WindowAction.LeftHalf: return "左半屏";
                case WindowAction.RightHalf: return "右半屏";
                case WindowAction.TopHalf: return "上半屏";
                case WindowAction.BottomHalf: return "下半屏";
                case WindowAction.TopLeftQuadrant: return "左上分屏";
                case WindowAction.BottomLeftQuadrant: return "左下分屏";
                case WindowAction.TopRightQuadrant: return "右上分屏";
                case WindowAction.BottomRightQuadrant: return "右下分屏";
                case WindowAction.LeftTwoThirds: return "左三分之二屏";
                case WindowAction.RightTwoThirds: return "右三分之二屏";
                case WindowAction.ToggleMaximize: return "最大化/还原切换";
                case WindowAction.ToggleTopMost: return "窗口置顶切换";
                case WindowAction.CloseWindow: return "关闭窗口";
                default: return act.ToString();
            }
        }

        private RadialMenu _opPreviewMenu;
        private void UpdateOpPreview()
        {
            try
            {
                if (_config == null || OpPreviewCanvas == null) return;
                
                // 清除预览画布
                OpPreviewCanvas.Children.Clear();
                
                double w = OpPreviewCanvas.ActualWidth; if (w <= 0) w = 220;
                double h = OpPreviewCanvas.ActualHeight; if (h <= 0) h = 220;
                var center = new Point(w / 2, h / 2);
                
                // 预览固定按 100% 缩放绘制，理由同 UpdatePreview。
                _config.SizingScale = SizingScale.PreviewScale;
                _opPreviewMenu = _menuFactory.CreateMenu(_config.MenuStyle, _config);
                _opPreviewMenu.Initialize(_config, center);
                
                // 获取菜单尺寸并计算居中位置。
                // 兜底值必须取自菜单自报的 VisualRadius：各样式绘制留白不同，
                // 用 BasicRadial 的半径兜底会让另外三种样式的预览尺寸/位置全部偏掉。
                double menuWidth = _opPreviewMenu.Width;
                double menuHeight = _opPreviewMenu.Height;
                if (double.IsNaN(menuWidth) || menuWidth <= 0) menuWidth = _opPreviewMenu.VisualRadius * 2;
                if (double.IsNaN(menuHeight) || menuHeight <= 0) menuHeight = _opPreviewMenu.VisualRadius * 2;
                if (double.IsNaN(menuWidth) || menuWidth <= 0) menuWidth = _config.BasicRadialMenuConfig.OuterRadius * 2;
                if (double.IsNaN(menuHeight) || menuHeight <= 0) menuHeight = _config.BasicRadialMenuConfig.OuterRadius * 2;
                
                // 将菜单居中放置在 Canvas 中
                double left = (w - menuWidth) / 2;
                double top = (h - menuHeight) / 2;
                Canvas.SetLeft(_opPreviewMenu, left);
                Canvas.SetTop(_opPreviewMenu, top);
                OpPreviewCanvas.Children.Add(_opPreviewMenu);
            }
            catch (Exception ex)
            {
                App.Log("UpdateOpPreview error: " + ex.Message);
            }
        }

        
        private void HighlightPositionLine(int positionIndex, bool highlight, System.Windows.Shapes.Line[] lines = null)
        {
            // 此方法暂时保留，但当前不再使用折线悬停效果
        }

        private void AddSliderEventHandlers()
        {
            try
            {
                // 已改用整数文本框控件，保留此方法为兼容（无操作）
                return;
            }
            catch (Exception ex)
            {
                App.Log("AddSliderEventHandlers error: " + ex.Message);
            }
        }

        private void OnSaveButton_Click_New(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveConfig();
                _configManager.SaveConfig(_config);
                
                // 应用自启动设置
                SystemIntegration.AutoStartManager.ApplyAutoStartSetting(_config.AutoStart);
                
                // 应用触发时长设置
                App.UpdateTriggerDelay(_config.TriggerDelay);
                App.UpdateXuanKongSi(_config.XuanKongSi);
                
                SetFooterStatus("已保存");
                App.Log($"Settings saved, auto-start: {_config.AutoStart}, trigger-delay: {_config.TriggerDelay}");
            }
            catch (Exception ex)
            {
                SetFooterStatus("保存失败，详见日志");
                App.Log("OnSaveButton_Click_New error: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Do not persist immediately; update UI to default AppConfig
                _config = new AppConfig();
                EnsureThicknessFallback();
                PopulateUIFromConfig();
                SetFooterStatus("已恢复默认值，点「确定」后生效");
            }
            catch (Exception ex)
            {
                App.Log("RestoreDefaultsButton_Click error: " + ex.Message);
            }
        }

        /// <summary>
        /// 底部状态提示。短暂显示后自动淡出，避免常驻文字干扰视线。
        /// 2.6 秒足够看清，又不会在下次操作时还挂着上一条消息。
        /// </summary>
        private DispatcherTimer _footerStatusTimer;

        private void SetFooterStatus(string text)
        {
            try
            {
                if (FooterStatusText == null) return;
                FooterStatusText.Text = text ?? string.Empty;
                FooterStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Accent");

                if (_footerStatusTimer == null)
                {
                    _footerStatusTimer = new DispatcherTimer();
                    _footerStatusTimer.Interval = TimeSpan.FromMilliseconds(2600);
                    _footerStatusTimer.Tick += (s, e) =>
                    {
                        _footerStatusTimer.Stop();
                        try
                        {
                            FooterStatusText.Text = string.Empty;
                            FooterStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextFaint");
                        }
                        catch { }
                    };
                }
                _footerStatusTimer.Stop();
                _footerStatusTimer.Start();
            }
            catch (Exception ex)
            {
                App.Log("SetFooterStatus error: " + ex.Message);
            }
        }

        // Accent color UI/logic removed per request

        /// <summary>
        /// 弹出颜色选择器，把选中颜色写回文本框并回写到配置，随后刷新预览。
        /// 圆环/蜘蛛网/八卦四个面板共用此逻辑，避免为每个按钮复制一份代码。
        ///
        /// 原先用的是系统 <c>System.Windows.Forms.ColorDialog</c>（RGB/HSL 分页那套），
        /// 已替换为自绘的 <see cref="ColorPickerWindow"/>：SV 二维调色板所见即所得，
        /// 与本项目设置界面的视觉语言也统一。
        /// </summary>
        private void PickColorInto(System.Windows.Controls.TextBox targetBox, Action<string> applyToConfig)
        {
            try
            {
                if (targetBox == null) return;

                var dlg = new ColorPickerWindow(targetBox.Text);
                dlg.Owner = this;

                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedHex))
                {
                    var hex = dlg.SelectedHex;
                    targetBox.Text = hex;
                    if (_config != null && applyToConfig != null) applyToConfig(hex);
                    UpdatePreview();
                }
            }
            catch (Exception ex)
            {
                App.Log("PickColorInto error: " + ex.Message);
            }
        }

        private void BasicRingColorPickButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new ColorPickerWindow(BasicRingColorBox.Text);
                dlg.Owner = this;

                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedHex))
                {
                    var hex = dlg.SelectedHex;
                    BasicRingColorBox.Text = hex;
                    if (_config != null) _config.BasicRadialMenuConfig.RingColor = hex;
                    UpdatePreview();
                }
            }
            catch (Exception ex)
            {
                App.Log("BasicRingColorPickButton_Click error: " + ex.Message);
            }
        }

        private void BasicHighlightColorPickButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 统一走自绘取色器。原先这里用的是系统 ColorDialog，
                // 与「圆环颜色」「线条颜色」等其余色块按钮的行为不一致 ——
                // 同一个界面里两种取色器，用户会以为坏了。
                var dlg = new ColorPickerWindow(BasicHighlightColorBox.Text);
                dlg.Owner = this;

                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedHex))
                {
                    var hex = dlg.SelectedHex;
                    BasicHighlightColorBox.Text = hex;
                    if (_config != null) _config.BasicRadialMenuConfig.HighlightColor = hex;
                    UpdatePreview();
                }
            }
            catch (Exception ex)
            {
                App.Log("BasicHighlightColorPickButton_Click error: " + ex.Message);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Window may be shown non-modally; avoid setting DialogResult.
            this.Close();
        }
    }

    /// <summary>
    /// 把 "#RRGGBB" / "#AARRGGBB" 字符串转成 Brush，供设置面板里的色块按钮使用。
    ///
    /// 存在的理由：绑定表达式**不走**类型转换器，把 string 直接绑到
    /// Border.Background 上不会自动变成颜色 —— 必须自己转一次。
    ///
    /// 容错：用户正在输入时值可能是 "#12" 这种半截内容，
    /// 转不出来就返回透明，绝不抛异常（抛了会连累整个界面渲染）。
    /// </summary>
    public class HexToBrushConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            try
            {
                var text = value as string;
                if (string.IsNullOrWhiteSpace(text)) return System.Windows.Media.Brushes.Transparent;

                text = text.Trim();
                if (!text.StartsWith("#")) text = "#" + text;

                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                return System.Windows.Media.Brushes.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}