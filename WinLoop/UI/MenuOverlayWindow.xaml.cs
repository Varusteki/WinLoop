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

            // 创建菜单
            var factory = new RadialMenuFactory();
            _currentMenu = factory.CreateMenu(_config.MenuStyle, _config);
            App.Log($"Menu created, style: {_config.MenuStyle}");

            // 获取菜单半径以计算位置
            double menuRadius = GetMenuRadius();
            App.Log($"Menu radius: {menuRadius}");

            // 菜单应该以鼠标位置为中心，所以需要偏移半径
            Point menuTopLeft = new Point(
                _logicalCenter.X - menuRadius,
                _logicalCenter.Y - menuRadius
            );

            // 以菜单中心为缩放原点，让弹出动画从鼠标位置展开
            _menuScale = new ScaleTransform(0.6, 0.6, menuRadius, menuRadius);
            _currentMenu.RenderTransform = _menuScale;
            _currentMenu.Opacity = 0;

            _currentMenu.Initialize(_config, new Point(menuRadius, menuRadius)); // 菜单内部的中心点
            App.Log($"Menu initialized, size: {_currentMenu.Width}x{_currentMenu.Height}");

            // 设置好菜单位置并添加到 Canvas
            System.Windows.Controls.Canvas.SetLeft(_currentMenu, menuTopLeft.X);
            System.Windows.Controls.Canvas.SetTop(_currentMenu, menuTopLeft.Y);
            OverlayCanvas.Children.Add(_currentMenu);
            App.Log($"Menu positioned at Canvas ({menuTopLeft.X}, {menuTopLeft.Y})");

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

        private double GetMenuRadius()
        {
            switch (_config.MenuStyle)
            {
                case MenuStyle.BasicRadial:
                    return _config.BasicRadialMenuConfig.OuterRadius;
                case MenuStyle.CSHeadshotOctagon:
                    return _config.CSHeadshotMenuConfig.Radius;
                case MenuStyle.SpiderWeb:
                    return _config.SpiderWebMenuConfig.OuterRadius;
                case MenuStyle.Bagua:
                    return _config.BaguaMenuConfig.OuterRadius * 1.2; // 八卦菜单绘制时外扩了1.2倍
                default:
                    return _config.BasicRadialMenuConfig.OuterRadius;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_currentMenu == null)
            {
                App.Log("OnMouseMove: _currentMenu is null");
                return;
            }

            var mousePos = e.GetPosition(OverlayCanvas);
            UpdateHighlight(mousePos);
        }

        /// <summary>
        /// 用屏幕坐标（物理像素）更新高亮。供全局鼠标钩子推送调用，替代原来的 16ms 轮询。
        /// </summary>
        public void UpdateHighlightFromScreen(Point screenPos)
        {
            if (_currentMenu == null || !this.IsVisible) return;

            // 换算到逻辑坐标：菜单定位与命中判定都在逻辑坐标系内进行
            Point logical = PhysicalToLogical(screenPos);
            double menuRadius = GetMenuRadius();

            Point menuPos = new Point(
                logical.X - _logicalCenter.X + menuRadius,
                logical.Y - _logicalCenter.Y + menuRadius
            );

            ApplyHighlight(menuPos);
        }

        private void UpdateHighlight(Point menuPos)
        {
            if (_currentMenu == null) return;
            ApplyHighlight(menuPos);
        }

        private void ApplyHighlight(Point menuPos)
        {
            // 直接使用菜单自己的 GetSelectedItem 方法（每个菜单有自己的角度计算逻辑）
            var position = _currentMenu.GetSelectedItem(menuPos);

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
