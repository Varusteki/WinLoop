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
        /// 移动窗口并补偿不可见边框，使窗口可见部分完全贴合目标区域
        /// </summary>
        private void MoveWindowCompensated(IntPtr hwnd, int x, int y, int width, int height)
        {
            // 先还原窗口以获取正确的边框信息
            ShowWindow(hwnd, SW_RESTORE);
            
            // 等待窗口状态更新
            System.Threading.Thread.Sleep(50);
            
            GetWindowFrameOffset(hwnd, out int leftOffset, out int topOffset, out int rightOffset, out int bottomOffset);
            
            // 补偿不可见边框：向外扩展窗口位置和大小
            int adjustedX = x - leftOffset;
            int adjustedY = y - topOffset;
            int adjustedWidth = width + leftOffset + rightOffset;
            int adjustedHeight = height + topOffset + bottomOffset;
            
            MoveWindow(hwnd, adjustedX, adjustedY, adjustedWidth, adjustedHeight, true);
        }

        public void ExecuteAction(WindowAction action)
        {
            try
            {
                App.Log($"Executing action: {action}");

                switch (action)
                {
                    case WindowAction.BackToDesktop:
                        ShowDesktop();
                        break;
                    case WindowAction.Minimize:
                        MinimizeCurrentWindow();
                        break;
                    case WindowAction.Maximize:
                        MaximizeCurrentWindow();
                        break;
                    case WindowAction.LeftHalf:
                        SetWindowToHalf(HalfPosition.Left);
                        break;
                    case WindowAction.RightHalf:
                        SetWindowToHalf(HalfPosition.Right);
                        break;
                    case WindowAction.TopHalf:
                        SetWindowToHalf(HalfPosition.Top);
                        break;
                    case WindowAction.BottomHalf:
                        SetWindowToHalf(HalfPosition.Bottom);
                        break;
                    case WindowAction.TopLeftQuadrant:
                        SetWindowToQuadrant(QuadrantPosition.TopLeft);
                        break;
                    case WindowAction.BottomLeftQuadrant:
                        SetWindowToQuadrant(QuadrantPosition.BottomLeft);
                        break;
                    case WindowAction.TopRightQuadrant:
                        SetWindowToQuadrant(QuadrantPosition.TopRight);
                        break;
                    case WindowAction.BottomRightQuadrant:
                        SetWindowToQuadrant(QuadrantPosition.BottomRight);
                        break;
                    case WindowAction.LeftTwoThirds:
                        SetWindowToTwoThirds(TwoThirdsPosition.Left);
                        break;
                    case WindowAction.RightTwoThirds:
                        SetWindowToTwoThirds(TwoThirdsPosition.Right);
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Log($"Error executing action {action}: {ex.Message}");
            }
        }

        private void ShowDesktop()
        {
            // 模拟按下 Win+D 快捷键
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event(VK_D, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void MinimizeCurrentWindow()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_MINIMIZE);
            }
        }

        private void MaximizeCurrentWindow()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_MAXIMIZE);
            }
        }

        private enum HalfPosition { Left, Right, Top, Bottom }
        private enum QuadrantPosition { TopLeft, TopRight, BottomLeft, BottomRight }
        private enum TwoThirdsPosition { Left, Right }

        private void SetWindowToHalf(HalfPosition position)
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            var workArea = SystemParameters.WorkArea;
            int x = 0, y = 0, width = 0, height = 0;

            switch (position)
            {
                case HalfPosition.Left:
                    x = (int)workArea.Left;
                    y = (int)workArea.Top;
                    width = (int)(workArea.Width / 2);
                    height = (int)workArea.Height;
                    break;
                case HalfPosition.Right:
                    x = (int)(workArea.Left + workArea.Width / 2);
                    y = (int)workArea.Top;
                    width = (int)(workArea.Width / 2);
                    height = (int)workArea.Height;
                    break;
                case HalfPosition.Top:
                    x = (int)workArea.Left;
                    y = (int)workArea.Top;
                    width = (int)workArea.Width;
                    height = (int)(workArea.Height / 2);
                    break;
                case HalfPosition.Bottom:
                    x = (int)workArea.Left;
                    y = (int)(workArea.Top + workArea.Height / 2);
                    width = (int)workArea.Width;
                    height = (int)(workArea.Height / 2);
                    break;
            }

            MoveWindowCompensated(hwnd, x, y, width, height);
        }

        private void SetWindowToQuadrant(QuadrantPosition position)
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            var workArea = SystemParameters.WorkArea;
            int x = 0, y = 0;
            int width = (int)(workArea.Width / 2);
            int height = (int)(workArea.Height / 2);

            switch (position)
            {
                case QuadrantPosition.TopLeft:
                    x = (int)workArea.Left;
                    y = (int)workArea.Top;
                    break;
                case QuadrantPosition.TopRight:
                    x = (int)(workArea.Left + workArea.Width / 2);
                    y = (int)workArea.Top;
                    break;
                case QuadrantPosition.BottomLeft:
                    x = (int)workArea.Left;
                    y = (int)(workArea.Top + workArea.Height / 2);
                    break;
                case QuadrantPosition.BottomRight:
                    x = (int)(workArea.Left + workArea.Width / 2);
                    y = (int)(workArea.Top + workArea.Height / 2);
                    break;
            }

            MoveWindowCompensated(hwnd, x, y, width, height);
        }

        private void SetWindowToTwoThirds(TwoThirdsPosition position)
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            var workArea = SystemParameters.WorkArea;
            int x = 0, y = (int)workArea.Top;
            int width = 0;
            int height = (int)workArea.Height;

            switch (position)
            {
                case TwoThirdsPosition.Left:
                    x = (int)workArea.Left;
                    width = (int)(workArea.Width * 2 / 3);
                    break;
                case TwoThirdsPosition.Right:
                    x = (int)(workArea.Left + workArea.Width / 3);
                    width = (int)(workArea.Width * 2 / 3);
                    break;
            }

            MoveWindowCompensated(hwnd, x, y, width, height);
        }
    }
}
