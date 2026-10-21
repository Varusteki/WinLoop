using System;
using System.Collections.Generic;
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

        /// <summary>
        /// GetWindowPlacement 用的窗口位置结构。字段是 int 的显式布局，
        /// 不依赖 POINT 定义。showCmd 反映**当前**显示状态；
        /// rcNormalPosition 是窗口非最大化/最小化时的矩形。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int Length;
            public int Flags;
            public int ShowCmd;
            public int MinX;
            public int MinY;
            public int MaxX;
            public int MaxY;
            public int NormalLeft;
            public int NormalTop;
            public int NormalRight;
            public int NormalBottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsZoomed(IntPtr hWnd);

        /// <summary>showCmd 取值（WinUser.h）。GetWindowPlacement 返回的状态位。</summary>
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWMAXIMIZED = 3;
        private const int SW_SHOWMINNOACTIVE = 7;
        /// <summary>flags 位：窗口最小化时，若它最小化前是最大化的，置此位（还原后回到最大化）。</summary>
        private const int WPF_RESTORETOMAXIMIZED = 0x2;

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
        /// 取窗口的**可见外框**（DWM 扩展边框），单位**物理像素**。
        ///
        /// ⚠️ 为什么不能直接用 WPF 的 <c>Window.Left/ActualWidth</c> 去拼窗口：
        /// Win10/11 会给带标题栏的窗口在左 / 右 / 下留约 8px 的**不可见**阴影边框
        /// （拖拽热区），而 WPF 报出的 <c>Left/Top/Width/Height</c> 是**含它的外层矩形**。
        /// 于是"把 B 窗左边贴到 A 窗 <c>Left+ActualWidth</c>"算出来的位置，
        /// 视觉上会凭空多出一条 8~9px 的缝 —— 用户看到的"两个窗口没贴在一起"就是它。
        /// 要真正贴着 A 窗的**看得见的边**，只能问 DWM 要扩展边框。
        ///
        /// 注：本项目的窗口是 Per-Monitor V2，返回值是物理像素；
        /// 调用方若要用在 <c>Window.Left/Top</c>（DIP）上，需除以该屏的 DPI 缩放系数。
        /// 失败（DWM 被关闭 / 句柄无效）返回 false，调用方自行降级。
        /// </summary>
        public static bool TryGetVisibleFrameBounds(IntPtr hwnd, out Rect boundsPx)
        {
            boundsPx = Rect.Empty;
            if (hwnd == IntPtr.Zero) return false;

            try
            {
                RECT frameRect;
                int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS,
                    out frameRect, Marshal.SizeOf(typeof(RECT)));
                if (hr != 0) return false;

                boundsPx = new Rect(frameRect.Left, frameRect.Top,
                    frameRect.Right - frameRect.Left,
                    frameRect.Bottom - frameRect.Top);
                return boundsPx.Width > 0 && boundsPx.Height > 0;
            }
            catch (Exception ex)
            {
                App.Log("TryGetVisibleFrameBounds failed: " + ex.Message);
                return false;
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

                // 系统分屏改道：按住设置里的修饰键 + 动作可被 Win+方向键表达 →
                // 交给 Windows 自己的 snap（带动画/布局 flyout），WinLoop 不插手。
                if (TryInterceptSystemSnap(action, hwnd)) return;

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
                    case WindowAction.CloseWindow:
                        CloseWindow(hwnd);
                        break;
                    // 窗口居中：不改尺寸，只把窗口的可见部分挪到当前显示器工作区中心。
                    // 与 10 个尺寸类动作共用 ExecuteSnapToggle —— "再次触发还原原位置"
                    // 的开关语义对纯位移动作同样成立（原矩形 = 第一次触发前的位置）。
                    case WindowAction.Center:
                        ExecuteSnapToggle(action, hwnd, CenterWindow);
                        break;
                    // 以下 10 个尺寸类动作统一走 ExecuteSnapToggle：
                    // 同一窗口对同一动作第二次触发 = 还原第一次触发前的窗口矩形。
                    case WindowAction.LeftHalf:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToHalf(h, HalfPosition.Left));
                        break;
                    case WindowAction.RightHalf:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToHalf(h, HalfPosition.Right));
                        break;
                    case WindowAction.TopHalf:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToHalf(h, HalfPosition.Top));
                        break;
                    case WindowAction.BottomHalf:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToHalf(h, HalfPosition.Bottom));
                        break;
                    case WindowAction.TopLeftQuadrant:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToQuadrant(h, QuadrantPosition.TopLeft));
                        break;
                    case WindowAction.BottomLeftQuadrant:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToQuadrant(h, QuadrantPosition.BottomLeft));
                        break;
                    case WindowAction.TopRightQuadrant:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToQuadrant(h, QuadrantPosition.TopRight));
                        break;
                    case WindowAction.BottomRightQuadrant:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToQuadrant(h, QuadrantPosition.BottomRight));
                        break;
                    case WindowAction.LeftTwoThirds:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToTwoThirds(h, TwoThirdsPosition.Left));
                        break;
                    case WindowAction.RightTwoThirds:
                        ExecuteSnapToggle(action, hwnd, h => SetWindowToTwoThirds(h, TwoThirdsPosition.Right));
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

        // ==================== 尺寸类动作的"开关语义" ====================
        //
        // 需求（2026-10-21 用户提出）：涉及窗口尺寸修改的动作（左半屏、左三分之二屏
        // 等），第一次按当前逻辑触发；对同一个窗口再次选择**同一个菜单**时，
        // 还原窗口原来的尺寸与位置。
        //
        // 按 (动作, 窗口句柄) 双键存锚点：左半屏记左半屏的原点，三分之二屏记
        // 三分之二屏的原点，互不打扰 —— "同一个菜单"才是还原条件，换了菜单就是
        // 那个新菜单的第一次，照常 snap。
        //
        // ⚠️ 为什么必须存进程号：HWND 会被系统回收复用。窗口 A 关闭后，新开的
        // 窗口 B 可能拿到同一个句柄。只比句柄会把 B 的第一次触发误判成"还原"，
        // 把 B 跳到 A 生前的矩形去。带上进程号 + IsWindow 双校验才分得清。
        private readonly object _anchorLock = new object();
        private readonly Dictionary<(WindowAction Action, IntPtr HWnd), SnapAnchor> _snapAnchors =
            new Dictionary<(WindowAction, IntPtr), SnapAnchor>();

        private sealed class SnapAnchor
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public uint ProcessId;
            /// <summary>触发前的窗口显示状态（GetWindowPlacement.showCmd）：
            /// 最大化/最小化态只存矩形不够，还原时要把状态也放回去，
            /// 否则最大化的窗口 snap 后再还原只会变成普通态矩形
            /// （= 用户报的"恢复成了最大化前的状态"）。</summary>
            public int ShowCmd;
            /// <summary>最小化前是最大化的窗口，placement.flags 带 WPF_RESTORETOMAXIMIZED，
            /// 此时 ShowCmd 是 SW_SHOWMINIMIZED，但用户心里的"原状态"是最大化。</summary>
            public bool RestoreToMaximized;
        }

        /// <summary>
        /// 尺寸类动作的统一入口：
        ///   第一次触发 → 先把当前窗口矩形存档，再走原 snap 逻辑；
        ///   第二次触发 → 还原存档矩形并清档（第三次再 snap，如此往复）。
        /// </summary>
        /// <param name="snap">原 snap 逻辑（SetWindowToHalf / Quadrant / TwoThirds）</param>
        private void ExecuteSnapToggle(WindowAction action, IntPtr hwnd, Action<IntPtr> snap)
        {
            if (hwnd == IntPtr.Zero)
            {
                snap(hwnd);   // 交给下游判空，与旧行为一致
                return;
            }

            bool restore;
            SnapAnchor anchor = null;

            // 表操作才需要锁；真正动窗口（P/Invoke）放在锁外，避免长时间持锁。
            lock (_anchorLock)
            {
                PruneDeadAnchors();

                var key = (action, hwnd);
                if (_snapAnchors.TryGetValue(key, out var hit))
                {
                    _snapAnchors.Remove(key);
                    anchor = hit;
                    restore = true;
                }
                else
                {
                    // ⚠️ 必须在 SW_RESTORE **之前**取 placement：SW_RESTORE 会把
                    // showCmd 从"最大化/最小化"改写成普通态，之后再取就录不到
                    // 用户原本的窗口状态了（本次 bug 的根因）。
                    int showCmd = SW_SHOWNORMAL;
                    bool restoreToMax = false;
                    var wp = new WINDOWPLACEMENT { Length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
                    if (GetWindowPlacement(hwnd, ref wp))
                    {
                        showCmd = wp.ShowCmd;
                        restoreToMax = (wp.Flags & WPF_RESTORETOMAXIMIZED) != 0;
                    }

                    // ⚠️ 先 SW_RESTORE 再取矩形：最小化窗口的 GetWindowRect 拿到的是
                    // (-32000,-32000) 这种垃圾坐标，最大化窗口拿到的是撑满屏幕的
                    // 临时矩形 —— 都不是"用户原来的位置"，照存的话还原时会跳到
                    // 莫名其妙的地方。SW_RESTORE 同步生效，之后 snap 逻辑里还有
                    // 一次 SW_RESTORE，幂等无害。
                    ShowWindow(hwnd, SW_RESTORE);
                    if (GetWindowRect(hwnd, out RECT r))
                    {
                        GetWindowThreadProcessId(hwnd, out uint pid);
                        _snapAnchors[key] = new SnapAnchor
                        {
                            Left = r.Left,
                            Top = r.Top,
                            Right = r.Right,
                            Bottom = r.Bottom,
                            ProcessId = pid,
                            ShowCmd = showCmd,
                            RestoreToMaximized = restoreToMax
                        };
                        App.Log($"Snap anchor saved: {action}, hwnd={hwnd}, " +
                                $"rect=({r.Left},{r.Top})-({r.Right},{r.Bottom}), pid={pid}, showCmd={showCmd}");
                    }
                    restore = false;
                }
            }

            if (restore && anchor != null)
            {
                // ⚠️ 还原用**裸 SetWindowPos + 原始 WindowRect**，不走
                // MoveWindowCompensated —— 存的是 WindowRect、还原的也是 WindowRect，
                // 原样往返恒等；再叠一层不可见边框补偿反而会整体偏移。
                // SW_RESTORE 保证当前处于最大化/最小化态时 SetWindowPos 仍生效。
                ShowWindow(hwnd, SW_RESTORE);
                SetWindowPos(hwnd, IntPtr.Zero, anchor.Left, anchor.Top,
                    anchor.Right - anchor.Left, anchor.Bottom - anchor.Top,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);

                // ⚠️ 状态也要还原：原本是最大化的窗口，只摆矩形用户看到的是
                // "最大化前的样子"。先摆矩形再切状态 —— 反序的话 SW_MAXIMIZE
                // 会把刚摆的矩形覆盖成屏幕工作区，白摆。
                // 顺序细节：最小化放最后且不等回信，最小化本身就是异步生效的。
                bool wasMaximized = anchor.ShowCmd == SW_SHOWMAXIMIZED
                                    || (anchor.RestoreToMaximized && anchor.ShowCmd == SW_SHOWMINIMIZED);
                if (wasMaximized)
                {
                    ShowWindow(hwnd, SW_MAXIMIZE);
                }
                else if (anchor.ShowCmd == SW_SHOWMINIMIZED || anchor.ShowCmd == SW_SHOWMINNOACTIVE)
                {
                    ShowWindow(hwnd, SW_SHOWMINIMIZED);
                }

                App.Log($"Snap restored: {action}, hwnd={hwnd}, " +
                        $"rect=({anchor.Left},{anchor.Top})-({anchor.Right},{anchor.Bottom}), showCmd={anchor.ShowCmd}");
                return;
            }

            snap(hwnd);
        }

        /// <summary>
        /// 清理失效锚点：窗口已死（IsWindow=false），或句柄被别的进程复用
        /// （当年的窗口早关了，现在这个句柄属于新窗口 —— 新窗口的"第一次"
        /// 不该被当成还原）。在 _anchorLock 内调用。
        /// </summary>
        private void PruneDeadAnchors()
        {
            if (_snapAnchors.Count == 0) return;

            var dead = new List<(WindowAction, IntPtr)>();
            foreach (var kv in _snapAnchors)
            {
                if (!IsWindow(kv.Key.HWnd)) { dead.Add(kv.Key); continue; }
                GetWindowThreadProcessId(kv.Key.HWnd, out uint pid);
                if (pid != kv.Value.ProcessId) dead.Add(kv.Key);
            }
            foreach (var k in dead) _snapAnchors.Remove(k);
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

        /// <summary>取创建窗口的进程号。HWND 会被系统回收复用，
        /// 光比句柄分不清"同一个窗口"还是"句柄撞了"，必须再问一次进程。</summary>
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>
        /// 取窗口矩形（WindowRect，物理像素）。供自检直接验证"snap → 还原"
        /// 的往返是否精确，不用在自检里再复制一份 P/Invoke。
        /// </summary>
        internal static bool TryGetWindowRect(IntPtr hwnd, out int left, out int top, out int right, out int bottom)
        {
            left = 0; top = 0; right = 0; bottom = 0;
            if (hwnd == IntPtr.Zero) return false;
            if (!GetWindowRect(hwnd, out RECT r)) return false;
            left = r.Left; top = r.Top; right = r.Right; bottom = r.Bottom;
            return true;
        }

        /// <summary>
        /// 窗口是否处于最大化态。供自检断言"原状态是最大化的窗口，
        /// 还原后必须还是最大化"（rectangle 对但状态丢了就是用户报的 bug）。
        /// </summary>
        internal static bool IsWindowZoomed(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            return IsZoomed(hwnd);
        }

        /// <summary>
        /// 窗口所在显示器的工作区（物理像素，已排除任务栏）。供自检验证
        /// 「窗口居中」的目标中心 —— 只断"矩形变了"抓不住"居中的参照系错了"
        /// （比如算到主屏中心去了），必须是"当前显示器的工作区中心"。
        /// 实例方法（TryGetWorkArea 是实例方法），自检用 svc 实例调用。
        /// </summary>
        internal bool TryGetWorkAreaOf(IntPtr hwnd, out int left, out int top, out int width, out int height)
        {
            return TryGetWorkArea(hwnd, out left, out top, out width, out height);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;

        private const uint WM_SYSCOMMAND = 0x0112;
        private static readonly IntPtr SC_CLOSE = new IntPtr(0xF060);

        private const int SW_SHOWMINIMIZED_ = 2;
        private const int SW_SHOWNORMAL_ = 1;

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
            // 与 MaximizeWindow 的实现保持一致。
            ShowWindowAsync(hwnd, SW_MINIMIZE);
        }

        private void MaximizeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, SW_MAXIMIZE);
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

        /// <summary>
        /// 「系统分屏修饰键」（设置项，默认左 Alt）：执行动作时按住该键，
        /// 且动作能被 Windows 系统分屏表达时，改走 Win+方向键的系统 snap。
        /// 由 App.InitializeCoreServices / App.UpdateSystemSnapModifier 从配置推送。
        /// </summary>
        public SnapModifierKey SystemSnapModifierKey { get; set; } = SnapModifierKey.LeftAlt;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // 异步键态虚拟键码（WinUser.h）。修饰键选项 6 个各自独立检测，
        // 不能用"Shift 按下"这种统称 —— 用户明确选了左右之分。
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_LEFT = 0x25;
        private const int VK_RIGHT = 0x27;

        private static byte ModifierVkOf(SnapModifierKey key)
        {
            switch (key)
            {
                case SnapModifierKey.LeftShift: return VK_LSHIFT;
                case SnapModifierKey.RightShift: return VK_RSHIFT;
                case SnapModifierKey.LeftCtrl: return VK_LCONTROL;
                case SnapModifierKey.RightCtrl: return VK_RCONTROL;
                case SnapModifierKey.LeftAlt: return VK_LMENU;
                case SnapModifierKey.RightAlt: return VK_RMENU;
                default: return VK_LMENU;   // 未知名按默认键处理，不开静默失效的后门
            }
        }

        /// <summary>
        /// WinLoop 动作 → 系统 snap（Win+方向键）映射表。
        /// <para>
        /// **入表标准**：Windows 自带分屏能用方向键稳定表达的动作才入表。
        /// 依据（Win10 / Win11 均适用的公共子集）：
        /// ① 半屏 = 方向键按 1 次（Win+← 左半、Win+→ 右半）；
        /// ② 四象限 = **同方向连按**的循环位（Win10：左半→左上→左下；
        ///    Win11 保留同一循环）—— 左 ×2=左上、左 ×3=左下，右侧同理；
        /// ③ 连按间隔 110ms：系统要把上一次 snap 处理完才认得出"又一次循环"，
        ///    0ms 连按会被合并成一次（症状：想要的象限变成半屏）。间隔取 110ms
        ///    是经验值下限（真人手动连按约 150-300ms），再低有合并风险。
        /// </para>
        /// <para>
        /// **刻意不入表**的动作（没有等价系统行为，按住修饰键也走 WinLoop 原逻辑）：
        /// · 上半屏/下半屏 —— Win+↑ 是最大化、Win+↓ 是还原/最小化，语义不等价；
        /// · 三分之二屏 —— 无纯按键路径（Win11 要走 snap layouts flyout 交互）；
        /// · 窗口居中/最小化/最大化/关闭/回到桌面 —— 与分屏无关。
        /// </para>
        /// </summary>
        private static readonly (WindowAction Action, byte DirKey, int Repeat)[] _systemSnapMaps =
        {
            (WindowAction.LeftHalf,            VK_LEFT,  1),
            (WindowAction.RightHalf,           VK_RIGHT, 1),
            (WindowAction.TopLeftQuadrant,     VK_LEFT,  2),
            (WindowAction.BottomLeftQuadrant,  VK_LEFT,  3),
            (WindowAction.TopRightQuadrant,    VK_RIGHT, 2),
            (WindowAction.BottomRightQuadrant, VK_RIGHT, 3),
        };

        /// <summary>
        /// 动作能否表达成系统 snap；能则给出方向键与重复次数。
        /// 纯查表、无副作用 —— 自检直接用它验证映射表，不发键。
        /// </summary>
        internal static bool TryGetSystemSnapKeys(WindowAction action, out byte dirKey, out int repeat)
        {
            foreach (var m in _systemSnapMaps)
            {
                if (m.Action == action)
                {
                    dirKey = m.DirKey;
                    repeat = m.Repeat;
                    return true;
                }
            }
            dirKey = 0;
            repeat = 0;
            return false;
        }

        /// <summary>
        /// 系统修饰键当前是否按住。用 GetAsyncKeyState（物理键态）而不是钩子队列：
        /// 执行动作发生在菜单**选中那一刻**，全局键盘钩子的事件时机与这里对不上，
        /// 而异步键态就是"此刻这个键按没按"，不受焦点归属影响。
        /// </summary>
        private bool IsSnapModifierDown()
        {
            short state = GetAsyncKeyState(ModifierVkOf(SystemSnapModifierKey));
            return (state & 0x8000) != 0;
        }

        /// <summary>
        /// 尝试把动作改道成系统分屏。返回 true 表示已拦截（调用方应直接 return）。
        /// </summary>
        private bool TryInterceptSystemSnap(WindowAction action, IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (!TryGetSystemSnapKeys(action, out byte dirKey, out int repeat)) return false;
            if (!IsSnapModifierDown()) return false;

            // ⚠️ 守卫：Win+方向键作用于**发键时的前台窗口**。前台不是目标窗口就放行，
            // 交回 WinLoop 自己的 snap 逻辑。这里刻意不 SetForegroundWindow 抢焦点 ——
            // 前台锁（ForegroundLockTimeout）会拒，且用户按住修饰键时就没打算换焦点。
            IntPtr fg = GetForegroundWindow();
            if (fg != hwnd)
            {
                App.Log($"System snap skipped (foreground mismatch): hwnd={hwnd}, fg={fg}");
                return false;
            }

            App.Log($"System snap intercept: {action}, dir=0x{dirKey:X2}, repeat={repeat}, hwnd={hwnd}");
            ExecuteSystemSnap(dirKey, repeat);
            return true;
        }

        /// <summary>
        /// 发 Win+方向键（可重复）。keybd_event 与 ShowDesktop 的 Win+D 同一手法，
        /// 项目里已验证可用。重复之间 Sleep 给系统处理上一次 snap 的时间，
        /// 否则连按被合并（见 _systemSnapMaps 注释 ③）。
        /// <para>
        /// ⚠️ 先物理释放用户按住的修饰键、发完再按回去 —— 这是功能能否成立的生死线：
        /// 用户按住左 Alt 触发菜单，发 Win+← 时 Alt 仍按着，组合变成 Alt+Win+←，
        /// Win10/11 都不认这个组合（Alt 会改变 shell 的快捷键语义）；若修饰键是
        /// Shift 更糟 —— Shift+Win+← 是"把窗口移到副显示器"，功能直接跑偏。
        /// 释放/恢复用裸 keybd_event 不带 OEM 扫描码：此刻用户手指没抬，
        /// 恢复后物理键态无缝续上，期间 ≤ 重复次数 × 110ms，无感知。
        /// </para>
        /// </summary>
        private void ExecuteSystemSnap(byte dirKey, int repeat)
        {
            byte modVk = ModifierVkOf(SystemSnapModifierKey);
            // 没按住修饰键时释放/恢复是空操作；按住时先松开，final 里按回。
            keybd_event(modVk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            try
            {
                for (int i = 0; i < repeat; i++)
                {
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                    keybd_event((byte)dirKey, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                    keybd_event((byte)dirKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    if (i < repeat - 1) System.Threading.Thread.Sleep(110);
                }
            }
            finally
            {
                keybd_event(modVk, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            }
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

        /// <summary>
        /// 窗口居中：**只挪位置，不改尺寸**。
        /// </summary>
        /// <remarks>
        /// 实现要点：
        /// ① 目标位置按**当前显示器的工作区**（rcWork，已排除任务栏）算中心，
        ///    不是主屏也不是 SystemParameters.WorkArea —— Per-Monitor V2 +
        ///    非 100% 缩放下，逻辑单位与 SetWindowPos 的物理像素会错位，
        ///    项目里统一走 <see cref="GetWorkArea"/>（MonitorFromWindow 物理像素）。
        /// ② 窗口宽高取**当前值**原样回传：需求明确"不修改窗口尺寸"。
        ///    注意最大化态窗口撑满工作区，SW_RESTORE 后拿到的才是用户的原尺寸 ——
        ///    这条由 ExecuteSnapToggle 存档前的 SW_RESTORE 保证，本方法拿到的
        ///    已是正常态矩形。
        /// ③ 走 <see cref="MoveWindowCompensated"/>：与 10 个 snap 动作同一通道，
        ///    不可见边框补偿让**可见部分**居中（否则 Win10/11 上视觉上偏右偏下）。
        /// </remarks>
        private void CenterWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            // 当前窗口矩形（WindowRect）。执行到这里时调用方已 SW_RESTORE 过，
            // 不会拿到最大化态的临时满屏矩形。
            if (!GetWindowRect(hwnd, out RECT r)) return;
            int width = r.Right - r.Left;
            int height = r.Bottom - r.Top;

            GetWorkArea(hwnd, out int waLeft, out int waTop, out int waWidth, out int waHeight);

            // 工作区中心减去半个窗口 = 左上角目标。整数除法向零取整无妨，
            // 奇数尺寸时偏 1px 肉眼不可见。
            int x = waLeft + (waWidth - width) / 2;
            int y = waTop + (waHeight - height) / 2;

            MoveWindowCompensated(hwnd, x, y, width, height);
        }
    }
}
