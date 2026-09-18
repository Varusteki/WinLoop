using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinLoop.Config;
using WinLoop.Menus;
using WinLoop.Models;

namespace WinLoop.UI
{
    public partial class MenuOverlayWindow : Window
    {
        private RadialMenu _currentMenu;
        private readonly ConfigManager _configManager;
        private AppConfig _config;
        private Point _centerPosition;
        private Point _logicalOrigin; // 虚拟屏幕左上角的逻辑坐标（窗口在 Canvas 中的原点）
        private Point _logicalCenter; // 菜单中心的 Canvas 内逻辑坐标
        private MenuItemPosition? _highlightedPosition;
        private ScaleTransform _menuScale;

        // 弹出/收起动画时长。取值偏短，避免掩盖"按下中键即出菜单"的即时感。
        private static readonly Duration ShowDuration = new Duration(TimeSpan.FromMilliseconds(120));
        private static readonly Duration HideDuration = new Duration(TimeSpan.FromMilliseconds(80));

        public event Action<WindowAction> ActionSelected;

        public MenuOverlayWindow()
        {
            InitializeComponent();
            _configManager = new ConfigManager();
            _config = _configManager.LoadConfig();

            // 兜底：窗口内的鼠标移动仍可用于更新高亮。
            // 主路径是全局钩子推送（见 App.OnGlobalMouseMoved），
            // 因为按住中键时 WPF 可能收不到 MouseMove。
            OverlayCanvas.MouseMove += OnMouseMove;
            OverlayCanvas.PreviewMouseLeftButtonDown += OnLeftButtonDown;
        }

        /// <summary>
        /// 把物理像素坐标换算为本窗口的 WPF 逻辑坐标。
        ///
        /// 鼠标钩子（WH_MOUSE_LL）报告的是物理像素，而 WPF 的 Left/Top/Width/Height
        /// 是逻辑单位。在 Per-Monitor V2 + 非 100% 缩放时两者不相等，
        /// 必须显式换算，否则菜单会偏离鼠标位置。
        /// </summary>
        private Point PhysicalToLogical(Point physical)
        {
            try
            {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    var m = source.CompositionTarget.TransformFromDevice;
                    return m.Transform(physical);
                }
            }
            catch (Exception ex)
            {
                App.Log($"PhysicalToLogical failed: {ex.Message}");
            }
            // 换算不可用（窗口尚未有 PresentationSource）时按 1:1 处理
            return physical;
        }

