using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ShowSettings
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                // 用法：
                //   ShowSettings               -> 打开设置窗口
                //   ShowSettings color [hex]   -> 打开颜色选择器（用于渲染验证）
                string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "settings";
                string hex = args.Length > 1 ? args[1] : "#3A7BD5";

                // Try to locate WinLoop.dll by walking up parent directories
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string winloopDll = null;
                for (int i = 0; i < 12; i++)
                {
                    // detect repo root by presence of PRD.md (present at repo root)
                    var prdCheck = Path.GetFullPath(Path.Combine(dir, "PRD.md"));
                    if (File.Exists(prdCheck))
                    {
                        var candidate = Path.GetFullPath(Path.Combine(dir, "WinLoop", "bin", "Release", "netcoreapp3.1", "WinLoop.dll"));
                        if (File.Exists(candidate))
                        {
                            winloopDll = candidate;
                        }
                        break;
                    }
                    dir = Path.GetFullPath(Path.Combine(dir, ".."));
                    if (string.IsNullOrEmpty(dir) || dir == Path.GetPathRoot(dir)) break;
                }
                if (string.IsNullOrEmpty(winloopDll))
                {
                    Console.WriteLine("WinLoop.dll not found. Please build the WinLoop project first.");
                    return 2;
                }

                Console.WriteLine("Using WinLoop.dll at: " + winloopDll);
                Console.WriteLine("Mode: " + mode + "  hex: " + hex);

                var t = new Thread(() => RunWindow(winloopDll, mode, hex));
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex);
                return 1;
            }
        }

        static void RunWindow(string dllPath, string mode, string hex)
        {
            var asm = Assembly.LoadFrom(dllPath);

            var app = new Application();
            Window win = null;
            try
            {
                if (mode == "color")
                {
                    var t = asm.GetType("WinLoop.UI.ColorPickerWindow");
                    if (t == null)
                    {
                        Console.WriteLine("Cannot find WinLoop.UI.ColorPickerWindow type in assembly.");
                        return;
                    }
                    win = (Window)Activator.CreateInstance(t, new object[] { hex });
                }
                else
                {
                    var t = asm.GetType("WinLoop.UI.SettingsWindow");
                    if (t == null)
                    {
                        Console.WriteLine("Cannot find WinLoop.UI.SettingsWindow type in assembly.");
                        return;
                    }
                    win = (Window)Activator.CreateInstance(t);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to create window: " + ex);
                return;
            }

            // 作为独立顶层窗口展示（没有 Owner，所以 CenterOwner 会退回屏幕居中）
            win.ShowInTaskbar = true;
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            win.Title = mode == "color" ? "WinLoopColorPicker" : "WinLoopSettings";

            // 捕捉未处理异常，避免静默退出
            app.DispatcherUnhandledException += (s, e) =>
            {
                Console.WriteLine("DispatcherUnhandledException: " + e.Exception);
                e.Handled = true;
            };

            win.Loaded += (s, e) => Console.WriteLine("EVENT Loaded; IsVisible=" + win.IsVisible);
            win.Closed += (s, e) => Console.WriteLine("EVENT Closed");
            win.Activated += (s, e) => Console.WriteLine("EVENT Activated");
            app.Exit += (s, e) => Console.WriteLine("EVENT AppExit");

            Console.WriteLine("Calling app.Run...");
            // ShutdownMode 必须显式指定：ShowSettings 里没有 App.xaml 的默认值，
            // 若不写，Application 的默认是 OnLastWindowClose，但 Window 关闭后
            // 也可能因为 MainWindow 未被赋值而提前退出。显式绑定到 win。
            app.MainWindow = win;
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(win);
            Console.WriteLine("app.Run returned.");
        }
    }
}
