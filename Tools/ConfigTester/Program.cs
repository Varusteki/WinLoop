using System;
using WinLoop.Config;
using WinLoop.Models;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("Starting ConfigTester...");
            var cm = new ConfigManager();
            var cfg = cm.LoadConfig();
            Console.WriteLine("Loaded config (before):");
            Console.WriteLine($" MenuStyle={cfg.MenuStyle}");
            Console.WriteLine($" OuterRadius={cfg.BasicRadialMenuConfig?.OuterRadius}");

            // If invoked with 'sync-loop-defaults', apply Loop-like defaults
            if (args != null && args.Length > 0 && args[0].Equals("sync-loop-defaults", StringComparison.OrdinalIgnoreCase))
            {
                cfg.MenuStyle = MenuStyle.BasicRadial;
                cfg.BasicRadialMenuConfig.OuterRadius = 50;
                // Outer 50 with thickness 22 => InnerRadius ~= 28
                cfg.BasicRadialMenuConfig.InnerRadius = 28;
                // Use Loop-like default colors (keep existing if empty)
                if (string.IsNullOrWhiteSpace(cfg.BasicRadialMenuConfig.WheelColor)) cfg.BasicRadialMenuConfig.WheelColor = "#FFFFFF";
                if (string.IsNullOrWhiteSpace(cfg.BasicRadialMenuConfig.RingColor)) cfg.BasicRadialMenuConfig.RingColor = "#000000";
                cfg.TriggerDelay = 0;
                Console.WriteLine("Applying Loop-like defaults to config.json");
            }
            else
            {
                // Modify some values (existing test behavior)
                cfg.BasicRadialMenuConfig.OuterRadius = 123;
                cfg.BasicRadialMenuConfig.InnerRadius = 45;
                cfg.TriggerDelay = 500;
                cfg.AutoStart = false;
                cfg.MinimizeToTray = false;
                cfg.ActionMapping[MenuItemPosition.Position1] = WindowAction.Minimize;
            }

            cm.SaveConfig(cfg);

            Console.WriteLine("Saved config. Reading file content:");
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cfgPath = System.IO.Path.Combine(local, "WinLoop", "config.json");
            if (System.IO.File.Exists(cfgPath))
            {
                var json = System.IO.File.ReadAllText(cfgPath);
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine("config.json not found at: " + cfgPath);
            }

            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logPath = System.IO.Path.Combine(roaming, "WinLoop", "log.txt");
            Console.WriteLine("\nLast lines of log.txt (if exists):");
            if (System.IO.File.Exists(logPath))
            {
                var lines = System.IO.File.ReadAllLines(logPath);
                int start = Math.Max(0, lines.Length - 200);
                for (int i = start; i < lines.Length; i++) Console.WriteLine(lines[i]);
            }
            else
            {
                Console.WriteLine("log.txt not found at: " + logPath);
            }

            // Reload via ConfigManager to ensure load path
            var cfg2 = cm.LoadConfig();
            Console.WriteLine("\nReloaded config (after):");
            Console.WriteLine($" MenuStyle={cfg2.MenuStyle}");
            Console.WriteLine($" OuterRadius={cfg2.BasicRadialMenuConfig?.OuterRadius}");
            Console.WriteLine($" Position1 action={ (cfg2.ActionMapping.TryGetValue(MenuItemPosition.Position1, out var a) ? a.ToString() : "<none>") }");
        }
        catch (Exception ex)
        {
            Console.WriteLine("ConfigTester error: " + ex.ToString());
        }
    }
}