        public void ShowAt(Point screenPosition)
        {
            App.Log($"ShowAt called with position: ({screenPosition.X}, {screenPosition.Y})");

            // 更新状态
            _centerPosition = screenPosition;
            _highlightedPosition = null;

            // 设置窗口覆盖整个虚拟屏幕（支持多显示器）
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            // 重新加载配置，以防用户修改了设置
            _config = _configManager.LoadConfig();

            // 菜单中心与坐标换算基准统一使用逻辑坐标。
            // 传入的 screenPosition 是钩子报告的物理像素，需要先换算。
            this.Show(); // 先 Show 以获得 PresentationSource，DPoP 换算才有效
            Point logicalCenter = PhysicalToLogical(_centerPosition);
            _logicalOrigin = new Point(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop);
            _logicalCenter = new Point(logicalCenter.X - _logicalOrigin.X, logicalCenter.Y - _logicalOrigin.Y);
            App.Log($"Center: physical=({_centerPosition.X},{_centerPosition.Y}) logical=({logicalCenter.X},{logicalCenter.Y})");

            // 尺寸自适应：配置里存的是「100% 缩放基准的逻辑像素」，
            // 这里按本窗口所在显示器的 DPI 缩放换算成实际逻辑像素，否则
            // 同一份配置在 200% 缩放的 4K 屏上只有 1080p 的四分之一大。
            // 必须在 CreateMenu/Initialize 之前设好 —— 菜单在 Initialize 里读这个系数。
            _config.SizingScale = SizingScale.FromVisual(this);
            App.Log($"SizingScale (DPI/96, this monitor) = {_config.SizingScale}");

            // 创建菜单
            var factory = new RadialMenuFactory();
            _currentMenu = factory.CreateMenu(_config.MenuStyle, _config);
            App.Log($"Menu created, style: {_config.MenuStyle}");

            // 先初始化菜单。Initialize 会把该样式真实的半尺寸写入 VisualRadius，
            // 外部必须用这个值定位：各样式绘制留白不同，不能再用配置半径自行推算。
            _currentMenu.Initialize(_config, new Point(0, 0));
            double menuRadius = _currentMenu.VisualRadius;
            App.Log($"Menu initialized: VisualRadius={menuRadius}, size={_currentMenu.Width}x{_currentMenu.Height}");

            if (menuRadius <= 0)
            {
                // 兜底：样式未正确上报半尺寸时按窗口尺寸回退，避免菜单跑到屏幕外
                menuRadius = Math.Max(_currentMenu.Width, _currentMenu.Height) / 2;
                App.Log($"VisualRadius invalid, falling back to {menuRadius}");
            }

            // 菜单应该以鼠标位置为中心，所以需要偏移半径
            Point menuTopLeft = new Point(
                _logicalCenter.X - menuRadius,
                _logicalCenter.Y - menuRadius
            );

            // 以菜单中心为缩放原点，让弹出动画从鼠标位置展开
            _menuScale = new ScaleTransform(0.6, 0.6, menuRadius, menuRadius);
            _currentMenu.RenderTransform = _menuScale;
            _currentMenu.Opacity = 0;

            // 设置好菜单位置并添加到 Canvas
            System.Windows.Controls.Canvas.SetLeft(_currentMenu, menuTopLeft.X);
            System.Windows.Controls.Canvas.SetTop(_currentMenu, menuTopLeft.Y);
            OverlayCanvas.Children.Add(_currentMenu);
            App.Log($"Menu positioned at Canvas ({menuTopLeft.X}, {menuTopLeft.Y})");

            // 菜单控件自己不参与命中测试：高亮完全由覆盖窗口按屏幕坐标统一更新，
            // 菜单只负责把高亮画成对应形状。若让它可命中，指针压在菜单图形上时
            // 事件会被它吃掉，窗口内 MouseMove 这条兜底路径就会在菜单范围内失效。
            _currentMenu.IsHitTestVisible = false;

            // 确保Canvas可以接收鼠标事件
            OverlayCanvas.IsHitTestVisible = true;

            // 窗口已在前面 Show，这里只需激活
            this.Activate();
            this.Focus();

            // 捕获鼠标以确保接收鼠标事件
            Mouse.Capture(OverlayCanvas);
            App.Log($"Mouse captured: {Mouse.Captured != null}");

            // 弹出动画：缩放 + 淡入
            PlayShowAnimation();

            App.Log($"Window shown and activated, Window size: {this.Width}x{this.Height}, Visible: {this.IsVisible}");
        }

        private void PlayShowAnimation()
        {
            if (_menuScale == null || _currentMenu == null) return;

            try
            {
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

                var scaleAnimX = new DoubleAnimation(0.6, 1.0, ShowDuration) { EasingFunction = easing };
                var scaleAnimY = new DoubleAnimation(0.6, 1.0, ShowDuration) { EasingFunction = easing };
                var fadeAnim = new DoubleAnimation(0, 1, ShowDuration) { EasingFunction = easing };

                // 动画不参与布局计算，减少首帧开销
                _menuScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
                _menuScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);
                _currentMenu.BeginAnimation(OpacityProperty, fadeAnim);
            }
            catch (Exception ex)
            {
                App.Log($"PlayShowAnimation error: {ex.Message}");
                // 动画失败不影响功能，直接设为最终状态
                if (_menuScale != null)
                {
                    _menuScale.ScaleX = 1.0;
                    _menuScale.ScaleY = 1.0;
                }
                if (_currentMenu != null)
                {
                    _currentMenu.Opacity = 1;
                }
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_currentMenu == null) return;

