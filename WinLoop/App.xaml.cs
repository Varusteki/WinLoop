using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using System.Text;
using WinLoop.Models;
// using System.Windows.Forms; (fully-qualify WinForms types to avoid conflicts with WPF)

namespace WinLoop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static string _logFilePath;
        private static App _instance;
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private SystemIntegration.MouseHookService _mouseHookService;
        private SystemIntegration.WindowManagementService _windowManagementService;
        private UI.MenuOverlayWindow _menuOverlayWindow;
        private SystemIntegration.KeyboardHookService _keyboardHookService;
        private UI.XuanKongSiOverlayWindow _xuanKongSiOverlayWindow;
        private XuanKongSiConfig _xuanKongSiConfig;
        
        /// <summary>
        /// 更新鼠标钩子服务的触发时长
        /// </summary>
        public static void UpdateTriggerDelay(int delayMs)
        {
            if (_instance?._mouseHookService != null)
            {
                _instance._mouseHookService.TriggerDelayMs = delayMs;
                Log($"TriggerDelayMs updated to {delayMs}");
            }
        }

        public static void UpdateXuanKongSi(XuanKongSiConfig config)
        {
            if (_instance == null) return;

            _instance._xuanKongSiConfig = config ?? new XuanKongSiConfig();

            if (_instance._keyboardHookService != null)
            {
                _instance._keyboardHookService.HoldDelayMs = _instance._xuanKongSiConfig.HoldDurationMs;
                _instance._keyboardHookService.TargetKey = _instance._xuanKongSiConfig.TriggerKey;
            }

            Log($"XuanKongSi updated: enabled={_instance._xuanKongSiConfig.Enabled}, trigger={_instance._xuanKongSiConfig.TriggerKey}, hold={_instance._xuanKongSiConfig.HoldDurationMs}ms, content={_instance._xuanKongSiConfig.ContentType}");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _instance = this;

            // 标记处于启动阶段，SettingsWindow 在此阶段应被抑制（以避免启动时自动弹出）
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.Properties["SuppressSettingsOnStartup"] = true;
                    Log("Set SuppressSettingsOnStartup = true");
                }
            }
            catch { }
            // 检查是否已有相同进程在运行（排除当前进程），若存在提示用户关闭或重试
            try
            {
                var cur = Process.GetCurrentProcess();
                while (true)
                {
                    var others = Process.GetProcessesByName(cur.ProcessName).Where(p => p.Id != cur.Id).ToArray();
                    if (others.Length == 0) break;

                    var sb = new StringBuilder();
                    foreach (var p in others)
                    {
                        try { sb.AppendLine($"PID {p.Id} - Started: {p.StartTime}"); } catch { sb.AppendLine($"PID {p.Id}"); }
                    }
                    // 记录检测到的已有实例，以便在无交互环境也能观测到该事件
                    Log("Existing instance(s) detected:\n" + sb.ToString());

                        var msg = $"检测到另一个 WinLoop 进程正在运行:\n{sb}\n请先关闭原有进程后再运行。\n(点击 [确定] 重新检测 或 [取消] 退出应用)";
                        var r = MessageBox.Show(msg, "WinLoop 已在运行", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                        if (r == MessageBoxResult.OK)
                    {
                        // 等待一会再重试
                        System.Threading.Thread.Sleep(800);
                        continue;
                    }
                    else
                    {
                        // 退出应用
                        Shutdown();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // 如果检测过程出错，记录日志并继续启动
                Log("Process check error: " + ex.Message);
            }
            
            // 设置日志文件路径
            _logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinLoop", "log.txt");
            
            // 确保日志目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath));
            
            // 写入启动日志
            Log("Application starting...");
            
            // 添加全局异常处理
            this.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;

            try
            {
                // 创建托盘图标（从嵌入资源加载）
                _notifyIcon = new System.Windows.Forms.NotifyIcon();
                
                // 尝试从嵌入资源加载图标
                try
                {
                    var resourceStream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/trayIcon.ico"));
                    if (resourceStream != null)
                    {
                        _notifyIcon.Icon = new System.Drawing.Icon(resourceStream.Stream);
                    }
                    else
                    {
                        _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                    }
                }
                catch
                {
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                }
                _notifyIcon.Text = "WinLoop";

                // 添加右键菜单
                var cms = new System.Windows.Forms.ContextMenuStrip();
                var openItem = new System.Windows.Forms.ToolStripMenuItem("打开设置");
                openItem.Click += (s, args) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var win = new UI.SettingsWindow();
                            win.Show();
                            win.Activate();
                        }
                        catch (Exception ex)
                        {
                            Log("Tray open settings error: " + ex.Message);
                        }
                    });
                };
                var exitItem = new System.Windows.Forms.ToolStripMenuItem("退出");
                exitItem.Click += (s, args) =>
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
                };
                cms.Items.Add(openItem);
                cms.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                cms.Items.Add(exitItem);
                _notifyIcon.ContextMenuStrip = cms;

                _notifyIcon.Visible = true;
                _notifyIcon.DoubleClick += (s, args) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var win = new UI.SettingsWindow();
                            win.Show();
                            win.Activate();
                        }
                        catch (Exception ex)
                        {
                            Log("Tray double-click error: " + ex.Message);
                        }
                    });
                };
            }
            catch (Exception ex)
            {
                Log("Tray init error: " + ex.Message);
            }

            // 初始化核心服务
            try
            {
                _windowManagementService = new SystemIntegration.WindowManagementService();
                _mouseHookService = new SystemIntegration.MouseHookService();
                _keyboardHookService = new SystemIntegration.KeyboardHookService();
                
                // 加载配置获取触发时长
                var configMgr = new Config.ConfigManager();
                var config = configMgr.LoadConfig();
                _mouseHookService.TriggerDelayMs = config.TriggerDelay;
                _xuanKongSiConfig = config.XuanKongSi ?? new XuanKongSiConfig();
                config.XuanKongSi = _xuanKongSiConfig;
                _keyboardHookService.HoldDelayMs = _xuanKongSiConfig.HoldDurationMs;
                _keyboardHookService.TargetKey = _xuanKongSiConfig.TriggerKey;

                // 应用自启动设置
                SystemIntegration.AutoStartManager.ApplyAutoStartSetting(config.AutoStart);
                Log($"Auto-start setting applied: {config.AutoStart}");

                // 订阅事件
                _mouseHookService.MiddleButtonTriggered += OnMiddleButtonTriggered;
                _mouseHookService.MiddleButtonReleased += OnMiddleButtonReleased;
                _keyboardHookService.HotkeyTriggered += OnXuanKongSiTriggered;
                _keyboardHookService.EscapePressed += OnXuanKongSiEscape;

                // 启动鼠标钩子
                _mouseHookService.Start();
                _keyboardHookService.Start();
                Log("Core services initialized and mouse hook started");
                
                // 启动完成后，允许设置窗口正常显示
                if (Application.Current != null)
                {
                    Application.Current.Properties["SuppressSettingsOnStartup"] = false;
                    Log("Set SuppressSettingsOnStartup = false (startup complete)");
                }
            }
            catch (Exception ex)
            {
                Log($"Core services init error: {ex.Message}");
            }
        }

        private void OnMiddleButtonTriggered(Point position)
        {
            try
            {
                // 确保在 UI 线程中执行
                Dispatcher.Invoke(() =>
                {
                    Log($"Middle button triggered at {position}");
                    
                    // 每次都关闭旧窗口并创建新窗口，彻底避免闪烁
                    if (_menuOverlayWindow != null)
                    {
                        Log("Closing old MenuOverlayWindow");
                        _menuOverlayWindow.ActionSelected -= OnActionSelected;
                        _menuOverlayWindow.Close();
                        _menuOverlayWindow = null;
                    }
                    
                    Log("Creating new MenuOverlayWindow");
                    _menuOverlayWindow = new UI.MenuOverlayWindow();
                    _menuOverlayWindow.ActionSelected += OnActionSelected;
                    Log("MenuOverlayWindow created and event subscribed");

                    Log($"Calling ShowAt({position.X}, {position.Y})");
                    _menuOverlayWindow.ShowAt(position);
                    Log("ShowAt completed");
                });
            }
            catch (Exception ex)
            {
                Log($"OnMiddleButtonTriggered error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnMiddleButtonReleased()
        {
            try
            {
                // 确保在 UI 线程中执行
                Dispatcher.Invoke(() =>
                {
                    Log($"Middle button released, _menuOverlayWindow={_menuOverlayWindow != null}, IsVisible={_menuOverlayWindow?.IsVisible}");
                    
                    if (_menuOverlayWindow != null && _menuOverlayWindow.IsVisible)
                    {
                        Log("Calling ExecuteAction");
                        _menuOverlayWindow.ExecuteAction();
                        Log("ExecuteAction completed");
                    }
                    else
                    {
                        Log("Skipping ExecuteAction: window not visible");
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"OnMiddleButtonReleased error: {ex.Message}");
            }
        }

        private void OnXuanKongSiTriggered(XuanKongSiTriggerKey triggerKey)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (_xuanKongSiConfig?.Enabled != true)
                    {
                        return;
                    }

                    // If already visible, ignore (hide is handled by ESC).
                    if (_xuanKongSiOverlayWindow != null && _xuanKongSiOverlayWindow.IsVisible)
                    {
                        return;
                    }

                    _xuanKongSiOverlayWindow?.Close();
                    _xuanKongSiOverlayWindow = new UI.XuanKongSiOverlayWindow();

                    var cfg = _xuanKongSiConfig ?? new XuanKongSiConfig();
                    var showCfg = new XuanKongSiConfig
                    {
                        Enabled = cfg.Enabled,
                        Scheme = cfg.Scheme,
                        HoldDurationMs = cfg.HoldDurationMs,
                        TriggerKey = triggerKey,
                        ContentType = cfg.ContentType,
                        TextXaml = cfg.TextXaml,
                        ImageFileName = cfg.ImageFileName,
                        WebUrl = cfg.WebUrl
                    };

                    _xuanKongSiOverlayWindow.ShowLayout(showCfg);
                });
            }
            catch (Exception ex)
            {
                Log($"OnXuanKongSiTriggered error: {ex.Message}");
            }
        }

        private void OnXuanKongSiEscape()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (_xuanKongSiOverlayWindow != null && _xuanKongSiOverlayWindow.IsVisible)
                    {
                        HideXuanKongSiOverlay();
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"OnXuanKongSiEscape error: {ex.Message}");
            }
        }

        private void HideXuanKongSiOverlay()
        {
            var win = _xuanKongSiOverlayWindow;
            if (win == null) return;

            // Clear reference only after hide animation completes.
            win.HiddenCompleted += (s, e) =>
            {
                try
                {
                    if (ReferenceEquals(_xuanKongSiOverlayWindow, win))
                    {
                        _xuanKongSiOverlayWindow = null;
                    }
                }
                catch { }
            };

            win.HideOverlayAnimated();
        }

        private void OnXuanKongSiReleased()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    var win = _xuanKongSiOverlayWindow;
                    if (win == null) return;

                    // Clear reference only after hide animation completes.
                    win.HiddenCompleted += (s, e) =>
                    {
                        try
                        {
                            if (ReferenceEquals(_xuanKongSiOverlayWindow, win))
                            {
                                _xuanKongSiOverlayWindow = null;
                            }
                        }
                        catch { }
                    };

                    win.HideOverlayAnimated();
                });
            }
            catch (Exception ex)
            {
                Log($"OnXuanKongSiReleased error: {ex.Message}");
            }
        }

        private void OnActionSelected(Models.WindowAction action)
        {
            try
            {
                Log($"Action selected: {action}");
                _windowManagementService?.ExecuteAction(action);
            }
            catch (Exception ex)
            {
                Log($"OnActionSelected error: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // 清理核心服务
                _mouseHookService?.Stop();
                _mouseHookService?.Dispose();
                _keyboardHookService?.Stop();
                _keyboardHookService?.Dispose();
                _menuOverlayWindow?.Close();
                _xuanKongSiOverlayWindow?.Close();
                Log("Core services cleaned up");
                
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
            }
            catch (Exception ex)
            {
                Log($"OnExit cleanup error: {ex.Message}");
            }
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log($"DispatcherUnhandledException: {e.Exception.Message}\n{e.Exception.StackTrace}");
            e.Handled = true;
        }

        private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            Log($"UnhandledException: {exception?.Message}\n{exception?.StackTrace}");
        }

        public static void Log(string message)
        {
            try
            {
                // 延迟初始化日志路径（在非 WPF 启动场景下也能工作，例如单元/工具调用）
                if (string.IsNullOrEmpty(_logFilePath))
                {
                    try
                    {
                        _logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinLoop", "log.txt");
                        var dir = Path.GetDirectoryName(_logFilePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    }
                    catch
                    {
                        // 如果初始化失败，保留 _logFilePath 为 null，稍后写入会被捕获
                    }
                }

                using (StreamWriter writer = new StreamWriter(_logFilePath, true))
                {
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                }
            }
            catch (Exception ex)
            {
                // 如果日志写入失败，尝试在控制台输出
                Console.WriteLine($"Failed to write to log: {ex.Message}");
                Console.WriteLine($"Original message: {message}");
            }
        }
    }
}
