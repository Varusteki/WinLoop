using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
        private MenuItemPosition? _highlightedPosition;
        private DispatcherTimer _mouseTrackingTimer;

        public event Action<WindowAction> ActionSelected;

        public MenuOverlayWindow()
        {
            InitializeComponent();
            _configManager = new ConfigManager();
            _config = _configManager.LoadConfig();

            // 监听鼠标移动
            OverlayCanvas.MouseMove += OnMouseMove;
            OverlayCanvas.PreviewMouseLeftButtonDown += OnLeftButtonDown;
            
            // 创建定时器来轮询鼠标位置（因为按住中键时MouseMove可能不触发）
            _mouseTrackingTimer = new DispatcherTimer();
            _mouseTrackingTimer.Interval = TimeSpan.FromMilliseconds(16); // 约60fps
            _mouseTrackingTimer.Tick += (s, e) =>
            {
                try
                {
                    if (this.IsVisible && _currentMenu != null)
                    {
                        // 获取当前鼠标屏幕位置
                        var screenPos = System.Windows.Forms.Control.MousePosition;
                        var mousePos = new Point(screenPos.X, screenPos.Y);
                        UpdateHighlight(mousePos);
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"MouseTrackingTimer error: {ex.Message}");
                }
            };
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
            
            // 创建菜单
            var factory = new RadialMenuFactory();
            _currentMenu = factory.CreateMenu(_config.MenuStyle, _config);
            App.Log($"Menu created, style: {_config.MenuStyle}");
            
            // 获取菜单半径以计算位置
            double menuRadius = GetMenuRadius();
            App.Log($"Menu radius: {menuRadius}");
            
            // 菜单应该以鼠标位置为中心，所以需要偏移半径
            Point menuTopLeft = new Point(
                _centerPosition.X - menuRadius,
                _centerPosition.Y - menuRadius
            );
            
            _currentMenu.Initialize(_config, new Point(menuRadius, menuRadius)); // 菜单内部的中心点
            App.Log($"Menu initialized, size: {_currentMenu.Width}x{_currentMenu.Height}");

            // 设置好菜单位置并添加到 Canvas
            System.Windows.Controls.Canvas.SetLeft(_currentMenu, menuTopLeft.X);
            System.Windows.Controls.Canvas.SetTop(_currentMenu, menuTopLeft.Y);
            OverlayCanvas.Children.Add(_currentMenu);
            App.Log($"Menu positioned at Canvas ({menuTopLeft.X}, {menuTopLeft.Y})");

            // 确保Canvas可以接收鼠标事件
            OverlayCanvas.IsHitTestVisible = true;

            // 显示窗口
            this.Show();
            this.Activate();
            this.Focus();
            
            // 捕获鼠标以确保接收鼠标事件
            Mouse.Capture(OverlayCanvas);
            App.Log($"Mouse captured: {Mouse.Captured != null}");
            
            // 启动鼠标位置轮询定时器
            _mouseTrackingTimer.Start();
            App.Log("Mouse tracking timer started");
            
            App.Log($"Window shown and activated, Window size: {this.Width}x{this.Height}, Visible: {this.IsVisible}");
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
        
        private void UpdateHighlight(Point screenPos)
        {
            if (_currentMenu == null) return;
            
            // 将屏幕坐标转换为菜单内部坐标
            // 每个菜单的中心点都是其自身宽度/高度的一半（GetMenuRadius返回的是这个值）
            double menuRadius = GetMenuRadius();
            Point menuPos = new Point(
                screenPos.X - _centerPosition.X + menuRadius,
                screenPos.Y - _centerPosition.Y + menuRadius
            );
            
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
            this.Hide();
        }

        private MenuItemPosition? CalculateMenuPosition(Point mousePos)
        {
            // 计算鼠标相对于菜单中心的位置
            double dx = mousePos.X - _centerPosition.X;
            double dy = mousePos.Y - _centerPosition.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // 根据菜单样式获取有效半径范围
            double minRadius = 0;
            double maxRadius = 0;
            
            switch (_config.MenuStyle)
            {
                case MenuStyle.BasicRadial:
                    minRadius = _config.BasicRadialMenuConfig.InnerRadius;
                    maxRadius = _config.BasicRadialMenuConfig.OuterRadius;
                    break;
                case MenuStyle.CSHeadshotOctagon:
                    minRadius = 0;
                    maxRadius = _config.CSHeadshotMenuConfig.Radius;
                    break;
                case MenuStyle.SpiderWeb:
                    minRadius = 0;
                    maxRadius = _config.SpiderWebMenuConfig.OuterRadius;
                    break;
                case MenuStyle.Bagua:
                    minRadius = _config.BaguaMenuConfig.OuterRadius * 0.35;
                    maxRadius = _config.BaguaMenuConfig.OuterRadius * 1.1;
                    break;
            }

            if (distance < minRadius || distance > maxRadius)
            {
                return null;
            }

            // 计算角度（从12点钟方向开始，顺时针）
            double angle = Math.Atan2(dy, dx);
            angle = angle * 180 / Math.PI; // 转换为度数
            angle += 90; // 调整为从12点钟方向开始
            if (angle < 0) angle += 360;

            // 8个扇区，每个45度
            int sector = (int)((angle + 22.5) / 45.0) % 8;

            return (MenuItemPosition)sector;
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
            
            // 停止鼠标跟踪定时器
            _mouseTrackingTimer?.Stop();
            App.Log("Mouse tracking timer stopped");
            
            // 释放鼠标捕获
            if (Mouse.Captured == OverlayCanvas)
            {
                Mouse.Capture(null);
                App.Log("Mouse capture released");
            }
            
            // 隐藏窗口（窗口会被 App 关闭和销毁）
            this.Hide();
            App.Log("Window hidden");
            
            // 最后执行动作
            if (actionToExecute.HasValue)
            {
                App.Log($"Executing action: {actionToExecute.Value}");
                ActionSelected?.Invoke(actionToExecute.Value);
            }
            
            App.Log("ExecuteAction completed");
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
