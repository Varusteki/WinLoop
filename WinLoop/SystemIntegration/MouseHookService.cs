using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Timers;

namespace WinLoop.SystemIntegration
{
    /// <summary>
    /// 全局鼠标钩子服务，用于监听鼠标中键按下/松开事件
    /// </summary>
    public class MouseHookService : IDisposable
    {
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelMouseProc _mouseProc;
        private Thread _hookThread;
        private bool _isDisposed = false;
        private volatile bool _isMiddleButtonDown = false; // volatile确保线程可见性
        private DateTime _middleButtonDownTime;
        private System.Timers.Timer _triggerTimer;
        private Point _middleButtonDownPosition;
        private readonly object _stateLock = new object(); // 状态锁

        public event Action<Point> MiddleButtonTriggered;
        public event Action MiddleButtonReleased;

        public int TriggerDelayMs { get; set; } = 200;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WH_MOUSE_LL = 14;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int HC_ACTION = 0;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public MouseHookService()
        {
            _mouseProc = HookCallback;
        }

        public void Start()
        {
            if (_hookId != IntPtr.Zero)
            {
                return; // 已启动
            }

            _hookThread = new Thread(() =>
            {
                try
                {
                    App.Log("Mouse hook thread starting...");
                    using (var curProcess = Process.GetCurrentProcess())
                    using (var curModule = curProcess.MainModule)
                    {
                        App.Log($"Module name: {curModule.ModuleName}");
                        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(curModule.ModuleName), 0);
                    }

                    if (_hookId == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        App.Log($"Failed to install mouse hook, error code: {error}");
                        return;
                    }

                    App.Log($"Mouse hook installed successfully, hookId={_hookId}");

                    // 消息循环
                    System.Windows.Forms.Application.Run();
                }
                catch (Exception ex)
                {
                    App.Log($"Mouse hook thread error: {ex.Message}");
                }
                finally
                {
                    if (_hookId != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(_hookId);
                        _hookId = IntPtr.Zero;
                    }
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "MouseHook线程"
            };

            _hookThread.Start();
        }

        public void Stop()
        {
            // 清理定时器
            if (_triggerTimer != null)
            {
                _triggerTimer.Stop();
                _triggerTimer.Dispose();
                _triggerTimer = null;
            }

            if (_hookThread != null && _hookThread.IsAlive)
            {
                System.Windows.Forms.Application.ExitThread();
                _hookThread.Join(1000);
            }

            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= HC_ACTION)
                {
                    int msg = wParam.ToInt32();
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    
                    // 只记录中键消息
                    if (msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP)
                    {
                        App.Log($"HookCallback: msg={msg:X}, position=({hookStruct.pt.X}, {hookStruct.pt.Y})");
                    }

                    if (msg == WM_MBUTTONDOWN)
                    {
                        App.Log($"WM_MBUTTONDOWN detected at ({hookStruct.pt.X}, {hookStruct.pt.Y})");
                        _isMiddleButtonDown = true;
                        _middleButtonDownTime = DateTime.Now;
                        _middleButtonDownPosition = new Point(hookStruct.pt.X, hookStruct.pt.Y);

                        // 停止并释放旧定时器
                        if (_triggerTimer != null)
                        {
                            _triggerTimer.Stop();
                            _triggerTimer.Dispose();
                            _triggerTimer = null;
                        }

                        // 每次都创建新的定时器，避免复用问题
                        App.Log($"Creating new trigger timer, delay={TriggerDelayMs}ms");
                        _triggerTimer = new System.Timers.Timer();
                        _triggerTimer.Interval = TriggerDelayMs;
                        _triggerTimer.AutoReset = false; // 只触发一次
                        _triggerTimer.Elapsed += (s, e) =>
                        {
                            App.Log($"Timer Elapsed fired, _isMiddleButtonDown={_isMiddleButtonDown}");
                            if (_isMiddleButtonDown)
                            {
                                App.Log($"Invoking MiddleButtonTriggered at {_middleButtonDownPosition}");
                                MiddleButtonTriggered?.Invoke(_middleButtonDownPosition);
                            }
                        };
                        App.Log("Starting timer...");
                        _triggerTimer.Start();
                        App.Log("Timer started");
                    }
                    else if (msg == WM_MBUTTONUP)
                    {
                        App.Log($"WM_MBUTTONUP detected, _isMiddleButtonDown={_isMiddleButtonDown}");
                        _triggerTimer?.Stop();
                        if (_isMiddleButtonDown)
                        {
                            _isMiddleButtonDown = false;
                            App.Log("Invoking MiddleButtonReleased event");
                            MiddleButtonReleased?.Invoke();
                            App.Log("MiddleButtonReleased event invoked");
                        }
                        else
                        {
                            App.Log("Skipping MiddleButtonReleased: _isMiddleButtonDown was false");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"HookCallback error: {ex.Message}");
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Stop();
                _isDisposed = true;
            }
        }
    }
}
