using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinLoop.Config;
using WinLoop.Menus;
using WinLoop.Models;

namespace WinLoop.Core
{
    public class MouseManager
    {
        private readonly ConfigManager _configManager;
        private readonly Window _mainWindow;
        private readonly RadialMenuFactory _menuFactory;
        
        private DispatcherTimer _triggerTimer;
        private Point _middleButtonDownPoint;
        private bool _menuVisible;
        private RadialMenu _currentMenu;
        private Window _overlayWindow;

        // low-level mouse hook
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelMouseProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        
        public MouseManager(Window mainWindow, ConfigManager configManager)
        {
            _mainWindow = mainWindow;
            _configManager = configManager;
            _menuFactory = new RadialMenuFactory();
            
            InitializeTimer();
            InstallHook();
        }
        
        private void InitializeTimer()
        {
            _triggerTimer = new DispatcherTimer();
            _triggerTimer.Tick += OnTriggerTimerTick;
        }
        
        private void InstallHook()
        {
            _proc = HookCallback;
            _hookId = SetHook(_proc);
        }

        private IntPtr SetHook(LowLevelMouseProc proc)
        {
            // 获取当前模块句柄
            IntPtr moduleHandle = NativeMethods.GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName);
            return NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, proc, moduleHandle, 0);
        }

        private void UninstallHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        // 对外公开一个停止/释放方法，以便在应用退出时卸载钩子
        public void Dispose()
        {
            try
            {
                _triggerTimer?.Stop();
            }
            catch { }
            try
            {
                UninstallHook();
            }
            catch { }
        }
        
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            const int WM_MBUTTONDOWN = 0x0207;
            const int WM_MBUTTONUP = 0x0208;

            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                try
                {
                    if (msg == WM_MBUTTONDOWN)
                    {
                        var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        var screenPt = new Point(ms.pt.x, ms.pt.y);
                        _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _middleButtonDownPoint = screenPt;
                            var config = _configManager.LoadConfig();
                            _triggerTimer.Interval = TimeSpan.FromMilliseconds(config.TriggerDelay);
                            _triggerTimer.Start();
                        }));
                    }
                    else if (msg == WM_MBUTTONUP)
                    {
                        _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _triggerTimer.Stop();
                            HideMenu();
                        }));
                    }
                }
                catch { }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        
        private void OnTriggerTimerTick(object sender, EventArgs e)
        {
            _triggerTimer.Stop();
            ShowMenu(_middleButtonDownPoint);
        }
        
        private void ShowMenu(Point centerPoint)
        {
            if (_menuVisible)
            {
                return;
            }
            
            var config = _configManager.LoadConfig();
            // 创建菜单实例
            _currentMenu = _menuFactory.CreateMenu(config.MenuStyle, config);

            double radius = 100;
            switch (config.MenuStyle)
            {
                case MenuStyle.BasicRadial:
                    radius = config.BasicRadialMenuConfig.OuterRadius;
                    break;
                case MenuStyle.CSHeadshotOctagon:
                    radius = config.CSHeadshotMenuConfig.Radius;
                    break;
                case MenuStyle.SpiderWeb:
                    radius = config.SpiderWebMenuConfig.OuterRadius;
                    break;
            }

            // 初始化菜单，传入局部中心点
            var localCenter = new Point(radius, radius);
            _currentMenu.Initialize(config, localCenter);
            _currentMenu.OnItemSelected += OnMenuItemSelected;

            // 创建透明悬浮窗口承载菜单
            _overlayWindow = new Window
            {
                Width = radius * 2,
                Height = radius * 2,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Left = centerPoint.X - radius,
                Top = centerPoint.Y - radius
            };

            // 将菜单放入窗口并显示
            _overlayWindow.Content = _currentMenu;
            _overlayWindow.Show();
            _menuVisible = true;
        }
        
        private void HideMenu()
        {
            if (!_menuVisible || _currentMenu == null)
            {
                return;
            }
            _currentMenu.OnItemSelected -= OnMenuItemSelected;
            try
            {
                if (_overlayWindow != null)
                {
                    _overlayWindow.Close();
                    _overlayWindow = null;
                }
            }
            catch { }

            _currentMenu = null;
            _menuVisible = false;
        }
        
        private void OnMenuItemSelected(MenuItemPosition itemPosition)
        {
            var config = _configManager.LoadConfig();
            if (config.ActionMapping.TryGetValue(itemPosition, out var action))
            {
                // 执行窗口操作
                WindowActionExecutor.Execute(action);
            }
            
            HideMenu();
        }
    }
}

// P/Invoke and native structures for low-level mouse hook
[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int x;
    public int y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

internal static partial class NativeMethods
{
    public const int WH_MOUSE_LL = 14;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);
}
