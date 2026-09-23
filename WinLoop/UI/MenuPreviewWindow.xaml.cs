using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WinLoop.SystemIntegration;

namespace WinLoop.UI
{
    /// <summary>
    /// 菜单样式的「效果预览」衍生窗口 —— 紧贴设置窗口**可见右缘**、高度与母窗口**可见框等高**，无标题。
    ///
    /// 为什么是独立窗口而不是设置窗口里的一张卡片：
    /// 「菜单样式」页的参数组本身就有 5~8 行，后面再挂一张 280×280 的预览卡，
    /// 整页必然超出一屏高度，用户必须下滑才能看到预览 —— 而调参数时眼睛恰恰
    /// 需要同时盯着预览。把预览移到窗口**外侧**，参数区回到一屏内、预览常驻可见，
    /// 两者互不挤占高度。
    ///
    /// 挂载关系（由 SettingsWindow 驱动，本窗口不主动显示/隐藏）：
    ///   - Owner = 设置窗口 → 保证预览始终浮在设置窗口之上，且母窗口关闭时不会被孤立；
    ///   - ShowActivated=False + Focusable=False → **不抢焦点**，用户手上正在输入的数字框
    ///     不会因为预览刷新而失焦（这是"衍生窗口"与"普通窗口"的关键差别）；
    ///   - ShowInTaskbar=False → 不额外占一个任务栏项，它只是设置窗口的一部分。
    ///
    /// 关于"不抢焦点"的边界：本窗口显示时不激活（<c>ShowActivated=False</c>，WPF 用
    /// <c>WS_EX_NOACTIVATE</c> 创建窗口），所以刷新预览**永远不会**把用户正在输入的数字框
    /// 弄失焦。⚠️ 窗口级 <c>Focusable="False"</c> 已**去掉** —— 底部的底色切换按钮需要能点，
    /// 而窗口整体不可聚焦时子控件的交互在部分场景下不可靠。不抢焦点的保证改由
    /// ShowActivated 单独承担，这一条没有变。
    /// </summary>
    public partial class MenuPreviewWindow : Window
    {
        /// <summary>
        /// 与设置窗口之间的水平间距（DIP）。
        /// <para>
        /// **刻意取 0：紧贴母窗口的可见右缘。** 这个窗口不是独立浮窗，而是母窗口向右的
        /// 延伸面板，留间距立刻会显出"两张卡各自飘着"的观感 —— 12 → 0 是第一次返工。
        /// </para>
        /// <para>
        /// ⚠️ 注意基准是**可见外框**而不是 <c>Left + ActualWidth</c>（见
        /// <see cref="Reposition"/> 的说明）。只把 Gap 改成 0、基准选错，缝依旧在 ——
        /// 第二次返工就是栽在这里。
        /// </para>
        /// </summary>
        private const double Gap = 0.0;

        /// <summary>距屏幕工作区边缘的最小留白（DIP）。</summary>
        private const double EdgePad = 4.0;

        private Window _anchor;

        /// <summary>最近一次定位时用到的缩放系数（本屏 DPI/96），仅用于日志诊断。</summary>
        private double _dpiScale = 1.0;

        /// <summary>
        /// 上次选的预览底色是否为深色（**跨窗口实例**记忆）。
        /// <para>
        /// 预览窗在设置窗口存活期内是懒创建、切页只 <c>Hide()</c>；但设置窗口一关就整体销毁，
        /// 记忆若放在实例字段上，"关掉设置再打开"就会退回白色。用户正拿深色底核对对比度时
        /// 还要再点一次，所以记忆放静态。它**不进配置文件** —— 这是查看辅助，不是菜单样式设置。
        /// </para>
        /// </summary>
        private static bool _sharedDarkBackground;

        /// <summary>当前是否为深色底色。</summary>
        private bool _darkBackground;

        /// <summary>当前是否为深色底色（供自检与日志读取）。</summary>
        public bool IsDarkPreviewBackground { get { return _darkBackground; } }

        /// <summary>
        /// 用户在预览窗里切换底色后触发。
        /// <para>
        /// 菜单自身的绘制并不依赖底色（颜色全部来自用户配置），但设置窗口收到后仍会重绘一次：
        /// 重绘是幂等的（清空 → 重建菜单对象），而且万一以后有"按底色自适应对比度"之类的
        /// 渲染逻辑，这里不必再补钩子。
        /// </para>
        /// </summary>
        public event EventHandler PreviewBackgroundChanged;

        public MenuPreviewWindow()
        {
            InitializeComponent();

            // 按上次选择还原（首次为白色）。不写日志、不发事件：此刻还没人订阅。
            ApplyPreviewBackground(_sharedDarkBackground, false);
        }