            // e.GetPosition(OverlayCanvas) 给的是 **Canvas 坐标**（原点 = 虚拟屏幕左上角），
            // 与 UpdateHighlightFromScreen 那条钩子路径最终得到的坐标是**同一个坐标系**，
            // 所以这里可以安全地走同一个入口。
            UpdateHighlight(e.GetPosition(OverlayCanvas));
        }

        /// <summary>
        /// 用屏幕坐标（物理像素）更新高亮。供全局鼠标钩子推送调用，替代原来的 16ms 轮询。
        /// </summary>
        public void UpdateHighlightFromScreen(Point screenPos)
        {
            if (_currentMenu == null || !this.IsVisible) return;
            UpdateHighlight(ScreenToCanvas(screenPos));
        }

        /// <summary>
        /// 屏幕物理像素 → Canvas 逻辑坐标。
        ///
        /// 钩子（WH_MOUSE_LL）报的是**物理像素**，原点在主显示器左上角；
        /// 而 Canvas 的原点在**虚拟屏幕左上角**（窗口 Left/Top = VirtualScreenLeft/Top），
        /// 两者差一个虚拟屏幕原点，必须减掉。
        /// 单屏时虚拟屏幕原点就是 (0,0)，少减这一下看不出问题；
        /// 多屏且副屏在主屏左侧或上方时，判定会整体偏掉。
        /// </summary>
        private Point ScreenToCanvas(Point physical)
        {
            Point logical = PhysicalToLogical(physical);
            return new Point(logical.X - _logicalOrigin.X, logical.Y - _logicalOrigin.Y);
        }

        /// <summary>
        /// 高亮更新的**唯一入口**：不管坐标从哪条路径来，都先在这里换算成菜单自身坐标再判定。
        /// 两条路径（全局钩子 / 窗口内 MouseMove）因此不可能再各算一套扇区。
        /// </summary>
        private void UpdateHighlight(Point canvasPos)
        {
            if (_currentMenu == null) return;

            // 用菜单自报的真实半尺寸；菜单在 Canvas 中的左上角与它基于同一基准。
            double menuRadius = _currentMenu.VisualRadius;
            if (menuRadius <= 0) return;

            ApplyHighlight(CanvasToMenu(canvasPos, MenuCenterInCanvas(menuRadius), menuRadius));
        }

        /// <summary>
        /// 菜单中心在 Canvas 中的位置。
        ///
        /// **不要改回读 `_logicalCenter` 字段**：那是 ShowAt 里用「屏幕坐标经 DPI 换算
        /// 再减虚拟屏幕原点」推导出来的，中间任何一步在非 100% 缩放或多屏下出偏差，
        /// 判定就会整体偏掉（表现为恒选某一个扇区，与指针方向无关）。
        /// 这里直接读菜单控件在 Canvas 中的**实际布局位置**，再由它加半径得到中心 ——
        /// 与菜单真正画在哪里永远一致，不受任何换算影响。
        /// </summary>
        private Point MenuCenterInCanvas(double menuRadius)
        {
            double left = System.Windows.Controls.Canvas.GetLeft(_currentMenu);
            double top = System.Windows.Controls.Canvas.GetTop(_currentMenu);

            if (double.IsNaN(left) || double.IsNaN(top))
            {
                // 布局尚未应用（极早期调用）：退回 ShowAt 里算好的中心值
                return _logicalCenter;
            }

            return new Point(left + menuRadius, top + menuRadius);
        }

        /// <summary>
        /// Canvas 逻辑坐标 → 菜单自身坐标（判定唯一接受的坐标系）。
        /// 做成静态纯函数，便于 <c>Tools/MenuProbe</c> 回归这颗坐标换算。
        /// </summary>
        /// <param name="canvasPos">指针在 Canvas 上的位置。</param>
        /// <param name="menuCenterInCanvas">菜单中心在 Canvas 上的位置（<c>MenuCenterInCanvas</c> 的结果）。</param>
        /// <param name="menuRadius">菜单半尺寸（<c>VisualRadius</c>）。</param>
        public static Point CanvasToMenu(Point canvasPos, Point menuCenterInCanvas, double menuRadius)
        {
            return new Point(
                canvasPos.X - menuCenterInCanvas.X + menuRadius,
                canvasPos.Y - menuCenterInCanvas.Y + menuRadius
            );
        }

        private void ApplyHighlight(Point menuPos)
        {
            // 判定统一在 RadialMenu 里（四种样式同一份），这里只管把结果落到画面上
            var position = _currentMenu.GetSelectedItem(menuPos);

            // 诊断：把「喂进判定的坐标 + 菜单中心 + 算出的扇区」成对记下来，
            // 便于定位「启动初期某方向不触发」这类时序问题（正常时应静默）。
            if (App.DiagnosticHighlightLogging)
            {
                Point center = MenuCenterInCanvas(_currentMenu.VisualRadius);
                App.Log($"[DIAG] menuPos=({menuPos.X:0.#},{menuPos.Y:0.#}) " +
                        $"center=({center.X:0.#},{center.Y:0.#}) " +
                        $"radius={_currentMenu.VisualRadius:0.#} " +
                        $"-> {(position.HasValue ? position.Value.ToString() : "null")} " +
                        $"(prev={(_highlightedPosition.HasValue ? _highlightedPosition.Value.ToString() : "null")})");
            }

            if (position != _highlightedPosition)
            {
                _highlightedPosition = position;
                if (position.HasValue)
                {
                    App.Log($"Highlighting sector: {position.Value}");
                    _currentMenu.HighlightItem(position.Value);
                }
                else
                {
                    // 清除高亮
                    App.Log("Clearing highlight");
                    _currentMenu.ClearHighlight();
                }
            }
        }

        private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 左键点击取消
            HideAnimated();
        }

        public void ExecuteAction()
        {
            App.Log($"ExecuteAction called, highlighted: {_highlightedPosition}, _currentMenu={_currentMenu != null}");

            // 先保存要执行的动作
            WindowAction? actionToExecute = null;
            if (_highlightedPosition.HasValue && _config.ActionMapping.TryGetValue(_highlightedPosition.Value, out var action))
            {
                actionToExecute = action;
                App.Log($"Will execute action: {action}");
            }
            else
            {
                App.Log($"No action to execute: highlighted={_highlightedPosition}, hasMapping={_highlightedPosition.HasValue && _config.ActionMapping.ContainsKey(_highlightedPosition.Value)}");
            }

            // 释放鼠标捕获
            if (Mouse.Captured == OverlayCanvas)
            {
                Mouse.Capture(null);
                App.Log("Mouse capture released");
            }

            // 隐藏窗口（窗口会被 App 关闭和销毁）
            HideAnimated();
            App.Log("Window hidden");

            // 最后执行动作
            if (actionToExecute.HasValue)
            {
                App.Log($"Executing action: {actionToExecute.Value}");
                ActionSelected?.Invoke(actionToExecute.Value);
            }

            App.Log("ExecuteAction completed");
        }

        /// <summary>
        /// 带淡出动画的隐藏。动画期间窗口立即不可命中，避免阻挡后续操作。
        /// </summary>
        private void HideAnimated()
        {
            if (!this.IsVisible)
            {
                this.Hide();
                return;
            }

            try
            {
                OverlayCanvas.IsHitTestVisible = false;

                var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                var fadeOut = new DoubleAnimation(OverlayCanvas.Opacity, 0, HideDuration);
                fadeOut.Completed += (s, e) => tcs.TrySetResult(true);

                OverlayCanvas.BeginAnimation(OpacityProperty, fadeOut);

                // 动画很短（80ms），同步等待不会造成可感知的卡顿，
                // 且能保证窗口在动作执行前已完成隐藏。
                tcs.Task.Wait(200);
            }
            catch (Exception ex)
            {
                App.Log($"HideAnimated error: {ex.Message}");
            }
            finally
            {
                try
                {
                    OverlayCanvas.BeginAnimation(OpacityProperty, null);
                    OverlayCanvas.Opacity = 1;
                }
                catch { }
                this.Hide();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            OverlayCanvas.Children.Clear();
            _currentMenu = null;
            _highlightedPosition = null;
            App.Log("MenuOverlayWindow closed and cleaned up");
        }
    }
}
