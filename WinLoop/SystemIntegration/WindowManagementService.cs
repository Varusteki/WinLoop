using System;
using System.Runtime.InteropServices;
using System.Windows;
using WinLoop.Models;

namespace WinLoop.SystemIntegration
{
    /// <summary>
    /// 窗口管理服务，用于执行各种窗口操作
    /// </summary>
    public class WindowManagementService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        // DwmGetWindowAttribute 用于获取窗口实际可见区域（不含不可见边框）
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        // 键盘输入相关
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_LWIN = 0x5B;  // 左Win键
        private const byte VK_D = 0x44;     // D键
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int SW_MINIMIZE = 6;
        private const int SW_MAXIMIZE = 3;
        private const int SW_RESTORE = 9;
        private const int SW_SHOWMINIMIZED = 2;

        /// <summary>
        /// 获取窗口的不可见边框大小（Windows 10/11 的阴影边框）
        /// </summary>
        private void GetWindowFrameOffset(IntPtr hwnd, out int leftOffset, out int topOffset, out int rightOffset, out int bottomOffset)
        {
            leftOffset = 0;
            topOffset = 0;
            rightOffset = 0;
            bottomOffset = 0;

            if (GetWindowRect(hwnd, out RECT windowRect))
            {
                if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frameRect, Marshal.SizeOf(typeof(RECT))) == 0)
                {
                    // 计算不可见边框的大小
                    leftOffset = frameRect.Left - windowRect.Left;
                    topOffset = frameRect.Top - windowRect.Top;
                    rightOffset = windowRect.Right - frameRect.Right;
                    bottomOffset = windowRect.Bottom - frameRect.Bottom;
                }
            }
        }

        /// <summary>
        /// 取目标窗口所在显示器的工作区（物理像素，已排除任务栏）。
        ///
        /// 不用 WPF 的 SystemParameters.WorkArea：那是逻辑单位，
        /// 在 Per-Monitor V2 + 非 100% 缩放时会与 SetWindowPos 需要的物理像素错位。
        /// 这里改用 MonitorFromWindow + GetMonitorInfo，直接拿到物理像素工作区。
        /// </summary>
        private bool TryGetWorkArea(IntPtr hwnd, out int left, out int top, out int width, out int height)
        {
            left = top = width = height = 0;

            try
            {
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero)
                {
                    return false;
                }

                var info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

                if (!GetMonitorInfo(monitor, ref info))
                {
                    return false;
                }

                // rcWork 已排除任务栏
                left = info.rcWork.Left;
                top = info.rcWork.Top;
                width = info.rcWork.Right - info.rcWork.Left;
                height = info.rcWork.Bottom - info.rcWork.Top;
                return width > 0 && height > 0;
            }
            catch (Exception ex)
            {
                App.Log($"TryGetWorkArea failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 取工作区；Win32 调用失败时回退到 WPF 的 SystemParameters.WorkArea。
        /// </summary>
        private void GetWorkArea(IntPtr hwnd, out int left, out int top, out int width, out int height)
        {
            if (TryGetWorkArea(hwnd, out left, out top, out width, out height))
            {
                return;
            }

            var wa = SystemParameters.WorkArea;
            left = (int)wa.Left;
            top = (int)wa.Top;
            width = (int)wa.Width;
            height = (int)wa.Height;
            App.Log($"Falling back to WPF WorkArea: {left},{top} {width}x{height}");
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        /// <summary>
        /// 移动窗口并补偿不可见边框，使窗口可见部分完全贴合目标区域。
        /// 所有参数均为物理像素，与 SetWindowPos 的坐标系一致。
        /// </summary>
        private void MoveWindowCompensated(IntPtr hwnd, int x, int y, int width, int height)
        {
            // 先还原窗口。SW_RESTORE 是同步生效的，不需要额外等待。
            ShowWindow(hwnd, SW_RESTORE);

            GetWindowFrameOffset(hwnd, out int leftOffset, out int topOffset, out int rightOffset, out int bottomOffset);

            // 补偿不可见边框：向外扩展窗口位置和大小
            int adjustedX = x - leftOffset;
            int adjustedY = y - topOffset;
            int adjustedWidth = width + leftOffset + rightOffset;
            int adjustedHeight = height + topOffset + bottomOffset;

            // 用 SetWindowPos 代替 MoveWindow：不重绘同步等待，也不改变 Z 序，
            // 配合 SWP_ASYNCWINDOWPOS 可避免跨进程窗口操作时的阻塞。
            SetWindowPos(hwnd, IntPtr.Zero, adjustedX, adjustedY, adjustedWidth, adjustedHeight,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
        }

        /// <summary>
        /// 执行窗口操作。
        /// </summary>
        /// <param name="action">要执行的动作</param>
        /// <param name="targetWindow">
        /// 目标窗口句柄。应传入中键按下瞬间锁定的窗口，避免菜单弹出后前台窗口变化导致操作错位。
        /// 传 IntPtr.Zero 时回退为实时取前台窗口（兼容旧调用）。
        /// </param>
        public void ExecuteAction(WindowAction action, IntPtr targetWindow)
        {
            try
            {
                IntPtr hwnd = ResolveHandler(targetWindow);
                App.Log($"Executing action: {action}, hwnd={hwnd}");

                switch (action)
                {
                    case WindowAction.BackToDesktop:
                        ShowDesktop();
                        break;
                    case WindowAction.Minimize:
                        MinimizeWindow(hwnd);
                        break;
                    case WindowAction.Maximize:
                        MaximizeWindow(hwnd);
                        break;
                    case WindowAction.ToggleMaximize:
                        ToggleMaximizeWindow(hwnd);
                        break;
                    case WindowAction.ToggleTopMost:
                        ToggleTopMostWindow(hwnd);
                        break;
                    case WindowAction.CloseWindow:
                        CloseWindow(hwnd);
                        break;
                    case WindowAction.LeftHalf:
                        SetWindowToHalf(hwnd, HalfPosition.Left);
                        break;
                    case WindowAction.RightHalf:
                        SetWindowToHalf(hwnd, HalfPosition.Right);
                        break;
                    case WindowAction.TopHalf:
                        SetWindowToHalf(hwnd, HalfPosition.Top);
                        break;
                    case WindowAction.BottomHalf:
                        SetWindowToHalf(hwnd, HalfPosition.Bottom);
                        break;
                    case WindowAction.TopLeftQuadrant:
                        SetWindowToQuadrant(hwnd, QuadrantPosition.TopLeft);
                        break;
                    case WindowAction.BottomLeftQuadrant:
                        SetWindowToQuadrant(hwnd, QuadrantPosition.BottomLeft);
                        break;
                    case WindowAction.TopRightQuadrant:
                        SetWindowToQuadrant(hwnd, QuadrantPosition.TopRight);
                        break;
                    case WindowAction.BottomRightQuadrant:
                        SetWindowToQuadrant(hwnd, QuadrantPosition.BottomRight);
                        break;
                    case WindowAction.LeftTwoThirds:
                        SetWindowToTwoThirds(hwnd, TwoThirdsPosition.Left);
                        break;
                    case WindowAction.RightTwoThirds:
                        SetWindowToTwoThirds(hwnd, TwoThirdsPosition.Right);
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Log($"Error executing action {action}: {ex.Message}");
            }
        }

        /// <summary>
        /// 兼容旧调用：未显式传入目标窗口时，实时取前台窗口。
        /// </summary>
        public void ExecuteAction(WindowAction action)
        {
            ExecuteAction(action, IntPtr.Zero);
        }

        /// <summary>
        /// 解析最终要操作的窗口句柄。传入为 0 或无效时会话回退到当前前台窗口，
        /// 并过滤掉桌面/任务栏/自身窗口。
        /// </summary>
        private IntPtr ResolveHandler(IntPtr targetWindow)
        {
            if (targetWindow != IntPtr.Zero && IsWindow(targetWindow))
            {
                return targetWindow;
            }

            IntPtr fg = GetForegroundWindow();
            App.Log($"Target window invalid, falling back to foreground: {fg}");
            return fg;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;

        private const uint WM_SYSCOMMAND = 0x0112;
        private static readonly IntPtr SC_CLOSE = new IntPtr(0xF060);

        private const int SW_SHOWMINIMIZED_ = 2;
        private const int SW_SHOWMAXIMIZED_ = 3;
        private const int SW_SHOWNORMAL_ = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public int ptMinPositionX;
            public int ptMinPositionY;
            public int ptMaxPositionX;
            public int ptMaxPositionY;
            public int rcNormalPositionLeft;
            public int rcNormalPositionTop;
            public int rcNormalPositionRight;
            public int rcNormalPositionBottom;
        }

        private void ShowDesktop()
        {
            // 模拟按下 Win+D 快捷键
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event(VK_D, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void MinimizeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            // 注意：这里不能用 PostMessage(WM_SYSCOMMAND, SC_MINIMIZE)。
            // WM_SYSCOMMAND/SC_MINIMIZE 是"用户主动点了标题栏最小化按钮"的语义，
            // Windows 会校验消息发起者与目标窗口的前台关系；而菜单弹出时覆盖窗口
            // 执行了 Activate()/Focus()/Mouse.Capture()，已经抢走前台，消息会被忽略。
            // 症状：软件刚启动的一两分钟内（ForegroundLockTimeout 保护期内）P5 无效，
            // 之后锁放开才恢复。ShowWindowAsync 直接设置窗口状态，不依赖前台，
            // 与 MaximizeWindow / ToggleMaximizeWindow 的实现保持一致。
            ShowWindowAsync(hwnd, SW_MINIMIZE);
        }

        private void MaximizeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, SW_MAXIMIZE);
        }

        /// <summary>
        /// 最大化 / 还原 切换。通过 GetWindowPlacement 判断当前状态再决定去向。
        /// </summary>
        private void ToggleMaximizeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            var placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));

            if (GetWindowPlacement(hwnd, ref placement))
            {
                if (placement.showCmd == SW_SHOWMAXIMIZED_)
                {
                    ShowWindowAsync(hwnd, SW_RESTORE);
                }
                else
                {
                    ShowWindowAsync(hwnd, SW_MAXIMIZE);
                }
            }
            else
            {
                // 拿不到状态时按"最大化"处理
                ShowWindowAsync(hwnd, SW_MAXIMIZE);
            }
        }

        /// <summary>
        /// 窗口置顶 / 取消置顶 切换。
        /// </summary>
        private void ToggleTopMostWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            bool isTopMost = (GetWindowExStyle(hwnd) & WS_EX_TOPMOST) != 0;
            IntPtr insertAfter = isTopMost ? HWND_NOTOPMOST : HWND_TOPMOST;

            SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// 关闭窗口。发送 SC_CLOSE 让目标自行处理（如弹出保存提示），
        /// 避免直接销毁导致数据丢失。
        /// </summary>
        private void CloseWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            PostMessage(hwnd, WM_SYSCOMMAND, SC_CLOSE, IntPtr.Zero);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOPMOST = 0x00000008;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        /// <summary>
        /// 读取窗口扩展样式。32/64 位下 GetWindowLong 的宽度不同，这里统一按 32 位取值
        /// （扩展样式本身只用到低 32 位）。
        /// </summary>
        private static int GetWindowExStyle(IntPtr hWnd)
        {
            if (IntPtr.Size == 8)
            {
                IntPtr value = GetWindowLongPtr64(hWnd, GWL_EXSTYLE);
                return unchecked((int)value.ToInt64());
            }
            return GetWindowLong32(hWnd, GWL_EXSTYLE);
        }

        private enum HalfPosition { Left, Right, Top, Bottom }
        private enum QuadrantPosition { TopLeft, TopRight, BottomLeft, BottomRight }
        private enum TwoThirdsPosition { Left, Right }

        private void SetWindowToHalf(IntPtr hwnd, HalfPosition position)
        {
            if (hwnd == IntPtr.Zero) return;

            GetWorkArea(hwnd, out int waLeft, out int waTop, out int waWidth, out int waHeight);
            int x = 0, y = 0, width = 0, height = 0;

            switch (position)
            {
                case HalfPosition.Left:
                    x = waLeft;
                    y = waTop;
                    width = waWidth / 2;
                    height = waHeight;
                    break;
                case HalfPosition.Right:
                    x = waLeft + waWidth / 2;
                    y = waTop;
                    width = waWidth / 2;
                    height = waHeight;
                    break;
                case HalfPosition.Top:
                    x = waLeft;
                    y = waTop;
                    width = waWidth;
                    height = waHeight / 2;
                    break;
                case HalfPosition.Bottom:
                    x = waLeft;
                    y = waTop + waHeight / 2;
                    width = waWidth;
                    height = waHeight / 2;
                    break;
            }

            MoveWindowCompensated(hwnd, x, y, width, height);
        }

        private void SetWindowToQuadrant(IntPtr hwnd, QuadrantPosition position)
        {
            if (hwnd == IntPtr.Zero) return;

            GetWorkArea(hwnd, out int waLeft, out int waTop, out int waWidth, out int waHeight);
            int x = 0, y = 0;
            int width = waWidth / 2;
            int height = waHeight / 2;

            switch (position)
            {
                case QuadrantPosition.TopLeft:
                    x = waLeft;
                    y = waTop;
                    break;
                case QuadrantPosition.TopRight:
                    x = waLeft + waWidth / 2;
                    y = waTop;
                    break;
                case QuadrantPosition.BottomLeft:
                    x = waLeft;
                    y = waTop + waHeight / 2;
                    break;
                case QuadrantPosition.BottomRight:
                    x = waLeft + waWidth / 2;
                    y = waTop + waHeight / 2;
                    break;
            }

            MoveWindowCompensated(hwnd, x, y, width, height);
        }

        private void SetWindowToTwoThirds(IntPtr hwnd, TwoThirdsPosition position)
        {
            if (hwnd == IntPtr.Zero) return;

            GetWorkArea(hwnd, out int waLeft, out int waTop, out int waWidth, out int waHeight);
            int x = 0, y = waTop;
            int width = 0;
            int height = waHeight;

            switch (position)
            {
                case TwoThirdsPosition.Left:
                    x = waLeft;
                    width = waWidth * 2 / 3;
                    break;
                case TwoThirdsPosition.Right:
                    x = waLeft + waWidth / 3;
                    width = waWidth * 2 / 3;
                    break;
            }

            MoveWindowCompensated(hwnd, x, y, width, height);
        }
    }
}
