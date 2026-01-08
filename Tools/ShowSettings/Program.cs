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

                var t = new Thread(() => RunWindow(winloopDll));
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

        static void RunWindow(string dllPath)
        {
            var asm = Assembly.LoadFrom(dllPath);
            var winType = asm.GetType("WinLoop.UI.SettingsWindow");
            if (winType == null)
            {
                Console.WriteLine("Cannot find WinLoop.UI.SettingsWindow type in assembly.");
                return;
            }

            var app = new Application();
            Window win = null;
            try
            {
                win = (Window)Activator.CreateInstance(winType);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to create SettingsWindow: " + ex);
                return;
            }

            // No preview interception: run the Settings window as-built.

            // No interception here; run the Settings window normally.

            // Keep window open until user closes it
            app.Run(win);
        }
    }
}
