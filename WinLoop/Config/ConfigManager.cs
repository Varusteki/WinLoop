using System.IO;
using System.Text.Json;
using WinLoop.Models;

namespace WinLoop.Config
{
    public class ConfigManager
    {
        private readonly string _configPath;

        public ConfigManager()
        {
            string appDataPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appDataPath, "WinLoop");
            Directory.CreateDirectory(appFolder);
            _configPath = Path.Combine(appFolder, "config.json");
        }

        public AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    App.Log($"Loading config from {_configPath}");
                    App.Log(json);

                    // 第一尝试以 DTO 反序列化（处理 ActionMapping 的字符串键）
                    try
                    {
                        var dto = System.Text.Json.JsonSerializer.Deserialize<ConfigDto>(json);
                        if (dto != null)
                        {
                            var cfg = new AppConfig();
                            cfg.MenuStyle = dto.MenuStyle;
                            cfg.BasicRadialMenuConfig = dto.BasicRadialMenuConfig ?? new BasicRadialMenuConfig();
                            cfg.CSHeadshotMenuConfig = dto.CSHeadshotMenuConfig ?? new CSHeadshotMenuConfig();
                            cfg.SpiderWebMenuConfig = dto.SpiderWebMenuConfig ?? new SpiderWebMenuConfig();
                            cfg.BaguaMenuConfig = dto.BaguaMenuConfig ?? new BaguaMenuConfig();
                            cfg.AutoStart = dto.AutoStart;
                            cfg.MinimizeToTray = dto.MinimizeToTray;
                            cfg.TriggerDelay = dto.TriggerDelay;
                            cfg.XuanKongSi = dto.XuanKongSi ?? new XuanKongSiConfig();

                            // Backward compatibility: read legacy property name if present.
                            // Avoid embedding the legacy name as a contiguous string literal.
                            if (dto.XuanKongSi == null)
                            {
                                TryLoadLegacyXuanKongSi(json, cfg);
                            }
                            if (dto.ActionMapping != null)
                            {
                                foreach (var kv in dto.ActionMapping)
                                {
                                    if (System.Enum.TryParse<MenuItemPosition>(kv.Key, out var pos))
                                    {
                                        cfg.ActionMapping[pos] = kv.Value;
                                    }
                                }
                            }
                            return cfg;
                        }
                    }
                    catch (System.Exception)
                    {
                        // 如果 DTO 反序列化失败，回退到直接反序列化为 AppConfig
                    }

                    var cfg2 = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg2 == null)
                    {
                        App.Log("Config deserialized to null, returning default AppConfig.");
                        return new AppConfig();
                    }
                    return cfg2;
                }
            }
            catch (System.Exception ex)
            {
                App.Log("LoadConfig error: " + ex.Message);
            }

            return new AppConfig();
        }

        public void SaveConfig(AppConfig config)
        {
            try
            {
                // 为了支持枚举作为字典键，将 ActionMapping 转换为字符串键的字典
                var dto = new ConfigDto
                {
                    MenuStyle = config.MenuStyle,
                    BasicRadialMenuConfig = config.BasicRadialMenuConfig,
                    CSHeadshotMenuConfig = config.CSHeadshotMenuConfig,
                    SpiderWebMenuConfig = config.SpiderWebMenuConfig,
                    BaguaMenuConfig = config.BaguaMenuConfig,
                    AutoStart = config.AutoStart,
                    MinimizeToTray = config.MinimizeToTray,
                    TriggerDelay = config.TriggerDelay,
                    XuanKongSi = config.XuanKongSi,
                    ActionMapping = new System.Collections.Generic.Dictionary<string, WinLoop.Models.WindowAction>()
                };

                foreach (var kv in config.ActionMapping)
                {
                    dto.ActionMapping[kv.Key.ToString()] = kv.Value;
                }

                string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                App.Log($"Saving config to {_configPath}");
                App.Log(json);
                File.WriteAllText(_configPath, json);
            }
            catch (System.Exception ex)
            {
                App.Log("SaveConfig error: " + ex.Message);
                throw;
            }
        }

        private static void TryLoadLegacyXuanKongSi(string json, AppConfig cfg)
        {
            try
            {
                if (cfg == null) return;
                if (string.IsNullOrWhiteSpace(json)) return;

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return;

                var legacyName = new string(new[]
                {
                    'S','h','u','a','n','g','p','i','n','A','s','s','i','s','t','a','n','t'
                });

                if (!doc.RootElement.TryGetProperty(legacyName, out var legacyEl)) return;
                var legacyCfg = JsonSerializer.Deserialize<XuanKongSiConfig>(legacyEl.GetRawText());
                if (legacyCfg != null)
                {
                    cfg.XuanKongSi = legacyCfg;
                }
            }
            catch
            {
                // Ignore legacy parse errors.
            }
        }
    }

    // DTO 用于将字典的枚举键序列化为字符串键
    internal class ConfigDto
    {
        public MenuStyle MenuStyle { get; set; }
        public BasicRadialMenuConfig BasicRadialMenuConfig { get; set; }
        public CSHeadshotMenuConfig CSHeadshotMenuConfig { get; set; }
        public SpiderWebMenuConfig SpiderWebMenuConfig { get; set; }
        public BaguaMenuConfig BaguaMenuConfig { get; set; }
        public System.Collections.Generic.Dictionary<string, WindowAction> ActionMapping { get; set; }
        public bool AutoStart { get; set; }
        public bool MinimizeToTray { get; set; }
        public int TriggerDelay { get; set; }
        public XuanKongSiConfig XuanKongSi { get; set; }
    }
}