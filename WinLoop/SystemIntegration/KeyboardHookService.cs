using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WinLoop.Models;

namespace WinLoop.SystemIntegration
{
    /// <summary>
    /// 全局键盘钩子，用于检测目标按键的“双击”触发。
    /// </summary>
    public class KeyboardHookService : IDisposable
    {
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;
        private Thread _hookThread;
        private bool _disposed;
        private volatile bool _isTargetDown;
        private volatile bool _isEscDown;
        private long _lastTapMs;

        public event Action<XuanKongSiTriggerKey> HotkeyTriggered;
        public event Action HotkeyReleased;
        public event Action EscapePressed;

        /// <summary>
        /// Double-tap interval threshold in milliseconds.
        /// (Legacy name kept to avoid wider code changes.)
        /// </summary>
        public int HoldDelayMs { get; set; } = 300;

        public XuanKongSiTriggerKey TargetKey { get; set; } = XuanKongSiTriggerKey.LeftAlt;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public KeyboardHookService()
        {
            _proc = HookCallback;
        }

        public void Start()
        {
            if (_hookId != IntPtr.Zero)
            {
                return;
            }

            _hookThread = new Thread(() =>
            {
                try
                {
                    using (var curProcess = Process.GetCurrentProcess())
                    using (var curModule = curProcess.MainModule)
                    {
                        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                    }

                    if (_hookId == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        App.Log($"Failed to install keyboard hook, error code: {error}");
                        return;
                    }

                    System.Windows.Forms.Application.Run();
                }
                catch (Exception ex)
                {
                    App.Log($"Keyboard hook thread error: {ex.Message}");
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
                Name = "KeyboardHook线程"
            };

            _hookThread.Start();
        }

        public void Stop()
        {
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
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var key = (Keys)hookStruct.vkCode;

                // Double-tap the configured target key.
                if (IsTargetKey(key))
                {
                    if (isDown && !_isTargetDown)
                    {
                        _isTargetDown = true;
                        TryHandleTap(key);
                    }
                    else if (isUp)
                    {
                        _isTargetDown = false;
                        HotkeyReleased?.Invoke();
                    }
                }
                else if (key == Keys.Escape)
                {
                    if (isDown && !_isEscDown)
                    {
                        _isEscDown = true;
                        try { EscapePressed?.Invoke(); } catch { }
                    }
                    else if (isUp)
                    {
                        _isEscDown = false;
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void TryHandleTap(Keys pressedKey)
        {
            try
            {
                var interval = HoldDelayMs;
                if (interval < 120) interval = 120;
                if (interval > 1200) interval = 1200;

                var now = Environment.TickCount64;
                if (_lastTapMs != 0 && (now - _lastTapMs) <= interval)
                {
                    _lastTapMs = 0;
                    HotkeyTriggered?.Invoke(MapPressedKeyToTriggerKey(pressedKey));
                    return;
                }

                _lastTapMs = now;
            }
            catch { }
        }

        private bool IsTargetKey(Keys key)
        {
            switch (TargetKey)
            {
            case XuanKongSiTriggerKey.LeftCtrl: return key == Keys.LControlKey;
            case XuanKongSiTriggerKey.RightCtrl: return key == Keys.RControlKey;
            case XuanKongSiTriggerKey.LeftShift: return key == Keys.LShiftKey;
            case XuanKongSiTriggerKey.RightShift: return key == Keys.RShiftKey;
            case XuanKongSiTriggerKey.LeftAlt: return key == Keys.LMenu;
            case XuanKongSiTriggerKey.RightAlt: return key == Keys.RMenu;

            case XuanKongSiTriggerKey.Ctrl: return key == Keys.LControlKey || key == Keys.RControlKey;
            case XuanKongSiTriggerKey.Shift: return key == Keys.LShiftKey || key == Keys.RShiftKey;
            case XuanKongSiTriggerKey.Alt: return key == Keys.LMenu || key == Keys.RMenu;

                default:
                    return key == Keys.LMenu;
            }
        }

        private XuanKongSiTriggerKey MapPressedKeyToTriggerKey(Keys key)
        {
            switch (key)
            {
                case Keys.LControlKey: return XuanKongSiTriggerKey.LeftCtrl;
                case Keys.RControlKey: return XuanKongSiTriggerKey.RightCtrl;
                case Keys.LShiftKey: return XuanKongSiTriggerKey.LeftShift;
                case Keys.RShiftKey: return XuanKongSiTriggerKey.RightShift;
                case Keys.LMenu: return XuanKongSiTriggerKey.LeftAlt;
                case Keys.RMenu: return XuanKongSiTriggerKey.RightAlt;
                default:
                    return TargetKey;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }
    }
}
