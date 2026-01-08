using System;
using System.Runtime.InteropServices;
using WinLoop.Models;

namespace WinLoop.Core
{
    public static class WindowActionExecutor
    {
        // Windows API常量
        private const int SW_SHOWMAXIMIZED = 3;
        private const int SW_SHOWMINIMIZED = 2;
        private const int SW_RESTORE = 9;
        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZE = 0x01000000;
        private const int WS_MINIMIZE = 0x20000000;
        private const uint WM_SYSCOMMAND = 0x0112;
        private const uint SC_MAXIMIZE = 0xF030;
        private const uint SC_MINIMIZE = 0xF020;
        private const uint SC_RESTORE = 0xF120;
        
        // Windows API函数
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern bool CloseWindow(IntPtr hWnd);
        
        // 系统指标常量
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        
        public static void Execute(WindowAction action)
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return;
            }
            
            switch (action)
            {
                case WindowAction.Maximize:
                    ShowWindow(foregroundWindow, SW_SHOWMAXIMIZED);
                    break;
                case WindowAction.Minimize:
                    ShowWindow(foregroundWindow, SW_SHOWMINIMIZED);
                    break;
                case WindowAction.TopLeftQuadrant:
                    SplitScreen(foregroundWindow, 0, 0, 0.5, 0.5);
                    break;
                case WindowAction.TopRightQuadrant:
                    SplitScreen(foregroundWindow, 0.5, 0, 0.5, 0.5);
                    break;
                case WindowAction.BottomLeftQuadrant:
                    SplitScreen(foregroundWindow, 0, 0.5, 0.5, 0.5);
                    break;
                case WindowAction.BottomRightQuadrant:
                    SplitScreen(foregroundWindow, 0.5, 0.5, 0.5, 0.5);
                    break;
                case WindowAction.TopHalf:
                    SplitScreen(foregroundWindow, 0, 0, 1, 0.5);
                    break;
                case WindowAction.BottomHalf:
                    SplitScreen(foregroundWindow, 0, 0.5, 1, 0.5);
                    break;
                case WindowAction.LeftHalf:
                    SplitScreen(foregroundWindow, 0, 0, 0.5, 1);
                    break;
                case WindowAction.RightHalf:
                    SplitScreen(foregroundWindow, 0.5, 0, 0.5, 1);
                    break;
            }
        }
        
        private static void SplitScreen(IntPtr hWnd, double leftRatio, double topRatio, double widthRatio, double heightRatio)
        {
            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);
            
            int left = (int)(screenWidth * leftRatio);
            int top = (int)(screenHeight * topRatio);
            int width = (int)(screenWidth * widthRatio);
            int height = (int)(screenHeight * heightRatio);
            
            MoveWindow(hWnd, left, top, width, height, true);
        }
    }
}