using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
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
            // 绑定调色盘按钮
            try
            {
                BasicRingColorPickButton.Click += BasicRingColorPickButton_Click;
                BasicHighlightColorPickButton.Click += BasicHighlightColorPickButton_Click;
                // 绑定颜色文本框失去焦点事件（仅圆环）
                BasicRingColorBox.LostFocus += ColorTextBox_LostFocus;
                BasicHighlightColorBox.LostFocus += ColorTextBox_LostFocus;
            }
            catch { }
            // 加载配置
            LoadConfig();
            
            // 添加滑块值变化事件处理
            AddSliderEventHandlers();
            
            // 添加按钮点击事件（使用新的安全处理器，避免旧版本残留）
            SaveButton.Click += OnSaveButton_Click_New;
            RestoreDefaultsButton.Click += RestoreDefaultsButton_Click;

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
                    new { Key = WindowAction.RightTwoThirds, Label = "右三分之二屏" }
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

                // Update UI + in-memory config
                if (_config?.XuanKongSi == null) _config.XuanKongSi = new XuanKongSiConfig();
                _config.XuanKongSi.ImageFileName = Path.GetFileName(dest);

                XuanKongSiImagePathLabel.Text = Path.GetFileName(dest);
                XuanKongSiContentCombo.SelectedValue = XuanKongSiContentType.Image;
                UpdateXuanKongSiPanels();
            }
            catch (Exception ex)
            {
                App.Log("XuanKongSiPickImageButton_Click error: " + ex.Message);
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
                
                // 验证颜色格式是否有效
                try
                {
                    System.Drawing.ColorTranslator.FromHtml(colorValue);
                }
                catch
                {
                    return; // 无效颜色格式，不更新
                }
                
                // 更新对应的配置
                if (tb == BasicRingColorBox) _config.BasicRadialMenuConfig.RingColor = colorValue;
                else if (tb == BasicHighlightColorBox) _config.BasicRadialMenuConfig.HighlightColor = colorValue;
                
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

        private void PopulateUIFromConfig()
        {
            try
            {
                if (_config == null) return;

                // Only keep the ring menu available; force style to BasicRadial
                _config.MenuStyle = MenuStyle.BasicRadial;

                // 菜单样式仅保留圆环，隐藏其他选项
                BasicRadialRadio.IsChecked = true;

                BasicOuterRadiusBox.Text = ((int)_config.BasicRadialMenuConfig.OuterRadius).ToString();
                BasicInnerRadiusBox.Text = ((int)_config.BasicRadialMenuConfig.InnerRadius).ToString();
                BasicRingColorBox.Text = _config.BasicRadialMenuConfig.RingColor;
                BasicHighlightColorBox.Text = _config.BasicRadialMenuConfig.HighlightColor;

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

                try
                {
                    XuanKongSiImagePathLabel.Text = string.IsNullOrWhiteSpace(sp.ImageFileName)
                        ? "(默认：小鹤双拼键位图)"
                        : sp.ImageFileName;
                }
                catch { }

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

        private void SaveConfig()
        {
            try
            {
                // 保存菜单样式（仅圆环可用）
                _config.MenuStyle = MenuStyle.BasicRadial;
                
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
            // 切换配置面板显示并更新预览
            UpdateConfigPanels();
            // 更新 _config.MenuStyle，仅允许圆环
            _config.MenuStyle = MenuStyle.BasicRadial;
            BasicRadialRadio.IsChecked = true;

            UpdatePreview();
        }

        private void UpdateConfigPanels()
        {
            BasicRadialConfigGrid.Visibility = Visibility.Visible;
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
                if (w <= 0) w = 300;
                if (h <= 0) h = 300;
                var center = new Point(w / 2, h / 2);

                // 创建并初始化菜单，用于预览
                _previewMenu = _menuFactory.CreateMenu(_config.MenuStyle, _config);
                _previewMenu.Initialize(_config, center);

                // 获取菜单尺寸并计算居中位置
                double menuWidth = _previewMenu.Width;
                double menuHeight = _previewMenu.Height;
                if (double.IsNaN(menuWidth) || menuWidth <= 0) menuWidth = _config.BasicRadialMenuConfig.OuterRadius * 2;
                if (double.IsNaN(menuHeight) || menuHeight <= 0) menuHeight = _config.BasicRadialMenuConfig.OuterRadius * 2;
                
                // 将菜单居中放置在 Canvas 中
                double left = (w - menuWidth) / 2;
                double top = (h - menuHeight) / 2;
                Canvas.SetLeft(_previewMenu, left);
                Canvas.SetTop(_previewMenu, top);
                
                // 将预览菜单加入画布（仅展示样式，不显示动作标签）
                PreviewCanvas.Children.Add(_previewMenu);
                // 不在样式预览中绘制动作标签，以便只展示菜单样式
                // 更新 操作配置 页 的预览（该页仍然显示动作标签）
                UpdateOpPreview();
            }
            catch (Exception ex)
            {
                App.Log("UpdatePreview error: " + ex.Message);
            }
        }

        private void DrawActionLabelsOnCanvas(Canvas canvas, Point center)
        {
            try
            {
                if (canvas == null || _config == null) return;

                const int ITEM_COUNT = 8;
                double baseAngle = -Math.PI / 2;
                double angleStep = 2 * Math.PI / ITEM_COUNT;
                for (int i = 0; i < ITEM_COUNT; i++)
                {
                    double angle = baseAngle + i * angleStep;

                    double labelRadius = _config.BasicRadialMenuConfig.OuterRadius * 0.65;

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
                
                _opPreviewMenu = _menuFactory.CreateMenu(_config.MenuStyle, _config);
                _opPreviewMenu.Initialize(_config, center);
                
                // 获取菜单尺寸并计算居中位置
                double menuWidth = _opPreviewMenu.Width;
                double menuHeight = _opPreviewMenu.Height;
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
                
                App.Log($"Settings saved, auto-start: {_config.AutoStart}, trigger-delay: {_config.TriggerDelay}");
            }
            catch (Exception ex)
            {
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
            }
            catch (Exception ex)
            {
                App.Log("RestoreDefaultsButton_Click error: " + ex.Message);
            }
        }

        // Accent color UI/logic removed per request

        private void BasicRingColorPickButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var dlg = new System.Windows.Forms.ColorDialog())
                {
                    try
                    {
                        var txt = BasicRingColorBox.Text.Trim();
                        if (!string.IsNullOrEmpty(txt)) dlg.Color = System.Drawing.ColorTranslator.FromHtml(txt);
                    }
                    catch { }

                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        var hex = System.Drawing.ColorTranslator.ToHtml(dlg.Color);
                        BasicRingColorBox.Text = hex;
                        if (_config != null) _config.BasicRadialMenuConfig.RingColor = hex;
                        UpdatePreview();
                    }
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
                using (var dlg = new System.Windows.Forms.ColorDialog())
                {
                    try
                    {
                        var txt = BasicHighlightColorBox.Text.Trim();
                        if (!string.IsNullOrEmpty(txt)) dlg.Color = System.Drawing.ColorTranslator.FromHtml(txt);
                    }
                    catch { }

                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        var hex = System.Drawing.ColorTranslator.ToHtml(dlg.Color);
                        BasicHighlightColorBox.Text = hex;
                        if (_config != null) _config.BasicRadialMenuConfig.HighlightColor = hex;
                        UpdatePreview();
                    }
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
}