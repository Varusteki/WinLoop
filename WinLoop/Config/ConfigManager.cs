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
                            // 可空枚举：老配置 JSON 无此键 → null → 回落到 AppConfig
                            // 自己的默认值（左 Alt）。若这里直接收 0 值，老用户一升级
                            // 修饰键就变成"左 Shift"。
                            cfg.SystemSnapModifier = dto.SystemSnapModifier ?? SnapModifierKey.LeftAlt;
                            cfg.XuanKongSi = dto.XuanKongSi ?? new XuanKongSiConfig();
                            cfg.SizingUnitVersion = dto.SizingUnitVersion;

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
                            MigrateSizingUnit(cfg);
                            MigrateActionMapping(cfg);
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
                    MigrateSizingUnit(cfg2);
                    MigrateActionMapping(cfg2);
                    return cfg2;
                }
            }
            catch (System.Exception ex)
            {
                App.Log("LoadConfig error: " + ex.Message);
            }

            return new AppConfig();
        }

        /// <summary>
        /// 把老配置的尺寸单位补标为当前版本。
        ///
        /// v1（无标记）的字段语义是「固定像素」，v2 是「100% 缩放下的逻辑像素」。
        /// 两者在 100% 缩放时数值完全等价，因此这里**只补标记、不动任何数值** ——
        /// 用户的 50/28/70/90 原样保留，含义从"永远画这么大"变成"100% 缩放下画这么大"。
        ///
        /// 标记的用处：将来若真需要按屏幕把老值反算一次（本方案不需要），
        /// 或需要区分"从未迁移"与"已迁移"，有据可依。
        /// </summary>
        private static void MigrateSizingUnit(AppConfig cfg)
        {
            try
            {
                if (cfg == null) return;
                if (cfg.SizingUnitVersion >= AppConfig.SizingUnitCurrent) return;

                App.Log($"Migrating sizing unit v{cfg.SizingUnitVersion} -> v{AppConfig.SizingUnitCurrent}"
                        + " (values unchanged: v1 pixels are numerically identical to v2 DIP baseline).");

                cfg.SizingUnitVersion = AppConfig.SizingUnitCurrent;
            }
            catch (System.Exception ex)
            {
                App.Log("MigrateSizingUnit error: " + ex.Message);
            }
        }

        /// <summary>
        /// 清理 <c>WindowAction</c> 里的**空号**：把配置中指向已删除动作的序号退回"该位置的默认动作"。
        ///
        /// 背景（2026-10-20）：旧版动作表的 13 = 最大化/还原切换、14 = 窗口置顶切换，两项按用户
        /// 要求删除。序号**没有回收**（回收会让老配置静默变成另一个动作，见 AppConfig.WindowAction），
        /// 于是 13 / 14 变成空号 —— 老配置里仍有这两个数字。
        ///
        /// ⚠️ 为什么必须兜底：System.Text.Json 按数字反序列化枚举**不会报错**，
        /// 未定义值会一路带到 <c>ExecuteAction</c> 的 switch 落到 default 分支被静默忽略，
        /// 用户看到的现象只是"某个扇区突然没反应"，日志里 `Executing action: 13` 照常打印，
        /// 排查成本极高。所以在这里一次性换成有效动作，并把替换记进日志。
        /// </summary>
        private static void MigrateActionMapping(AppConfig cfg)
        {
            try
            {
                if (cfg == null || cfg.ActionMapping == null || cfg.ActionMapping.Count == 0) return;

                // 默认映射单独取一份：不要读 cfg 自己的（它可能整体就是畸形数据）。
                var defaults = new AppConfig().ActionMapping;

                foreach (var pos in new System.Collections.Generic.List<MenuItemPosition>(cfg.ActionMapping.Keys))
                {
                    WindowAction action = cfg.ActionMapping[pos];
                    if (System.Enum.IsDefined(typeof(WindowAction), action)) continue;

                    WindowAction fallback;
                    if (!defaults.TryGetValue(pos, out fallback)) fallback = WindowAction.Minimize;

                    cfg.ActionMapping[pos] = fallback;
                    App.Log($"MigrateActionMapping: {pos} 指向已删除动作({(int)action}) → {fallback}");
                }
            }
            catch (System.Exception ex)
            {
                App.Log("MigrateActionMapping error: " + ex.Message);
            }
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
                    SystemSnapModifier = config.SystemSnapModifier,
                    XuanKongSi = config.XuanKongSi,
                    // 落盘时一律写成当前单位版本：存进去的值就是 DIP 基准值，
                    // 不能把读到时的旧标记再写回去。
                    SizingUnitVersion = AppConfig.SizingUnitCurrent,
                    ActionMapping = new System.Collections.Generic.Dictionary<string, WinLoop.Models.WindowAction>()
                };

                foreach (var kv in config.ActionMapping)
                {
                    dto.ActionMapping[kv.Key.ToString()] = kv.Value;
                }

                string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                App.Log($"Saving config to {_configPath}");
                App.Log(json);

                // 落盘前把「上一次的配置」留一份 .bak：配置大多是用户手工调出来的，
                // 而这里是无条件覆盖，一旦被错误的值写坏就找不回来了。
                // 备份失败不能影响保存本身，所以单独包一层。
                try
                {
                    if (File.Exists(_configPath))
                    {
                        string bak = _configPath + ".bak";
                        File.Copy(_configPath, bak, true);
                    }
                }
                catch (System.Exception exBak)
                {
                    App.Log("SaveConfig backup skipped: " + exBak.Message);
                }

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

        /// <summary>
        /// 系统分屏修饰键。**可空**：老 config.json 没有这个键，反序列化为 null，
        /// 由加载方回落默认值（见 LoadConfig 里的 ?? LeftAlt）。
        /// DTO 侧刻意不给初值 —— 给了就等于宣布"没配也是某个键"。
        /// </summary>
        public SnapModifierKey? SystemSnapModifier { get; set; }
        public XuanKongSiConfig XuanKongSi { get; set; }

        /// <summary>
        /// 尺寸单位版本。老 config.json 无此字段 → 反序列化为 0（= v1），
        /// 供 <see cref="ConfigManager"/> 识别老配置。
        /// 注意 DTO 侧**不能**给非 0 默认值，否则老配置会被误判成"已迁移"。
        /// </summary>
        public int SizingUnitVersion { get; set; }
    }
}