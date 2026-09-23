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

        /// <summary>
        /// 「效果预览」的衍生窗口 —— 紧贴设置窗口**可见右缘**、高度与母窗口的
        /// **可见框等高**（见 <see cref="MenuPreviewWindow"/>）。
        /// <para>
        /// 懒创建：只有真正要显示预览时才 new。构造设置窗口时不会多出一个窗口，
        /// 而 <c>--settingscheck</c> 这类只跑一两个页面的场景也不会被牵连。
        /// </para>
        /// </summary>
        private MenuPreviewWindow _previewWindow;

        /// <summary>防重入：见 UpdatePreview 开头的说明。</summary>
        private bool _inPreviewUpdate;

        /// <summary>「菜单样式」在 MainTabControl 里的页面索引 —— 预览只在这一页出现。</summary>
        private const int MenuStylePageIndex = 0;

        /// <summary>「操作配置」在 MainTabControl 里的页面索引 —— 三栏布局只在这一页画。</summary>
        private const int OpConfigPageIndex = 1;

        // ==================== 操作配置页：三栏 + 折线引线 ====================
        //
        // 8 个选区**同时**列在菜单左右两侧（各 4 行），每行一个动作下拉框。
        // 数组下标 = MenuItemPosition 的整数值（0..7），与 XAML 里各行的 Tag 一致，
        // 所以「第几行是哪个扇区」不需要另立映射表 —— 下标本身就是位置。
        //
        // ⚠️ 左右分组由**方位**决定，不是随手分的：
        //   Position1~4（上 / 右上 / 右 / 右下）在右列，
        //   Position5~8（下 / 左下 / 左 / 左上）在左列。
        // 这样每个扇区的引线都朝自己那一侧引出，8 条线天然不交叉；
        // 列内次序也按方位排，行的上下序与扇区的上下序一致。
        private readonly Border[] _opRows = new Border[8];
        private readonly ComboBox[] _opCombos = new ComboBox[8];

        /// <summary>
        /// 引线对象与扇区端的锚点圆点，各 8 份（选中时一起变蓝，见 PaintOpConnector）。
        /// <para>
        /// 用 <see cref="System.Windows.Shapes.Path"/> 而不是 Polyline，是为了让几何**可被断言**：
        /// 自检直接读 <c>Data</c> 里的段数/段类型来钉"还是不是折线"（见 CheckOperationConfigLayout）。
        /// 2026-09-22 起折点是**直角**（两段直线），圆角那版已被用户否掉。
        /// </para>
        /// </summary>
        private readonly System.Windows.Shapes.Path[] _opLines = new System.Windows.Shapes.Path[8];
        private readonly System.Windows.Shapes.Ellipse[] _opDots = new System.Windows.Shapes.Ellipse[8];

        /// <summary>
        /// 各锚点的**中心**坐标（引线层坐标系）。
        /// 选中时锚点会放大，而 Canvas 定位的是元素左上角 —— 不记中心就只能按旧尺寸摆，
        /// 放大的点会整体偏向右下。几何算一次存下来，PaintOpConnector 直接用。
        /// </summary>
        private readonly Point[] _opDotCenters = new Point[8];

        /// <summary>回填下拉框期间置位，抑制 SelectionChanged（否则"读配置"会被当成"改配置"）。</summary>
        private bool _inOpComboSync;

        /// <summary>
        /// 中间菜单**实际**用的显示缩放（引线锚点半径要跟着缩）。
        /// <para>
        /// 正常窗口尺寸下四种样式都是 1.0（真实尺寸）—— 只有在窗口被压窄、
        /// 锚点环将要越进行的内边缘时才会 &lt;1，见 <see cref="ComputeOpFitCap"/>。
        /// 绘制前一定会被 <see cref="UpdateOpPreview"/> 或引线刷新重算一次。
        /// </para>
        /// </summary>
        private double _opFit = 1.0;

        /// <summary>
        /// 引线锚点（扇区端那颗实心点）与图形**可见外缘**之间留的空隙。
        /// <para>
        /// 取 6 = 选中态圆点半径（10 / 2）再 +1：点放大到选中态时刚好贴住图形，不压上去。
        /// **不能再大** —— 曾经这个空隙是 12，而且基准是 <c>VisualRadius</c>（含各样式的
        /// 布局留白），两者叠加后八卦那版锚点离图案 28px，8 颗点像悬空的一圈杂质
        /// （2026-09-22 用户反馈"引线起点应该要更接近菜单图案"）。
        /// </para>
        /// </summary>
        private const double OpAnchorGap = 6.0;

        /// <summary>
        /// 选中态锚点圆点的半径（未选中 7px、选中 10px，见 <see cref="PaintOpConnector"/>）。
        /// <para>
        /// 反解显示缩放上限时要按**放大后**的半径算 —— 否则选中时那颗点会越过行的内边缘。
        /// </para>
        /// </summary>
        private const double OpDotSelectedRadius = 5.0;

        /// <summary>
        /// 锚点环统一落在图形**可见外缘之外**多少像素处。
        /// <para>
        /// 2026-09-22 第三轮：**八条引线的几何只由这一个数决定**，不再让各样式的菜单
        /// 各自"把中间那列用满"。原因是一条硬约束 —— 行是等距的、锚点 y 是正弦分布的，
        /// 两者只有在**垂直跨度相当时**才可能大范围对齐（对齐 = 该条引线退化成一段水平直线）。
        /// 各样式 <c>DrawnRadius</c> 差一倍（圆环 50、八卦 85.5），若各按自己的空间放大，
        /// 锚点环半径就会在 99～124 之间飘，行距却只有一个值 —— 圆环那版必然对不齐
        /// （实测最大偏差 42.7px，线绕得最凶）。
        /// </para>
        /// <para>
        /// 取值 93 是实测扫描出来的（见 tmp/opt_rows2/3/4.py）：它让四种样式的锚点环半径
        /// 统一到 99，恰好落在"跨度匹配"点上 —— 12 段（4 条一段 + 4 条两段）为全局最优。
        /// **上限由八卦决定**：画布 252 − 边距 16 → 元素半宽 ≤118，而八卦的 VisualRadius 是
        /// 108（含 1.2 的外扩）→ 可见半径最多 93.4，所以 93 是四种样式**都**够得着的值。
        /// </para>
        /// <para>
        /// ⚠️ 代价要认：圆环 / Headshot / 蜘蛛网原来是"各自把中间那列用满"
        /// （可见半径 118 / 115.7 / 107.3），现在一律收到 93（−21% / −19% / −13%）。
        /// 换来的是引线几何在四种样式下**完全一致**，而且最长竖段从 46px 级降到 19px 级
        /// （不统一时圆环那版总甩出一段 46px 的长竖段 —— 实测 h=50/g=12/偏移 +10 的最好情况
        /// 也只有总段 49、最长 45.7px）。
        /// 这一页的菜单本来就是示意图、不承担"展示真实尺寸"的职责，所以按引线整齐来取舍。
        /// </para>
        /// </summary>
        private const double OpAnchorRingVisibleR = 93.0;

        /// <summary>
        /// 引线起点距下拉框朝内**边缘**留出的像素数（起点 x = 框边 ∓ 本值）。
        /// <para>
        /// 取 2 而不是 0：零点几像素的抗锯齿会让线头与框边糊在一起、显脏；
        /// 也不能多留（试过 6px，屏幕上看着像线和框"断开"了 —— 那更像漏画）。
        /// </para>
        /// <para>
        /// ⚠️ 起点 x 量的是**下拉框**、不是行 Border：行 Border 带内边距，比下拉框宽一圈，
        /// 按行算会让线头悬在行的留白里（2026-09-22 踩过）。
        /// </para>
        /// </summary>
        private const double OpEdgeGap = 2.0;

        /// <summary>
        /// 「竖直走廊」比锚点环再往外多少像素 —— 八条引线的折点全落在这一条竖线上。
        /// <para>
        /// 2026-09-22 第四轮（按用户标注的红线改）：起点固定在**下拉框侧边的中点**，
        /// 于是"起点高度"不再可调，锚点 y 与它不等时就必须找一条竖直段走完差值。
        /// 这条竖线不能贴在下拉框边上（那样像"线沿着框描边"），所以放到图形与下拉框之间、
        /// 锚点环再往外的位置：<c>走廊 x = 菜单中心 ± (锚点环半径 + 本值)</c>。
        /// </para>
        /// <para>
        /// 取 30 是按用户红线量的（红线走廊离菜单中心 129、锚点环半径 99 → 30）。
        /// 它的作用：保证"走廊 → 锚点"这一小段水平线不会短到看不见 ——
        /// 正左 / 正右那两个锚点正好顶在锚点环的左右极值上，接入段长度就等于本值。
        /// </para>
        /// <para>
        /// ⚠️ 走廊由**锚点环半径**推导（而不是写死像素），窗口变窄时锚点环被
        /// <see cref="ComputeOpFitCap"/> 收小，走廊会自动跟着往里走，不会挤到下拉框上。
        /// </para>
        /// </summary>
        private const double OpCorridorLead = 30.0;

        /// <summary>
        /// 菜单缩放到画布时四边留的余量，防止描边正好贴住画布边缘被切掉半个像素。
        /// 提到类级是想让自检能反射读到 —— 断言"菜单按画布尽量大"时要按同一个边距算期望值。
        /// </summary>
        private const double MenuFitPad = 8.0;

        /// <summary>
        /// 「操作配置」页中间那张菜单的实例。
        /// 与「菜单样式」页的 <see cref="_previewMenu"/> **是两个独立对象** ——
        /// 那一页在跑循环高亮动画，两者共用会互相抢高亮。
        /// </summary>
        private RadialMenu _opPreviewMenu;

        /// <summary>引线重算是否已经排好队 —— 同一轮布局里只排一次。</summary>
        private bool _opConnectorRefreshQueued;

        // 循环高亮动画定时器
        private DispatcherTimer _highlightCycleTimer;
        private int _currentHighlightIndex = 0;

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

            // 先把 8 个选区行的控件收进数组，再初始化下拉框 ——
            // 顺序反了 InitializeActionCombos 拿不到这些 ComboBox。
            BindOpActionRows();

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
            // 上限的取值依据 —— 预览画布逻辑尺寸（现为 300，见 MenuPreviewWindow.xaml），
            // 半径超过它的一半就无法完整显示：
            //   圆环/蜘蛛网/八卦 半径 → 130；内半径额外受"必须小于外半径"约束，给 120。
            // ⚠️ 画布从 280 放大到 300 后，理论上限本可放宽到 150；这里**有意不动**，
            // 避免把"布局改动"和"告警阈值调整"混在一次改动里（阈值只是提示，
            // 超了也只会让预览自动缩放，不影响实际菜单尺寸）。
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

            // 画布大小改变时更新预览。
            // 只剩「操作配置」页的画布了 —— 「菜单样式」的预览画布已移到衍生窗口，
            // 它的等价挂钩在 EnsurePreviewWindow() 里。
            OpPreviewCanvas.SizeChanged += (s, e) => UpdateOpPreview();

            // 衍生窗口的显示/隐藏/贴合全部由本窗口驱动（预览窗口自己不主动显示）：
            //   - Loaded：首次显示预览
            //   - StateChanged / 切页：最小化或切走 → 隐藏
            //   - LocationChanged / SizeChanged：拖动或改尺寸 → 重新贴到右侧界外
            this.Loaded += (s, e) => UpdatePreview();
            this.LocationChanged += (s, e) => _previewWindow?.Reposition();
            this.SizeChanged += (s, e) => _previewWindow?.Reposition();
            this.StateChanged += (s, e) => UpdatePreview();
            
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

            // 母窗口关闭 → 衍生窗口一并关掉。
            // Owner 关系本已让 WPF 顺带关闭它，这里显式再关一次是为了结果确定：
            // 关闭顺序、以及"--settingscheck 跑完自动退出"这类路径都不留悬挂窗口。
            this.Closed += (s, e) =>
            {
                try
                {
                    if (_previewWindow != null)
                    {
                        _previewWindow.Close();
                        _previewWindow = null;
                    }
                }
                catch (Exception ex)
                {
                    App.Log("close preview window error: " + ex.Message);
                }
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

                // 8 个选区行各有一个下拉框，数据源**共用同一份**动作清单。
                // 共用数组实例是安全的：ItemsSource 只被读，不会被任何一方改写。
                for (int i = 0; i < _opCombos.Length; i++)
                {
                    var combo = _opCombos[i];
                    if (combo == null) continue;
                    combo.ItemsSource = items;
                    combo.DisplayMemberPath = "Label";
                    combo.SelectedValuePath = "Key";
                }
            }
            catch (Exception ex)
            {
                App.Log("InitializeActionCombos error: " + ex.Message);
            }
        }

        /// <summary>
        /// 把 XAML 里 8 个选区行的控件收进按位置索引寻址的数组，并挂上布局钩子。
        /// <para>
        /// 数组下标 = <see cref="MenuItemPosition"/> 的整数值，与 XAML 里各行的 Tag 一致 ——
        /// 「第几行是哪个扇区」不需要另立映射表。
        /// </para>
        /// <para>
        /// 行内**没有**位置文字（2026-09-22 按用户要求去掉）：行的身份只剩"下标 + 引线"两个凭据，
        /// 所以这里不再往行上写任何文字。XAML 里 8 个 Border 的 Name 与左右列的分组
        /// 就是全部的身份声明，改 Name 或挪行必须同步这里。
        /// </para>
        /// </summary>
        private void BindOpActionRows()
        {
            try
            {
                var rows = new[] { OpRowP1, OpRowP2, OpRowP3, OpRowP4, OpRowP5, OpRowP6, OpRowP7, OpRowP8 };
                var combos = new[] { OpComboP1, OpComboP2, OpComboP3, OpComboP4, OpComboP5, OpComboP6, OpComboP7, OpComboP8 };

                for (int i = 0; i < 8; i++)
                {
                    _opRows[i] = rows[i];
                    _opCombos[i] = combos[i];

                    // 「设置项获得焦点 → 菜单高亮切到对应选区」。
                    // 挂在代码里而不是在 XAML 里给 8 个下拉框各写一遍 GotFocus="..."：
                    // 位置索引从 Tag 读，写一遍就够，也不会漏挂某一行。
                    if (combos[i] != null)
                    {
                        combos[i].GotFocus += OpActionCombo_GotFocus;
                        combos[i].DropDownOpened += OpActionCombo_DropDownOpened;
                    }
                }

                // 三栏布局一旦落定就重算引线。切页那一刻所有控件尺寸都是 0，
                // 只有布局跑完才知道每行的边缘在哪、菜单中心在哪。
                if (OpLayoutGrid != null)
                {
                    OpLayoutGrid.SizeChanged += (s, e) => UpdateOpConnectors();
                }

                // 卡片要顶满整页（否则"面板吊在窗口里"，见 SyncOpPageCardHeight）。
                // 挂在 ScrollChanged 上而不是窗口 SizeChanged：视口高度才是真依据，
                // 它同时覆盖窗口尺寸变化、Padding 变化与切页首次布局。
                if (OpPageScroll != null)
                {
                    OpPageScroll.ScrollChanged += (s, e) =>
                    {
                        if (Math.Abs(e.ViewportHeightChange) > 0.5 || Math.Abs(e.ViewportWidthChange) > 0.5)
                        {
                            SyncOpPageCardHeight();
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                App.Log("BindOpActionRows error: " + ex.Message);
            }
        }

        private void BindPositionComboEvents()
        {
            // 事件已在 XAML 里以 SelectionChanged="OpActionCombo_SelectionChanged" 直接挂好，
            // 8 个下拉框共用同一个处理函数（各自用 Tag 携带自己的位置索引）。
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

                // 本屏缩放比：仅用于把 DIP 讲成物理像素给人看（菜单尺寸本身不用它）
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
                // 循环高亮各扇区以展示高亮颜色效果。
                // 预览窗口没显示时（切到别的页、母窗口最小化）不必空转 ——
                // 高亮的视觉对象在那个窗口里，看不见的刷新没有意义。
                var pos = (MenuItemPosition)_currentHighlightIndex;
                if (_previewMenu != null && _previewWindow != null && _previewWindow.IsVisible)
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
                    // 预览只属于「菜单样式」页：切走 → 隐藏，切回 → 重新显示。
                    // ⚠️ 必须放在下面的 _suppressNavSync 早退**之前**：
                    // 点左侧导航切页时该标志为 true，放后面会被直接 return 掉。
                    UpdatePreview();

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

                // 操作映射 - 默认选中第一个扇区，并把 8 行的下拉框按配置回填。
                // 回填放在这里（而不是只靠 UpdatePreview 顺带调）是因为 PopulateUIFromConfig
                // 也会在"启动时配置被外部改写"等路径上单独调用，那时不一定重绘预览。
                _selectedSectorIndex = 0;
                UpdateOpActionCombos();
                UpdateOpSelectionHighlight();

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

        /// <summary>
        /// 把配置里的动作映射回填到 8 个下拉框。
        /// <para>
        /// ⚠️ 必须用 <see cref="_inOpComboSync"/> 抑制 SelectionChanged：
        /// 回填会触发它，不抑制就会把"刚读出来的值"当成"用户改的"再写回配置。
        /// </para>
        /// </summary>
        private void UpdateOpActionCombos()
        {
            try
            {
                if (_config == null) return;

                _inOpComboSync = true;
                for (int i = 0; i < _opCombos.Length; i++)
                {
                    var combo = _opCombos[i];
                    if (combo == null) continue;

                    WindowAction action;
                    if (_config.ActionMapping != null
                        && _config.ActionMapping.TryGetValue((MenuItemPosition)i, out action))
                    {
                        combo.SelectedValue = action;
                    }
                    else
                    {
                        combo.SelectedIndex = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log("UpdateOpActionCombos error: " + ex.Message);
            }
            finally
            {
                _inOpComboSync = false;
            }
        }

        /// <summary>
        /// 刷新「当前选中哪个选区」的视觉：菜单扇区高亮 + 对应行加蓝框 + 那条引线变蓝。
        /// <para>
        /// 选中态有两个来源 —— 点菜单上的扇区、改某行的下拉框 —— 两者都收敛到这里，
        /// 保证"当前选中"只有一份真相（<see cref="_selectedSectorIndex"/>）。
        /// </para>
        /// </summary>
        private void UpdateOpSelectionHighlight()
        {
            try
            {
                var accent = TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue;
                var accentSoft = TryFindResource("AccentSoft") as Brush ?? Brushes.Transparent;

                for (int i = 0; i < 8; i++)
                {
                    bool on = i == _selectedSectorIndex;

                    // 行内没有位置文字了，选中态只能靠"整行的底与框"表达 ——
                    // 所以底色 + 1px 框都在这里给（框始终占位，见 OpRowStyle 的注释）。
                    var row = _opRows[i];
                    if (row != null)
                    {
                        row.Background = on ? accentSoft : Brushes.Transparent;
                        row.BorderBrush = on ? accent : Brushes.Transparent;
                    }

                    PaintOpConnector(i, on);
                }
            }
            catch (Exception ex)
            {
                App.Log("UpdateOpSelectionHighlight error: " + ex.Message);
            }
        }

        /// <summary>
        /// 只改某条引线的颜色与粗细（几何不动）。
        /// 选中态切换走这条，避免每点一下就重算 8 条引线的几何。
        /// </summary>
        private void PaintOpConnector(int index, bool on)
        {
            if (index < 0 || index >= 8) return;

            try
            {
                var brush = on
                    ? (TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue)
                    : (TryFindResource("OpConnectorLine") as Brush ?? Brushes.Silver);
                double thickness = on ? 1.8 : 1.3;

                var line = _opLines[index];
                if (line != null)
                {
                    line.Stroke = brush;
                    line.StrokeThickness = thickness;
                }

                // 锚点做成**实心小圆点**（选中时略放大）。
                // 早先是"白底 + 同色描边"的空心点，但锚点本来就落在图形**之外**的空白区，
                // 空心在白卡上等于每个位置留了个小洞，8 个散布在菜单周围像是杂质。
                // 放大要连着挪左上角 —— Canvas 定位的是元素左上角，不是中心。
                var dot = _opDots[index];
                if (dot != null)
                {
                    dot.Fill = brush;
                    double d = on ? 10.0 : 7.0;
                    dot.Width = d;
                    dot.Height = d;
                    Canvas.SetLeft(dot, _opDotCenters[index].X - d / 2.0);
                    Canvas.SetTop(dot, _opDotCenters[index].Y - d / 2.0);
                }
            }
            catch { }
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
                double w = OpPreviewCanvas.ActualWidth > 0 ? OpPreviewCanvas.ActualWidth : 252;
                double h = OpPreviewCanvas.ActualHeight > 0 ? OpPreviewCanvas.ActualHeight : 252;

                double centerX = w / 2;
                double centerY = h / 2;

                double dx = clickPos.X - centerX;
                double dy = clickPos.Y - centerY;

                // 与 RadialMenu.SelectSectorByDirection 同一套角度约定：
                // Position1（正上方）为 0 号，顺时针每 45° 一格。
                //
                // ⚠️ 这里刻意不复用菜单自己的判定方法：那套判定带「中心死区」语义
                // （落在图形内缘之内返回 null）—— 那是**运行时松开鼠标**的语义，
                // 用来实现"退回中心就反悔"。在设置界面上点哪儿都该选中一个扇区，
                // 不该存在"点了没反应"的位置。
                double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                angle = (angle + 90 + 360) % 360;
                angle = (angle + 22.5) % 360;

                int sectorIndex = (int)(angle / 45) % 8;
                _selectedSectorIndex = sectorIndex;

                _opPreviewMenu?.HighlightItem((MenuItemPosition)sectorIndex);
                UpdateOpSelectionHighlight();
            }
            catch (Exception ex)
            {
                App.Log("OpPreviewCanvas_MouseLeftButtonDown error: " + ex.Message);
            }
        }

        /// <summary>
        /// 8 个选区行共用的下拉框事件。行的位置索引挂在 <c>ComboBox.Tag</c> 上
        /// （0..7 = <see cref="MenuItemPosition"/>），所以不需要 8 个各自的方法。
        /// </summary>
        private void OpActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_inOpComboSync) return;
                if (_config == null || _config.ActionMapping == null) return;

                var combo = sender as ComboBox;
                if (combo == null || combo.Tag == null) return;

                int index;
                if (!int.TryParse(combo.Tag.ToString(), out index)) return;
                if (index < 0 || index >= 8) return;

                if (combo.SelectedValue is WindowAction action)
                {
                    _config.ActionMapping[(MenuItemPosition)index] = action;

                    // 改了哪个选区就把菜单上那个扇区点亮 —— 让"下拉框 ↔ 扇区"的对应关系
                    // 在改动的那一刻确认一次，不用回头去数折线。
                    _selectedSectorIndex = index;
                    _opPreviewMenu?.HighlightItem((MenuItemPosition)index);
                    UpdateOpSelectionHighlight();
                }
            }
            catch (Exception ex)
            {
                App.Log("OpActionCombo_SelectionChanged error: " + ex.Message);
            }
        }

        /// <summary>
        /// 某个设置项（选区行）获得焦点 → 菜单高亮切到它对应的扇区。
        /// <para>
        /// 焦点与"选中"在这一页是同一个概念：用户点/切到哪一行的下拉框，就说明他在看哪一格的配置，
        /// 菜单上跟着点亮那一格，省得回头数引线。所以这里直接改
        /// <see cref="_selectedSectorIndex"/> —— 菜单扇区高亮、行的底与框、那条引线
        /// 三处由 <see cref="UpdateOpSelectionHighlight"/> 一起刷，真相仍然只有一份。
        /// </para>
        /// <para>
        /// 刻意**不在失焦时清空**：下拉框弹出时键盘焦点会进到 Popup 里（Popup 不在本窗口的
        /// 视觉树内），一失焦就清会把"正在改这一行"的高亮在展开下拉的瞬间抹掉。
        /// 高亮保留到下一次获得焦点为止 —— 语义变成"当前关注哪一格"，更稳，也省掉一堆
        /// 焦点在 Popup / 下拉框 / 行容器之间来回跳的时序纠葛。
        /// </para>
        /// </summary>
        private void OpActionCombo_GotFocus(object sender, RoutedEventArgs e)
        {
            FocusOpSectorFromCombo(sender);
        }

        /// <summary>
        /// 下拉框展开时再确认一次。有些路径下焦点先落进 Popup，<c>GotFocus</c> 的时序不可靠，
        /// 补这一个钩子保证"展开哪个下拉框，菜单就亮哪一格"。
        /// </summary>
        private void OpActionCombo_DropDownOpened(object sender, EventArgs e)
        {
            FocusOpSectorFromCombo(sender);
        }

        /// <summary>从下拉框的 Tag 取出位置索引（0..7）并切换选中扇区。</summary>
        private void FocusOpSectorFromCombo(object sender)
        {
            var combo = sender as ComboBox;
            if (combo == null || combo.Tag == null) return;

            int index;
            if (!int.TryParse(combo.Tag.ToString(), out index)) return;
            FocusOpSector(index);
        }

        /// <summary>
        /// 把"当前选中"切到第 <paramref name="index"/> 个扇区（焦点进入设置项时走这里）。
        /// 与"点菜单扇区""改下拉框"共用同一份真相 <see cref="_selectedSectorIndex"/>，
        /// 所以三者不会互相打架。
        /// </summary>
        private void FocusOpSector(int index)
        {
            try
            {
                if (index < 0 || index >= 8) return;
                if (_selectedSectorIndex == index) return;   // 已经亮着：不重复重绘

                _selectedSectorIndex = index;
                _opPreviewMenu?.HighlightItem((MenuItemPosition)index);
                UpdateOpSelectionHighlight();

                // 留个日志点：用户手动操作时能从这里读到"焦点确实触发了切换"
                App.Log("OpAction focus -> Position" + (index + 1));
            }
            catch (Exception ex)
            {
                App.Log("FocusOpSector error: " + ex.Message);
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

        /// <summary>
        /// 重绘「效果预览」（防重入包装）。
        /// <para>
        /// 为什么要防重入：首次显示衍生窗口时 <c>Show()</c> 会同步触发一轮布局，
        /// 画布拿到真实尺寸后抛 <c>SizeChanged</c>，而那个挂钩又调回本方法 ——
        /// 不加锁就会在"显示"过程中嵌一层重绘，内层把菜单对象换成了新的、
        /// 外层的引用随即失效。加个标志位让内层直接返回，行为确定。
        /// </para>
        /// <para>
        /// 量级影响可忽略：画布是**固定尺寸**（300×300），首帧的降级值就是最终值，
        /// 内层那次重绘本就画不出任何不同。
        /// </para>
        /// </summary>
        private void UpdatePreview()
        {
            if (_inPreviewUpdate) return;
            _inPreviewUpdate = true;
            try
            {
                UpdatePreviewCore();
            }
            finally
            {
                _inPreviewUpdate = false;
            }
        }

        /// <summary>
        /// 重绘「效果预览」。预览画在**衍生窗口**（<see cref="MenuPreviewWindow"/>）的画布上，
        /// 本窗口内已无预览控件。
        /// <para>
        /// 可见性由 <see cref="ShouldShowPreview"/> 决定：只有停在「菜单样式」页、
        /// 且设置窗口真正可见时才显示；切走只是 <c>Hide()</c>（不销毁，切回来即时恢复）。
        /// </para>
        /// </summary>
        private void UpdatePreviewCore()
        {
            try
            {
                if (_config == null) return;

                // 页不对 / 窗口不可见 → 连窗口都不创建（懒创建），只把已存在的收起来
                if (!ShouldShowPreview())
                {
                    if (_previewWindow != null && _previewWindow.IsVisible) _previewWindow.Hide();

                    // ⚠️ 这里不能直接 return：另外两个页面里，「操作配置」页中央也有一张
                    // 菜单要画（它跟这一页的预览是两套独立实例）。早退会让操作配置页
                    // 永远停在"上一次画的样子"甚至空白。
                    UpdateOpPreview();
                    return;
                }

                var host = EnsurePreviewWindow();
                if (host == null) return;

                var canvas = host.PreviewCanvas;
                if (canvas == null) return;

                canvas.Children.Clear();

                // 获取画布尺寸。
                // 首帧布局还没跑完时 ActualWidth/ActualHeight 为 0，退回 XAML 里写死的
                // 尺寸 —— 画布固定 300×300，两者只差"是否已过一轮布局"，值相同。
                double w = canvas.ActualWidth;
                double h = canvas.ActualHeight;
                if (w <= 0) w = double.IsNaN(canvas.Width) ? 300 : canvas.Width;
                if (h <= 0) h = double.IsNaN(canvas.Height) ? 300 : canvas.Height;
                var center = new Point(w / 2, h / 2);

                // 创建并初始化菜单，用于预览。
                // 预览与运行时用**同一套 DIP 基准**（都用配置里的原始值）：
                // 预览画的就是配置值本身，所以“预览里看到多大，弹出来就多大”。
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
                canvas.Children.Add(_previewMenu);
                // 不在样式预览中绘制动作标签，以便只展示菜单样式
                UpdatePreviewFitLabel(fit);

                // 显示 + 贴合。
                // 顺序是"先画完、再 Show、最后 Reposition"：
                //   · ShowActivated=False → 显示不抢焦点，用户正在输入的数字框不会失焦；
                //   · 首次 Show 之后才拿得到真实高度（SizeToContent），所以定位放最后。
                if (!host.IsVisible)
                {
                    host.Show();
                }
                host.Reposition();

                // 更新 操作配置 页 的预览（该页仍然显示动作标签）
                UpdateOpPreview();
            }
            catch (Exception ex)
            {
                App.Log("UpdatePreview error: " + ex.Message);
            }
        }

        /// <summary>
        /// 懒创建预览衍生窗口。
        /// <para>
        /// ⚠️ Owner 必须在本窗口 <c>Show()</c> **之前**设好 —— 显示后再设会被 WPF 拒绝
        /// （InvalidOperationException），所以创建完立刻 AttachTo(this)。
        /// </para>
        /// </summary>
        private MenuPreviewWindow EnsurePreviewWindow()
        {
            if (_previewWindow != null) return _previewWindow;

            try
            {
                _previewWindow = new MenuPreviewWindow();
                _previewWindow.AttachTo(this);

                // 首帧渲染完成 / 尺寸落定后补一次定位：窗口高度是运行时按主窗口**可见高**
                // 写上去的（见 Reposition），首次调用时它可能还没落地，需要等布局完再补一次。
                _previewWindow.ContentRendered += (s, e) => _previewWindow?.Reposition();
                _previewWindow.SizeChanged += (s, e) => _previewWindow?.Reposition();

                // 画布尺寸落定后补一次重绘：首次绘制时 ActualWidth 还是 0，
                // 走的是"写死尺寸"的降级分支。画布固定尺寸，这里只会触发一次。
                _previewWindow.PreviewCanvas.SizeChanged += (s, e) => UpdatePreview();

                // 用户在预览窗里切「白色 / 黑色背景」→ 重绘一次菜单。
                // 底色本身不参与菜单绘制（它只在画布下层），重绘是为了让"以后若有人把底色
                // 引到渲染逻辑里"也自动生效 —— 重绘幂等（清空 → 重建菜单对象），没有代价。
                _previewWindow.PreviewBackgroundChanged += (s, e) => UpdatePreview();
            }
            catch (Exception ex)
            {
                App.Log("EnsurePreviewWindow error: " + ex.Message);
                _previewWindow = null;
            }

            return _previewWindow;
        }

        /// <summary>
        /// 预览窗口此刻是否应当可见。四个条件缺一不可：
        /// 非启动抑制期、设置窗口已加载、设置窗口未最小化、当前停在「菜单样式」页。
        /// <para>
        /// 「已加载」这条是为了避开构造期：<c>LoadConfig()</c> 一路会调到 UpdatePreview，
        /// 那时窗口还没显示，提前弹出预览就是启动瞬间闪一个窗口。
        /// </para>
        /// </summary>
        private bool ShouldShowPreview()
        {
            try
            {
                if (_suppressOnStartup) return false;
                if (!IsLoaded) return false;
                if (WindowState == WindowState.Minimized) return false;
                if (MainTabControl == null) return false;
                return MainTabControl.SelectedIndex == MenuStylePageIndex;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 按画布可用空间把预览菜单整体缩放"装进去"，返回实际使用的比例。
        /// 装得下时返回 1.0（不做任何缩放），避免小半径时被无谓放大。
        /// 留 8px 边距，防止描边正好贴住画布边缘被切掉半个像素。
        /// </summary>
        private double ApplyPreviewFit(double menuWidth, double menuHeight, double canvasW, double canvasH)
        {
            if (_previewMenu == null) return 1.0;
            return ApplyMenuFit(_previewMenu, menuWidth, menuHeight, canvasW, canvasH);
        }

        /// <summary>
        /// 按可用空间把菜单整体缩放"装进去"，返回实际使用的比例，
        /// 并把缩放挂到菜单的 <c>RenderTransform</c> 上。
        /// <para>
        /// 默认只缩小不放大（<paramref name="maxFit"/> = 1.0、<paramref name="allowUpscale"/> = false），
        /// 避免小半径时被无谓放大 —— 「菜单样式」页要能直观感到"我把半径改小了"。
        /// <b>唯一的例外</b>是「操作配置」页：那一页的菜单是**示意图**（当引线的锚点环用），
        /// 尺寸不是要展示的信息，所以按可用空间尽量画大（传 <paramref name="allowUpscale"/> = true、
        /// <paramref name="maxFit"/> = 0 表示不设人为上限，只受画布与锚点环约束）。
        /// </para>
        /// <para>
        /// 留 <see cref="MenuFitPad"/> px 边距，防止描边正好贴住画布边缘被切掉半个像素。
        /// </para>
        /// <para>
        /// 抽成静态方法是因为有两个调用方：「菜单样式」页的衍生预览窗口、
        /// 「操作配置」页中间那张菜单 —— 缩放**规则**一致，只是操作配置页多两条约束
        /// （是否允许放大、锚点环不许越进行内边缘，见 <see cref="ComputeOpFitCap"/>）。
        /// </para>
        /// </summary>
        private static double ApplyMenuFit(RadialMenu menu, double menuWidth, double menuHeight,
                                           double canvasW, double canvasH,
                                           double maxFit = 1.0, bool allowUpscale = false)
        {
            try
            {
                if (menu == null) return 1.0;
                if (menuWidth <= 0 || menuHeight <= 0) return 1.0;

                double pad = MenuFitPad;
                double availW = Math.Max(1, canvasW - pad * 2);
                double availH = Math.Max(1, canvasH - pad * 2);

                double fit = Math.Min(availW / menuWidth, availH / menuHeight);

                // 上限：默认 1.0（只缩小不放大）。
                // 操作配置页不传人为上限（maxFit = 0），只受画布本身的可用尺寸与本方法外的
                // "锚点环"约束（ComputeOpFitCap）限制。
                if (maxFit > 0 && fit > maxFit) fit = maxFit;
                if (!allowUpscale && fit >= 1.0) fit = 1.0;

                // 极端值保护，避免算出 0 或 NaN 让整个图形消失
                if (double.IsNaN(fit) || double.IsInfinity(fit) || fit <= 0.01) fit = 0.01;

                // ⚠️ 判据是"比例不等于 1"而不是"比例小于 1"：
                // 允许放大时（操作配置页）fit 会 >1，写成 `if (fit < 1.0)` 会走 else 分支
                // 把 RenderTransform 清成 null —— 于是**报告里的比例是 2.36、画出来的却是原尺寸**
                // （锚点按 2.36 摆、图形按 1.0 画，点就浮在图形外面一大圈）。
                // 这个 bug 改这一版时踩过：自检全 PASS，因为 fit 字段是对的，只有截图能看出来。
                if (Math.Abs(fit - 1.0) > 1e-9)
                {
                    // 以中心为原点做 RenderTransform 缩放：
                    // 位置仍按原始尺寸居中算，缩放围绕中心，所以居中性不受影响。
                    var tg = new TransformGroup();
                    tg.Children.Add(new ScaleTransform(fit, fit));
                    menu.RenderTransform = tg;
                    menu.RenderTransformOrigin = new Point(0.5, 0.5);
                }
                else
                {
                    menu.RenderTransform = null;
                }

                return fit;
            }
            catch (Exception ex)
            {
                App.Log("ApplyMenuFit error: " + ex.Message);
                return 1.0;
            }
        }

        /// <summary>
        /// 「操作配置」页那张菜单元素的绘制尺寸（DIP）。
        /// <para>
        /// 各样式的 <c>Width</c> 未必等于 <c>VisualRadius×2</c>（蜘蛛网/八卦都带绘制外扩），
        /// 所以才要走元素自报的尺寸，不能拿某个造型的配置半径去兜底 ——
        /// 那样另外三种样式的尺寸与位置会整体偏掉。
        /// </para>
        /// </summary>
        private Size GetOpMenuSize()
        {
            double vr = _opPreviewMenu == null ? 0 : _opPreviewMenu.VisualRadius;

            double w = _opPreviewMenu == null ? 0 : _opPreviewMenu.Width;
            double h = _opPreviewMenu == null ? 0 : _opPreviewMenu.Height;
            if (double.IsNaN(w) || w <= 0) w = vr * 2;
            if (double.IsNaN(h) || h <= 0) h = vr * 2;

            double fallback = _config == null ? 0 : _config.BasicRadialMenuConfig.OuterRadius * 2;
            if (double.IsNaN(w) || w <= 0) w = fallback;
            if (double.IsNaN(h) || h <= 0) h = fallback;

            return new Size(w, h);
        }

        /// <summary>
        /// 「操作配置」页菜单显示比例的**上限**：由"锚点环不许越进行的内边缘"反解。
        /// <para>
        /// 约束式：<c>drawnR × fit + OpAnchorGap + 选中态点半径 ≤ rowInnerDist</c>。
        /// 三项都是锚点环本身的占位（圆点选中时会放大，按放大后的半径算）。
        /// 2026-09-22 第三轮去掉了式中的 <c>− OpLeadReserve</c>（那 8px 原来是留给
        /// "水平引出段"的）—— 引出段整个没了、竖直段落在**行的内边缘**上，不再需要那段余量。
        /// 真正起作用的上限通常来自"统一锚点环"那条（见 <see cref="OpAnchorRingVisibleR"/>），
        /// 它比本条更紧（八卦 1.135 vs 1.167）。
        /// </para>
        /// <para>
        /// ⚠️ 这条约束以前写成一个**固定系数 0.72**。固定系数的问题是"无差别地削一刀"：
        /// 圆环的可见半径只有 50，本来宽裕得多，也被一路缩到 36 —— 用户反馈"菜单有点太小了"。
        /// 改成按实测反解后，约束允许多大就画多大；配合这一页"允许放大"的策略
        /// （见 <see cref="ApplyMenuFit"/>），菜单会把中间那列的空间用满，
        /// 只有极窄的窗口里才会真的被压到 1.0 以下。
        /// </para>
        /// <para>
        /// 返回值**不夹 1.0** —— 这一页的菜单是示意图、不承担"展示真实尺寸"的职责
        /// （那是「菜单样式」页衍生预览窗口的事），所以解出 2.0 就按 2.0 走，
        /// 实际画多大由画布可用尺寸与这个解共同决定（取小者）。
        /// 下界 0.2 只是防呆：真触发说明左右两列已经挤到没有引线空间，别让菜单缩到看不见。
        /// </para>
        /// </summary>
        private static double ComputeOpFitCap(double rowInnerDist, double drawnR)
        {
            if (double.IsNaN(rowInnerDist) || rowInnerDist <= 0) return double.PositiveInfinity;
            if (double.IsNaN(drawnR) || drawnR <= 0) return double.PositiveInfinity;

            double allowed = rowInnerDist - OpAnchorGap - OpDotSelectedRadius;
            if (allowed <= 0) return 0.2;

            double cap = allowed / drawnR;
            if (cap < 0.2) cap = 0.2;
            return cap;
        }

        /// <summary>
        /// 让「操作配置」页的卡片**撑满整页高度**。
        /// <para>
        /// 这一页只有中间一组三栏，卡片若按内容高度排就只在页面顶部占一条、下面空一大片
        /// （用户 2026-09-22 反馈"面板在整个设置窗口吊着不美观"）。
        /// </para>
        /// <para>
        /// 取 <c>ViewportHeight</c> 而不是控件高度：它已扣掉 ScrollViewer 的 Padding，
        /// 卡片直接顶到这个高度即"填满可见区"。
        /// 用 <c>MinHeight</c> 而不是 <c>Height</c>：内容将来变高（字体放大、后续加行）
        /// 仍能撑开并滚动。因为内容高度 ≡ 视口高度，不会出现"滚动条出现→视口变小→
        /// 卡片变矮→滚动条消失"的抖动。
        /// </para>
        /// </summary>
        private void SyncOpPageCardHeight()
        {
            try
            {
                if (OpPageScroll == null || OpPageCard == null) return;

                double vh = OpPageScroll.ViewportHeight;
                if (double.IsNaN(vh) || vh <= 0) return;

                if (Math.Abs(OpPageCard.MinHeight - vh) > 0.5) OpPageCard.MinHeight = vh;
            }
            catch (Exception ex)
            {
                App.Log("SyncOpPageCardHeight error: " + ex.Message);
            }
        }

        /// <summary>
        /// 在预览下方标注当前显示比例。100% 时清空，不显示多余文字。
        /// 说明文字在衍生窗口里（<see cref="MenuPreviewWindow.PreviewFitLabel"/>）。
        /// </summary>
        private void UpdatePreviewFitLabel(double fit)
        {
            try
            {
                if (_previewWindow == null) return;

                var label = _previewWindow.PreviewFitLabel;
                if (label == null) return;

                if (fit >= 0.999)
                {
                    label.Text = string.Empty;
                }
                else
                {
                    label.Text = string.Format(
                        "预览按 {0}% 缩放显示，以便完整展示；实际菜单尺寸不受影响。",
                        (int)Math.Round(fit * 100));
                }
            }
            catch (Exception ex)
            {
                App.Log("UpdatePreviewFitLabel error: " + ex.Message);
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

        /// <summary>
        /// 重绘「操作配置」页的菜单本体（三栏里的中栏）。
        /// <para>
        /// 与「菜单样式」页的预览共用同一套 DIP 基准（都是直接画配置值），
        /// 但**是两个独立的菜单实例**：那一页跑循环高亮动画、这一页高亮的是用户选中的扇区，
        /// 共用一个实例会互相抢高亮。同理，那一页的自动适配缩放也只作用于它自己的实例，
        /// 这里要自己算一份 <see cref="_opFit"/>。
        /// </para>
        /// <para>
        /// 引线不在这里画 —— 它依赖布局跑完后的真实坐标，由
        /// <see cref="ScheduleOpConnectorRefresh"/> 排队、<see cref="UpdateOpConnectors"/> 落地。
        /// </para>
        /// </summary>
        private void UpdateOpPreview()
        {
            try
            {
                if (OpPreviewCanvas == null) return;

                // 卡片顶满整页（"面板吊在窗口里"的修复）。放这里是因为它同时覆盖
                // "切到这一页"和"画布尺寸变了"两条路径，成本也只有一次属性比较。
                SyncOpPageCardHeight();

                // 不在这一页（或窗口最小化/启动抑制期）→ 清掉画布。
                // 判据不能只看 SelectedIndex：启动抑制期页面也不该画，
                // 否则会在窗口还没显示时先建一遍菜单对象。
                if (!ShouldShowOpPreview())
                {
                    if (OpPreviewCanvas.Children.Count > 0) OpPreviewCanvas.Children.Clear();
                    if (OpConnectorLayer != null) OpConnectorLayer.Children.Clear();
                    for (int k = 0; k < 8; k++) { _opLines[k] = null; _opDots[k] = null; }
                    _opPreviewMenu = null;
                    return;
                }

                if (_config == null) return;

                OpPreviewCanvas.Children.Clear();

                double w = OpPreviewCanvas.ActualWidth; if (w <= 0) w = 252;
                double h = OpPreviewCanvas.ActualHeight; if (h <= 0) h = 252;
                var center = new Point(w / 2, h / 2);

                // 预览基准同「菜单样式」页（直接用配置里的 DIP）。
                _opPreviewMenu = _menuFactory.CreateMenu(_config.MenuStyle, _config);
                _opPreviewMenu.Initialize(_config, center);

                // 尺寸取菜单自报的值（GetOpMenuSize 里有兜底）：各样式绘制留白不同
                //（蜘蛛网外扩 1.1、八卦 1.2…），用 BasicRadial 的半径兜底会让
                // 另外三种样式的尺寸与位置整体偏掉。
                var menuSize = GetOpMenuSize();
                double menuWidth = menuSize.Width, menuHeight = menuSize.Height;

                // 只影响显示，绝不回写配置。
                // 这一页的菜单是**示意图**：尺寸不是要展示的信息（真实尺寸看「菜单样式」页的
                // 衍生预览窗口），职责是把中间那列的空间用满、当好引线的锚点环 ——
                // 所以传 allowUpscale: true、maxFit: 0（不设人为上限，只受画布可用尺寸限制）。
                // ⚠️ 另一条约束"锚点环不许越进行的内边缘"必须**实测**才行 —— 放到
                // UpdateOpConnectors 里按真实行位置反解（见 ComputeOpFitCap），
                // 那一刻才拿得到 8 行的 ActualWidth，这里读到的还是 0。
                _opFit = ApplyMenuFit(_opPreviewMenu, menuWidth, menuHeight, w, h,
                                      maxFit: 0, allowUpscale: true);

                double left = (w - menuWidth) / 2;
                double top = (h - menuHeight) / 2;
                Canvas.SetLeft(_opPreviewMenu, left);
                Canvas.SetTop(_opPreviewMenu, top);
                OpPreviewCanvas.Children.Add(_opPreviewMenu);

                // 重建菜单后高亮会丢（新对象），按当前选中重新点亮。
                _opPreviewMenu.HighlightItem((MenuItemPosition)_selectedSectorIndex);

                UpdateOpActionCombos();
                UpdateOpSelectionHighlight();
                ScheduleOpConnectorRefresh();
            }
            catch (Exception ex)
            {
                App.Log("UpdateOpPreview error: " + ex.Message);
            }
        }

        /// <summary>
        /// 引线重算排队（同一轮布局里只排一次）。
        /// <para>
        /// 必须延后到布局之后：切页那一刻，8 个行与画布的 <c>ActualWidth</c> 都还是 0，
        /// 立刻算会把引线全画到原点去。
        /// </para>
        /// <para>
        /// ⚠️ 用 <see cref="DispatcherPriority.Background"/> 而不是 <c>Loaded</c>：
        /// WPF 的布局/渲染跑在 <c>Render</c> 优先级上，而 <c>Loaded</c> 比它**低**
        /// —— 实测排在 Loaded 上的回调会在布局之前执行，读到的尺寸仍是 0（静默 return，
        /// 界面上就是"引线根本没画"）。Background 低于 Render，一定在布局之后。
        /// 同时它仍高于 <c>ContextIdle</c>，能保证被消息泵处理到。
        /// </para>
        /// </summary>
        private void ScheduleOpConnectorRefresh()
        {
            if (_opConnectorRefreshQueued) return;
            _opConnectorRefreshQueued = true;

            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _opConnectorRefreshQueued = false;
                    try { UpdateOpConnectors(); }
                    catch (Exception ex) { App.Log("deferred UpdateOpConnectors error: " + ex.Message); }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                _opConnectorRefreshQueued = false;
                App.Log("ScheduleOpConnectorRefresh error: " + ex.Message);
            }
        }

        /// <summary>
        /// 按当前布局重算三栏引线：下拉框**侧边中点** → 水平引出 → 竖直走廊 → 水平接入 → 锚点。
        /// <para>
        /// 几何契约（三栏各自的位置见 SettingsWindow.xaml 的 OpLayoutGrid）：
        /// ① **终点钉在下拉框朝内一侧、侧边的正中间**（用户 2026-09-22 明确要求）——
        ///    起点 x = 框边 ∓ <see cref="OpEdgeGap"/>、起点 y = 框侧边中点；
        /// ② 起点与锚点同高 → **一段水平直线**；否则 → **横 + 竖 + 横**三段
        ///    （竖段落在两列各一条的**走廊**上，见 <see cref="OpCorridorLead"/>）。
        /// 全程只有水平 / 垂直两种方向（正交肘线，见 <see cref="BuildOpConnectorGeometry"/>）。
        /// </para>
        /// <para>
        /// 演进史（四轮，别把前几轮的方案拿回来）：
        /// ① "水平引出 + 一条斜线直达" —— 斜段长短倾角各行不同、正上正下还会甩出上百像素的长斜线，
        /// 整体像往外发散的一簇线；
        /// ② 三段正交、走廊紧贴图形 —— 方向齐了，但走廊太靠里，竖段紧擦图案；
        /// ③ "起点沿行边缘滑动 + 两段（竖→横）"，走廊取消 —— 段数从 24 降到 12，
        /// 但起点会滑到**下拉框的上下角**上（锚点离行中心 40px、行程只有 21px），
        /// 看着不像从这一行引出来的；
        /// ④ 本版：起点回到**下拉框侧边中点**、走廊回归 —— 段数回升到 24，换"每条线都
        /// 明明白白从自己那一行的正中间出来"。折点始终是**直角**（用户要求不做圆角）。
        /// </para>
        /// <para>
        /// 锚点落在图形**可见外缘之外**：这样从侧面过来的线全程在空白区走，
        /// 不会横穿相邻扇区（放进图形内部就会，看着像"连错了"）。
        /// 锚点环的半径 = <see cref="RadialMenu.DrawnRadius"/> × 显示缩放 + <see cref="OpAnchorGap"/>
        /// —— 用**可见外缘**而不是 VisualRadius（后者含样式留白，锚点会浮起来）。
        /// </para>
        /// <para>
        /// 显示缩放也在这里定：菜单按真实尺寸画，只有"锚点环将要越进行的内边缘"时才收小，
        /// 上限由实测的行内边缘反解（<see cref="ComputeOpFitCap"/>）——
        /// 因为 8 行的真实位置只有布局跑完才拿得到，这比 <see cref="UpdateOpPreview"/> 早不了。
        /// </para>
        /// <para>
        /// ⚠️ 行内不再有位置文字（2026-09-22 按用户要求去掉），所以"这条线属于哪一行"
        /// 只能从这条线的**起点位置**读出来 —— 起点必须是下拉框侧边的**中点**，
        /// 一旦被挪到框角或框外，"哪一行是哪个位置"就无从读起。
        /// 自检里的方向角断言是这一页唯一的正确性保障（见 CheckOperationConfigLayout）。
        /// </para>
        /// </summary>
        private void UpdateOpConnectors()
        {
            try
            {
                if (OpConnectorLayer == null || OpLayoutGrid == null) return;

                OpConnectorLayer.Children.Clear();
                for (int k = 0; k < 8; k++) { _opLines[k] = null; _opDots[k] = null; }

                if (_opPreviewMenu == null || !ShouldShowOpPreview()) return;

                // ⚠️ 尺寸判据用 OpLayoutGrid，不用引线层自己的 ActualWidth。
                // 两者同尺寸（引线层铺满三栏），但 OpLayoutGrid 是布局的直接结果、
                // 在 SizeChanged 回调里也**一定**已经是新值；而 Canvas 作为"零期望尺寸"
                // 的容器，Arrange 可能晚于 Grid 的 SizeChanged 一步，那时读到的还是 0。
                if (OpLayoutGrid.ActualWidth <= 0 || OpLayoutGrid.ActualHeight <= 0) return;
                if (OpPreviewCanvas == null || OpPreviewCanvas.ActualWidth <= 0) return;

                double visualR = _opPreviewMenu.VisualRadius;
                if (double.IsNaN(visualR) || visualR <= 0) return;

                // 菜单中心（OpLayoutGrid 坐标系）。
                // ⚠️ 走 TranslatePoint，不要自己按"画布中心"算：各样式的 Width 未必等于
                // VisualRadius×2（蜘蛛网/八卦都带绘制外扩），自己算会在这些样式上整体偏掉。
                Point center;
                try
                {
                    center = _opPreviewMenu.TranslatePoint(_opPreviewMenu.VisualCenter, OpLayoutGrid);
                }
                catch
                {
                    return;   // 元素还没进可视树
                }

                // 锚点环贴着**看得见的边**放：用 DrawnRadius 而不是 VisualRadius ——
                // 后者含各样式为定位/标签预留的外扩（蜘蛛网 1.1、八卦 1.2、八角星 1.02），
                // 照着它放 8 颗点会浮在留白里，而且各样式浮的距离还不一样（八卦浮得最凶），
                // 两列一对比就显得不齐。DrawnRadius 是图形真正画到的半径，锚点因此贴边。
                double drawnR = _opPreviewMenu.DrawnRadius;
                if (double.IsNaN(drawnR) || drawnR <= 0) drawnR = visualR;

                // ---- 第一遍：量出每行的起点、朝内边缘离菜单中心多远（先不画）----
                // 分两遍是因为**引出段长度要整列统一**（见下）—— 得先知道全列 4 格的锚点在哪；
                // 顺带量出 rowInnerDist：锚点环的硬天花板，显示缩放由它反解。
                var rowAx = new double[8];      // 起点 x：下拉框朝内边缘 ∓ OpEdgeGap
                var rowAy = new double[8];      // 起点 y：下拉框侧边的**中点**（本轮起不再滑动）
                var dotX = new double[8];
                var dotY = new double[8];
                var live = new bool[8];
                var corridorX = new double[2];  // [0] 左列、[1] 右列（见 OpCorridorLead）
                double rowInnerDist = double.NaN;

                for (int i = 0; i < 8; i++)
                {
                    var row = _opRows[i];
                    if (row == null || row.ActualWidth <= 0) continue;

                    Rect rr;
                    try
                    {
                        rr = row.TransformToAncestor(OpLayoutGrid)
                                .TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
                    }
                    catch { continue; }

                    // 下拉框（真正能点的那个控件）在同一坐标系里的矩形。
                    // ⚠️ 横向必须量它、不能量行 Border：行 Border 带内边距（现 4px + 1px 描边），
                    //    下拉框比行窄一大截 —— 早先按行算，线头就悬在行的留白里、离下拉框 10px，
                    //    看着"没落到下拉框上"（2026-09-22 用户反馈）。
                    //    现在分工明确：**行 Border 只管纵向（行中心 / 起点行程），横向以下拉框为准。**
                    var box = _opCombos[i];
                    if (box == null || box.ActualWidth <= 0) continue;
                    Rect boxR;
                    try
                    {
                        boxR = box.TransformToAncestor(OpLayoutGrid)
                                  .TransformBounds(new Rect(0, 0, box.ActualWidth, box.ActualHeight));
                    }
                    catch { continue; }

                    // 起点 x：下拉框的朝内边缘再让出 OpEdgeGap（2px）。
                    rowAx[i] = i >= 4 ? boxR.Right + OpEdgeGap : boxR.Left - OpEdgeGap;

                    // 起点 y：**下拉框侧边的中点** —— 2026-09-22 第四轮按用户要求钉死。
                    // ⚠️ 这一条与第三轮的"起点沿行的内边缘滑动到锚点高度"是**互斥**的：
                    //    滑动换来了更少的段数（总段数 24 → 12），但起点会滑到框的上下角上，
                    //    看着不像从这一行引出来的。用户标注的红线明确要求"终点落在下拉框
                    //    侧边中点"，所以滑动整个取消 —— 段数随之回升，这是自觉付出的代价。
                    rowAy[i] = boxR.Top + boxR.Height / 2.0;

                    // 这一行朝内边缘离菜单中心有多远 —— 锚点环的硬天花板。
                    // 正左 / 正右那两个锚点就顶在这条线上，越过去点就落进行里、引线退化成短线。
                    // 两列各 4 行、同列的朝内边缘本就落在同一个 x 上，取最小只是防呆（等值）。
                    double d = i >= 4 ? (center.X - rr.Right) : (rr.Left - center.X);
                    if (double.IsNaN(rowInnerDist) || d < rowInnerDist) rowInnerDist = d;

                    live[i] = true;
                }

                // ---- 显示缩放：由"锚点环不许越进行内边缘"反解（不是固定系数）----
                // ⚠️ 顺序要紧：锚点位置依赖 _opFit，所以必须先定缩放、再算锚点。
                // 缩放只改菜单的 RenderTransform（以中心为原点），不触发重新布局，
                // 菜单中心与图形方向都不变 —— 同一个回调里改完接着算锚点是自洽的。
                // 详见 ComputeOpFitCap 的注释（含"为什么不再用固定系数"）。
                //
                // 判据用"值不同就重算"而不是"只准变小"：这样每一次都会收敛到
                // min(画布可用尺寸, 锚点环约束) —— 上一次遗留的偏小值也能自己修回来。
                if (live[0] || live[4])
                {
                    // 上限取**两者较小**：
                    // ① 不许越进行的内边缘 —— ComputeOpFitCap 按实测反解；
                    // ② **统一锚点环半径** —— 让四种样式的引线几何完全一致。
                    //    这条比①更紧（八卦 1.135 vs 上限 1.167），是"八条线能不能对齐"的
                    //    决定性约束，见 OpAnchorRingVisibleR。
                    double capFit = Math.Min(ComputeOpFitCap(rowInnerDist, drawnR),
                                             OpAnchorRingVisibleR / drawnR);
                    if (Math.Abs(capFit - _opFit) > 0.005)
                    {
                        var menuSize = GetOpMenuSize();
                        _opFit = ApplyMenuFit(_opPreviewMenu, menuSize.Width, menuSize.Height,
                                              OpPreviewCanvas.ActualWidth, OpPreviewCanvas.ActualHeight,
                                              maxFit: capFit, allowUpscale: true);
                    }
                }

                // 锚点环：可见外缘 × 显示缩放 + 固定空隙。
                // ⚠️ 一定要乘 _opFit：上面可能刚把菜单缩小，看得见的边也跟着缩。
                double anchorR = drawnR * _opFit + OpAnchorGap;

                for (int i = 0; i < 8; i++)
                {
                    if (!live[i]) continue;

                    // 第 i 个扇区的**中心方向**（Position1 = 正上方，顺时针每 45° 一格），
                    // 与 RadialMenu.FirstSectorAxisDeg / SectorDeg 一致。
                    double rad = (-90.0 + i * 45.0) * Math.PI / 180.0;
                    dotX[i] = center.X + Math.Cos(rad) * anchorR;
                    dotY[i] = center.Y + Math.Sin(rad) * anchorR;
                }

                // ---- 竖直走廊：整列共用一条竖线 ----
                // 起点被钉在下拉框侧边中点之后，"起点高度"就不再可调；锚点 y 与它不等时
                // 必须找一条竖直段把差值走完。这条竖线放在**图形与下拉框之间、锚点环再往外
                // OpCorridorLead** 的位置 —— 贴着下拉框边竖走会像"给框描边"，贴着锚点环
                // 又会和锚点/其它线挤在一起。由锚点环半径推导（而不是写死像素），窗口变窄时
                // 锚点环被收小、走廊自动跟着往里。
                double corridorDist = anchorR + OpCorridorLead;
                corridorX[0] = center.X - corridorDist;   // 左列
                corridorX[1] = center.X + corridorDist;   // 右列

                // ---- 画线 + 锚点 ----
                // 形态固定为三段「水平引出 → 竖直走廊 → 水平接入」（锚点与起点同高时退化成一段）。
                // 这就是用户 2026-09-22 标注的红线形状。见 BuildOpConnectorGeometry。
                for (int i = 0; i < 8; i++)
                {
                    if (!live[i]) continue;

                    double ax = rowAx[i], ay = rowAy[i], cx = dotX[i], cy = dotY[i];
                    double midX = corridorX[i >= 4 ? 0 : 1];

                    var line = new System.Windows.Shapes.Path
                    {
                        Data = BuildOpConnectorGeometry(ax, ay, midX, cx, cy),
                        IsHitTestVisible = false,
                        // 折点是直角：join 用 Miter。用 Round 会把直角磨成一截小圆弧，
                        // 那是"变相的圆角转弯"，与本次要求相悖（线宽只有 1.3，Miter 不会爆尖）。
                        StrokeLineJoin = PenLineJoin.Miter,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    OpConnectorLayer.Children.Add(line);
                    _opLines[i] = line;

                    // 扇区端一个实心小圆点，明确"这条线指向这里"。
                    // 尺寸/颜色统一由 PaintOpConnector 给（它会按选中态放大，所以这里不写死大小）。
                    _opDotCenters[i] = new Point(cx, cy);
                    var dot = new System.Windows.Shapes.Ellipse { IsHitTestVisible = false };
                    OpConnectorLayer.Children.Add(dot);
                    _opDots[i] = dot;

                    PaintOpConnector(i, i == _selectedSectorIndex);
                }
            }
            catch (Exception ex)
            {
                App.Log("UpdateOpConnectors error: " + ex.Message);
            }
        }

        /// <summary>
        /// 生成一条引线的几何：**下拉框侧边中点 → 水平引出 → 竖直走廊 → 水平接入 → 锚点**。
        /// <para>
        /// 只有两种合法形态：
        /// ① 起点与锚点同高（差 &lt; 0.5px）→ **一段水平直线**（退化形态，最省）；
        /// ② 否则 → **水平 + 竖直 + 水平**三段，两个直角折点，join 用 Miter。
        /// </para>
        /// <para>
        /// 2026-09-22 第四轮（用户标注红线后的定版）。前三版都别拿回来：
        /// ① "水平引出段 + 一条斜线直达锚点" —— 斜段的长度与倾角由"行 y 与锚点 y 之差"决定，
        /// 而行的 y 是等距排布、锚点的 y 是圆周均布反算的，两者天然对不上：8 条斜线长短倾角
        /// 各异，正上 / 正下还会甩出上百像素的长斜线（用户原话"引线的伸展看着又回到之前的版本"）；
        /// ② 第一版三段正交（走廊贴图形、引出段很短）—— 走廊太靠里，竖段紧擦图案；
        /// ③ 第三轮"起点沿行边缘滑动 + 两段（竖→横）"—— 段数最少（24 → 12），但**起点会滑到
        /// 下拉框的上下角上**（锚点离行中心 40px 时，行程只有 21px，起点只能压在框角），
        /// 看着不像从这一行引出来的。用户标了红线要求"终点落在下拉框侧边的中点"，故本轮改为
        /// 起点钉死在侧边中点、差值交给走廊，**总段数回升到 24（8×3）是自觉付出的代价**。
        /// </para>
        /// <para>
        /// ⚠️ 顺序不能反成"竖 → 横 → 竖"：那样水平引出段会消失，竖段贴着下拉框侧边走
        /// （像给框描边），用户红线里明确是先从框的侧边中点**水平**出来。
        /// </para>
        /// <para>
        /// ⚠️ 也**不能**为了省段数把起点从侧边中点滑走 —— 那就退回第三轮了，见上。
        /// </para>
        /// <para>
        /// 自检按"1 段水平直线 或 3 段（横 + 竖 + 横，首末段水平）"断言结构 —— 圆角一旦被加回来
        /// （出现 Bezier / Arc 段）或混进斜段都会直接 FAIL。
        /// </para>
        /// </summary>
        /// <param name="ax">起点 x：下拉框朝内边缘 ∓ <see cref="OpEdgeGap"/>。</param>
        /// <param name="ay">起点 y：下拉框侧边的**中点**。</param>
        /// <param name="midX">竖直走廊的 x（见 <see cref="OpCorridorLead"/>）。</param>
        /// <param name="cx">锚点 x。</param>
        /// <param name="cy">锚点 y。</param>
        private static Geometry BuildOpConnectorGeometry(double ax, double ay, double midX,
                                                         double cx, double cy)
        {
            var fig = new PathFigure
            {
                StartPoint = new Point(ax, ay),
                IsClosed = false,
                IsFilled = false
            };

            // 候选折点按顺序：走廊上的两个折点 → 锚点。
            // 相邻重复点（差 < 0.5）直接丢掉 —— 否则会留出零长段：
            // 屏幕上是个"毛刺"，自检的数段数也会虚高。
            var pts = new List<Point>(3);
            void AddPt(double x, double y)
            {
                if (pts.Count > 0)
                {
                    Point prev = pts[pts.Count - 1];
                    if (Math.Abs(prev.X - x) < 0.5 && Math.Abs(prev.Y - y) < 0.5) return;
                }
                pts.Add(new Point(x, y));
            }

            // ⚠️ 退化门限取 **1.0**（不是 0.5）：布局是把行心**对齐到**锚点环半径 R=99，
            // 而 R 在四种菜单样式下是 99.0~99.4（八卦样式元素外扩 1.2 倍，fit 被画布夹在 93.4）。
            // 用 0.5 的话"行心 99 vs 锚点 99.4"只剩 0.1px 余量，稍有取整就会多出一根
            // 0.4px 的竖直段 —— 屏幕上是一根看不见的毛刺，却会让这条线从"1 段"变成"3 段"
            // （自检的形态断言、用户的"不要转角"都直接挂掉）。
            // 1.0px 以内的台阶肉眼看不出是折线，老实画成一段直线才是正确形态。
            if (Math.Abs(cy - ay) < 1.0)
            {
                // 起点与锚点同高：一条水平直线就到位（**最省的形态**）。
                // 硬摆两个"零长折点"会得到三段共线的直线，画出来一样、
                // 但结构上多两段，不如老实画一段。
                AddPt(cx, cy);
            }
            else
            {
                AddPt(midX, ay);   // 水平引出：从下拉框侧边中点平着走到走廊
                AddPt(midX, cy);   // 竖直走廊：在走廊上把高度差走完
                AddPt(cx, cy);     // 水平接入：在锚点的高度上平推进锚点
            }

            foreach (Point p in pts) fig.Segments.Add(new LineSegment(p, true));

            // 兜底：起点与终点重合时上面一段都加不进来 —— 给一条零长直线，
            // 免得交出一个没有段的 PathFigure（自检会判结构异常）。
            if (fig.Segments.Count == 0)
                fig.Segments.Add(new LineSegment(new Point(cx, cy), true));

            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            geo.Freeze();
            return geo;
        }

        /// <summary>
        /// 三栏布局此刻是否该画（与 <see cref="ShouldShowPreview"/> 同一套判据，
        /// 只差"停在哪一页"）。
        /// </summary>
        private bool ShouldShowOpPreview()
        {
            try
            {
                if (_suppressOnStartup) return false;
                if (!IsLoaded) return false;
                if (WindowState == WindowState.Minimized) return false;
                if (MainTabControl == null) return false;
                return MainTabControl.SelectedIndex == OpConfigPageIndex;
            }
            catch
            {
                return false;
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