using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinLoop.Menus;
using WinLoop.Models;

namespace WinLoop.Tools.IconGen
{
    /// <summary>
    /// 图标生成工具 —— 把「八角星菜单」渲染成带透明背景的高分辨率 PNG，供合成 .ico 使用。
    ///
    /// 用法：IconGen.exe [输出目录] [主图边长px] [半径]
    ///   默认：probe-out/icon、2048px、半径 90
    ///
    /// 输出 `icon-master.png`：**完整三层**（圆环 + 八角星 + 骷髅）。
    ///
    /// 托盘与应用**共用这一张主图** —— 2026-09-20 用户明确要求托盘图标保留全部元素
    /// （曾试过去掉骷髅层的简化版以提升 16px 可辨识度，被否）。
    /// 两者的差别只在导出 ico 时的尺寸档位。
    ///
    /// 合成脚本见 **`docs/archive/icon-source/make_icons.py`**
    /// （本工具与它同被归档在 `docs/archive/icon-source/` —— 因为 `Tools/`
    ///  与 `probe-out/` 都在 .gitignore 里，不归档就会随本地清理丢失）。
    ///
    /// ⚠️ 纯离屏渲染（RenderTargetBitmap），不注入任何键鼠输入。
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 四周留白（相对菜单半径）。
        /// 这里只留一点点：真正的「撑满画布」由合成脚本按实际像素边界裁剪完成
        /// （见 make_icons.py 的 fit()）—— 留白会让图标在系统里显得比别的应用小。
        /// </summary>
        private const double PAD_RATIO = 0.02;

        [STAThread]
        private static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string outDir = args.Length > 0 ? args[0] : "probe-out/icon";
            int targetPx = args.Length > 1 ? int.Parse(args[1]) : 2048;
            double radius = args.Length > 2 ? double.Parse(args[2]) : 90.0;

            Directory.CreateDirectory(outDir);

            var cfg = new AppConfig();
            cfg.CSHeadshotMenuConfig.Radius = radius;

            var menu = new CSHeadshotMenu();
            menu.Initialize(cfg, new Point(0, 0));

            double w = menu.Width;
            double h = menu.Height;
            double pad = radius * PAD_RATIO;

            // 透明底板：图标需要 alpha 通道，所以 Background 留 null
            var host = new Canvas
            {
                Width = w + pad * 2,
                Height = h + pad * 2,
                Background = null,
                ClipToBounds = false
            };
            Canvas.SetLeft(menu, pad);
            Canvas.SetTop(menu, pad);
            host.Children.Add(menu);

            var size = new Size(host.Width, host.Height);
            host.Measure(size);
            host.Arrange(new Rect(size));
            host.UpdateLayout();

            menu.HighlightItem(MenuItemPosition.Position2);   // Position2 = 右上扇区
            host.UpdateLayout();

            double scale = targetPx / host.Width;
            int pw = (int)Math.Round(host.Width * scale);
            int ph = (int)Math.Round(host.Height * scale);

            var rtb = new RenderTargetBitmap(
                pw, ph, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
            rtb.Render(host);

            string file = Path.Combine(outDir, "icon-master.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(file)) encoder.Save(fs);

            Console.WriteLine($"主图: {Path.GetFullPath(file)}");
            Console.WriteLine($"尺寸: {pw}x{ph}   半径={radius}   高亮={MenuItemPosition.Position2}（右上）");
            Console.WriteLine($"菜单逻辑尺寸: {w}x{h}   留白={pad:F1}");
        }
    }
}
