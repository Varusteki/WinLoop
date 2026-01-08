using System;
using System.Windows;
using WinLoop.Config;
using WinLoop.Core;
using WinLoop.UI;

namespace WinLoop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MouseManager _mouseManager;
        private ConfigManager _configManager;
        
        public MainWindow()
        {
            try
            {
                App.Log("MainWindow constructor starting...");
                
                InitializeComponent();
                // 启动时不在屏幕上显示主窗口（仅显示设置窗口）
                this.Visibility = Visibility.Hidden;
                this.ShowInTaskbar = false;
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                
                App.Log("MainWindow InitializeComponent completed.");
                
                // 初始化配置管理器
                App.Log("Initializing ConfigManager...");
                _configManager = new ConfigManager();
                App.Log("ConfigManager initialized successfully.");
                
                // 初始运行不再自动弹出设置窗口
                App.Log("Skipping automatic SettingsWindow show on startup.");
                
                // 初始化鼠标管理器
                App.Log("Initializing MouseManager...");
                _mouseManager = new MouseManager(this, _configManager);
                App.Log("MouseManager initialized successfully.");

                // 启动阶段完成，允许后续显示 SettingsWindow（用户触发）
                try
                {
                    if (Application.Current != null && Application.Current.Properties.Contains("SuppressSettingsOnStartup"))
                    {
                        Application.Current.Properties["SuppressSettingsOnStartup"] = false;
                        App.Log("SuppressSettingsOnStartup = false (startup complete)");
                    }
                }
                catch { }
                
            }
            catch (Exception ex)
            {
                App.Log($"Error in MainWindow constructor: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _mouseManager?.Dispose();
            }
            catch { }
            base.OnClosed(e);
        }
        
        private void ShowSettingsWindow()
        {
            try
            {
                SettingsWindow settingsWindow = new SettingsWindow();
                settingsWindow.ShowDialog();
                App.Log("SettingsWindow closed.");
            }
            catch (Exception ex)
            {
                App.Log($"Error in ShowSettingsWindow: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }
    }
}