        /// <summary>
        /// 切换预览底色（白色 / 黑色）。点已选中那一档时直接返回，不重绘。
        /// </summary>
        public void SetPreviewBackground(bool dark)
        {
            if (_darkBackground == dark) return;
            ApplyPreviewBackground(dark, true);
        }

        private void PreviewBackground_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rb = sender as RadioButton;
                bool dark = rb != null && string.Equals(rb.Tag as string, "Dark", StringComparison.Ordinal);
                SetPreviewBackground(dark);
            }
            catch (Exception ex)
            {
                App.Log("MenuPreviewWindow.PreviewBackground_Click error: " + ex.Message);
            }
        }

        /// <summary>
        /// 落地一次底色切换：换底色块画刷 + 同步两个单选项 + （可选）写日志并通知。
        /// <para>
        /// ⚠️ 赋值 <c>IsChecked</c> 只会触发 <c>Checked</c>，**不会**回头触发 <c>Click</c>，
        /// 所以这里不存在递归。本窗口只订阅 Click，不订阅 Checked。
        /// </para>
        /// </summary>
        private void ApplyPreviewBackground(bool dark, bool notify)
        {
            try
            {
                _darkBackground = dark;
                _sharedDarkBackground = dark;

                if (PreviewBgBorder != null)
                {
                    PreviewBgBorder.Background = GetPreviewBackgroundBrush(dark);
                }

                if (BgLightRadio != null) BgLightRadio.IsChecked = !dark;
                if (BgDarkRadio != null) BgDarkRadio.IsChecked = dark;

                if (notify)
                {
                    App.Log("MenuPreviewWindow 预览底色切换为「" + (dark ? "黑色" : "白色") + "背景」");

                    var h = PreviewBackgroundChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                App.Log("MenuPreviewWindow.ApplyPreviewBackground error: " + ex.Message);
            }
        }

        /// <summary>
        /// 取底色画刷。颜色只在 XAML 里定义一处，这里按 key 取；取不到才退回硬编码值
        /// （自检/设计器下资源字典可能尚未就绪）。
        /// </summary>
        private Brush GetPreviewBackgroundBrush(bool dark)
        {
            object o = TryFindResource(dark ? "PreviewBgDark" : "PreviewBgLight");
            var b = o as Brush;
            return b ?? (dark ? Brushes.Black : Brushes.White);
        }

        /// <summary>
        /// 首帧渲染完成后把最终几何写进日志。
        /// <para>
        /// 这是"衍生窗口到底有没有出现、贴得对不对"的**唯一可查证据** ——
        /// 「没有异常」只能说明没崩，不能说明窗口真的显示在右侧界外。
        /// 定位逻辑若被算歪（例如拿到了没落地的 Width），日志里的
        /// <c>window=</c> 与 <c>anchor=</c> 一对就能立刻看出来。
        /// </para>
        /// </summary>
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            try
            {
                if (_anchor == null) return;
                // 日志里同时给出**可见外框**：贴靠与等高都以它为基准，
                // "缝还在不在""两边上下沿齐不齐"直接拿这两组数一减就知道，
                // 不用再回代码里推、也不用再截图量像素。
                Rect vis;
                string visText = TryGetAnchorVisibleFrameDip(out vis)
                    ? string.Format(CultureInfo.InvariantCulture, "({0:0.#},{1:0.#}) {2:0.#}x{3:0.#}",
                        vis.Left, vis.Top, vis.Width, vis.Height)
                    : "n/a";

                App.Log(string.Format(CultureInfo.InvariantCulture,
                    "MenuPreviewWindow shown: window=({0:0.#},{1:0.#}) {2:0.#}x{3:0.#}; anchor=({4:0.#},{5:0.#}) w={6:0.#} h={7:0.#}; visAnchor={8}; dpiScale={9:0.##}",
                    Left, Top, ActualWidth, ActualHeight,
                    _anchor.Left, _anchor.Top, _anchor.ActualWidth, _anchor.ActualHeight,
                    visText, _dpiScale));
            }
            catch (Exception ex)
            {
                App.Log("MenuPreviewWindow.OnContentRendered log error: " + ex.Message);
            }
        }

        /// <summary>
        /// 绑定锚点窗口（设置窗口）。
        /// <para>
        /// ⚠️ Owner 必须在 <see cref="Window.Show"/> **之前**设置 —— 窗口显示后再改 Owner
        /// 会被 WPF 拒绝（抛 InvalidOperationException）。所以这里只在未显示时设置。
        /// </para>
        /// </summary>
        public void AttachTo(Window anchor)
        {
            try
            {
                _anchor = anchor;
                if (anchor != null && Owner == null && !IsVisible)
                {
                    Owner = anchor;
                }
                Reposition();
            }
            catch (Exception ex)
            {
                App.Log("MenuPreviewWindow.AttachTo error: " + ex.Message);
            }
        }

        /// <summary>
        /// 重新定位到锚点窗口**可见右缘**之外，并把本窗高度对齐到锚点的**可见高度**。
        ///
        /// <para>
        /// ⚠️⚠️ 基准是 DWM 的**可见外框**（<see cref="WindowManagementService.TryGetVisibleFrameBounds"/>），
        /// **不是** <c>Left + ActualWidth</c>。这是踩过两次的地方：
        /// Win10/11 给带标题栏的窗口在左/右/下留了约 8px 的**不可见**拖拽边框，
        /// WPF 报出的 Left/Width 是含它的外层矩形。照着外层矩形贴，屏幕上就永远留着
        /// 一条 8~9px 的黑缝 —— 第一次把 Gap 从 12 改成 0 也没消除，因为基准本身就偏了。
        /// 实测（1920×1080 @100%）：主窗口外层 x∈[18,898]、可见 x∈[26,889]。
        /// </para>
        /// <para>
        /// 高度策略：**本窗高度 = 锚点可见高**，且顶部与锚点可见顶对齐。
        /// 本窗是 <c>WindowStyle="None"</c>（无标题栏、无隐形边框），
        /// 所以它的"可见矩形"就等于它的窗口矩形 —— 等高 + 顶对齐之后，
        /// 两个可见矩形**完全重合**，纵向怎么对齐这个问题就自动消失了。
        /// </para>
        /// <para>
        /// 水平决策顺序：右侧放得下 → 贴可见右缘；右侧不够但左侧够 → 翻到可见左缘；
        /// 两侧都不够 → 贴屏幕边，允许与母窗口轻微重叠。
        /// </para>
        /// <para>
        /// ⚠️ 首次布局前 <see cref="FrameworkElement.ActualWidth"/> 为 0，
        /// 拿不到有效宽度就直接返回；等 ContentRendered / SizeChanged 回调里再落位。
        /// </para>
        /// </summary>
        public void Reposition()
        {
            try
            {
                if (_anchor == null) return;
                if (!_anchor.IsVisible) return;
                if (_anchor.WindowState == WindowState.Minimized) return;

                double w = ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? 0 : Width);
                if (w <= 0) return;

                // ---- 基准矩形：优先锚点可见外框；拿不到（DWM 关闭/句柄未就绪）退外层矩形 ----
                Rect vis;
                if (!TryGetAnchorVisibleFrameDip(out vis))
                {
                    // 降级：外层矩形比可见框大一整圈隐形边框，可能仍留细缝，
                    // 但绝不会因为"取不到基准"就不定位。高度取外层高（略高一点，不致命）。
                    double aWidth = _anchor.ActualWidth > 0 ? _anchor.ActualWidth : _anchor.Width;
                    if (double.IsNaN(aWidth) || aWidth <= 0) return;
                    double aHeight = _anchor.ActualHeight > 0 ? _anchor.ActualHeight : 0;
                    vis = new Rect(_anchor.Left, _anchor.Top, aWidth, aHeight);
                }

                var wa = GetWorkAreaDip();

                // ---- 高度先对齐：定了高，下面垂直夹取才有意义 ----
                // 只在真的不同时才写 Height：Height 会触发 SizeChanged → 又回调 Reposition，
                // 加了这个 0.5 容差才能收敛（否则是死循环）。
                double wantH = vis.Height;
                if (wantH > 0 && Math.Abs(Height - wantH) > 0.5)
                {
                    Height = wantH;
                }

                // 用 wantH 而不是 ActualHeight 参与夹取：Height 刚落地的这一拍，
                // ActualHeight 还是旧值，用它会把窗口夹到错误的纵向位置。
                double h = wantH > 0 ? wantH : (ActualHeight > 0 ? ActualHeight : 0);

                // ---- 水平：贴可见右缘（Gap = 0），右侧不足则翻到可见左缘 ----
                double wantRight = vis.Right + Gap;
                double wantLeft = vis.Left - Gap - w;

                double left;
                if (wa.Right - EdgePad - wantRight >= 0) left = wantRight;
                else if (wantLeft - (wa.Left + EdgePad) >= 0) left = wantLeft;
                else left = Math.Max(wa.Left + EdgePad, wa.Right - EdgePad - w);

                // 兜底夹取：无论如何都要落在工作区内。
                // 正常路径（母窗口在屏内）下是空操作；真正兜的是母窗口**自身在屏外**
                // 的情况 —— 最小化时 Windows 把窗口坐标报成 (-32000,-32000)，
                // 那时上面每一条分支算出来的位置都在屏外。
                if (left < wa.Left + EdgePad) left = wa.Left + EdgePad;
                if (left + w > wa.Right - EdgePad) left = Math.Max(wa.Left + EdgePad, wa.Right - EdgePad - w);

                // ---- 垂直：与可见顶对齐（等高之下，这就是上下沿同时对齐）----
                double top = vis.Top;
                if (h > 0)
                {
                    if (top + h > wa.Bottom - EdgePad) top = wa.Bottom - EdgePad - h;
                    if (top < wa.Top + EdgePad) top = wa.Top + EdgePad;
                }

                if (!double.IsNaN(left) && !double.IsInfinity(left)) Left = left;
                if (!double.IsNaN(top) && !double.IsInfinity(top)) Top = top;
            }
            catch (Exception ex)
            {
                App.Log("MenuPreviewWindow.Reposition error: " + ex.Message);
            }
        }

        /// <summary>
        /// 取锚点窗口的**可见外框**，单位 **DIP**（<c>Window.Left/Top</c> 用的是 DIP）。
        /// 取不到返回 false，调用方自行降级。
        /// </summary>
        private bool TryGetAnchorVisibleFrameDip(out Rect dip)
        {
            dip = Rect.Empty;
            try
            {
                IntPtr hwnd = new WindowInteropHelper(_anchor).Handle;
                if (hwnd == IntPtr.Zero) return false;

                Rect px;
                if (!WindowManagementService.TryGetVisibleFrameBounds(hwnd, out px)) return false;

                double scale = GetAnchorDpiScale();
                dip = new Rect(px.Left / scale, px.Top / scale, px.Width / scale, px.Height / scale);
                return dip.Width > 0 && dip.Height > 0;
            }
            catch (Exception ex)
            {
                App.Log("MenuPreviewWindow: 取可见外框失败，降级用外层矩形: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 取锚点窗口所在显示器的工作区，单位换成 **DIP**（WPF 的 Left/Top 是 DIP）。
        /// <para>
        /// WinForms 的 <c>Screen.WorkingArea</c> 是**物理像素**，必须除以缩放系数；
        /// 缩放系数取锚点窗口的 <c>TransformToDevice.M11</c>（Per-Monitor V2 下即该屏 DPI/96）。
        /// </para>
        /// <para>
        /// ⚠️ 已知边界：多显示器**混合缩放**时，DIP 基准在不同屏上并不统一
        /// （本项目 MenuOverlayWindow 的同类坐标错位问题尚未解决）。此处按
        /// "锚点窗口所在屏"换算，单屏与多屏同缩放场景正确；混合缩放场景下
        /// 预览可能偏移，属已知局限。
        /// </para>
        /// </summary>
        private Rect GetWorkAreaDip()
        {
            double scale = GetAnchorDpiScale();

            try
            {
                IntPtr handle = _anchor != null ? new WindowInteropHelper(_anchor).Handle : IntPtr.Zero;
                var screen = handle != IntPtr.Zero
                    ? System.Windows.Forms.Screen.FromHandle(handle)
                    : System.Windows.Forms.Screen.PrimaryScreen;
                var r = screen.WorkingArea;
                return new Rect(r.Left / scale, r.Top / scale, r.Width / scale, r.Height / scale);
            }
            catch (Exception ex)
            {
                // 兜底：SystemParameters.WorkArea 本身已经是 DIP（主屏）
                App.Log("MenuPreviewWindow: 取工作区失败，退回主屏 WorkArea: " + ex.Message);
                return SystemParameters.WorkArea;
            }
        }

        /// <summary>
        /// 锚点窗口所在显示器的缩放系数（DPI/96）。
        /// <para>
        /// Per-Monitor V2 下即 <c>TransformToDevice.M11</c>。Win32 给的矩形是**物理像素**，
        /// 而 <c>Window.Left/Top/Width/Height</c> 是 DIP，两者靠这个系数换算。
        /// 顺手缓存到 <see cref="_dpiScale"/> 供日志诊断。
        /// </para>
        /// </summary>
        private double GetAnchorDpiScale()
        {
            double scale = 1.0;
            try
            {
                var src = _anchor != null ? PresentationSource.FromVisual(_anchor) : null;
                if (src != null && src.CompositionTarget != null)
                {
                    double m = src.CompositionTarget.TransformToDevice.M11;
                    if (!double.IsNaN(m) && !double.IsInfinity(m) && m > 0) scale = m;
                }
            }
            catch (Exception ex)
            {
                App.Log("MenuPreviewWindow: 取 DPI 失败，按 100% 处理: " + ex.Message);
            }

            _dpiScale = scale;
            return scale;
        }
    }
}
