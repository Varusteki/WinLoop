using System;
using Microsoft.Win32;
using System.Reflection;
using System.IO;

namespace WinLoop.SystemIntegration
{
    /// <summary>
    /// 管理Windows开机自启动
    /// </summary>
    public class AutoStartManager
    {
        private const string AppName = "WinLoop";
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// 获取当前应用程序的完整路径
        /// </summary>
        private static string GetExecutablePath()
        {
            return Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe");
        }

        /// <summary>
        /// 检查是否已设置开机自启动
        /// </summary>
        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false))
                {
                    if (key == null) return false;
                    
                    var value = key.GetValue(AppName) as string;
                    if (string.IsNullOrEmpty(value)) return false;

                    // 验证路径是否与当前程序路径一致
                    string currentPath = GetExecutablePath();
                    return value.Equals(currentPath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                LogError($"IsAutoStartEnabled error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 启用开机自启动
        /// </summary>
        public static bool EnableAutoStart()
        {
            try
            {
                string exePath = GetExecutablePath();
                
                if (!File.Exists(exePath))
                {
                    LogError($"Executable not found: {exePath}");
                    return false;
                }

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key == null)
                    {
                        LogError("Failed to open registry key");
                        return false;
                    }

                    key.SetValue(AppName, exePath, RegistryValueKind.String);
                    LogInfo($"Auto-start enabled: {exePath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError($"EnableAutoStart error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 禁用开机自启动
        /// </summary>
        public static bool DisableAutoStart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key == null) return true; // 注册表项不存在，视为成功

                    var value = key.GetValue(AppName);
                    if (value != null)
                    {
                        key.DeleteValue(AppName, false);
                        LogInfo("Auto-start disabled");
                    }
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError($"DisableAutoStart error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根据配置设置自启动状态
        /// </summary>
        public static void ApplyAutoStartSetting(bool enable)
        {
            bool currentStatus = IsAutoStartEnabled();
            
            if (enable && !currentStatus)
            {
                EnableAutoStart();
            }
            else if (!enable && currentStatus)
            {
                DisableAutoStart();
            }
        }

        private static void LogInfo(string message)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WinLoop"
                );
                Directory.CreateDirectory(logDir);
                
                string logPath = Path.Combine(logDir, "log.txt");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n";
                File.AppendAllText(logPath, logEntry);
            }
            catch { }
        }

        private static void LogError(string message)
        {
            LogInfo($"ERROR: {message}");
        }
    }
}
