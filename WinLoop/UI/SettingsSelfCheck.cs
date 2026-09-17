using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinLoop.UI
{
    /// <summary>
    /// 设置窗口的运行时自检。
    ///
    /// 为什么需要它：XAML 里有一类错误**编译期完全查不出来**，
    /// 只有真正构造窗口时才抛。典型是样式类型不匹配 ——
    /// 把 `TargetType="CheckBox"` 的 Style 套到 `RadioButton` 上，
    /// 运行时报：
    ///     设置属性"System.Windows.FrameworkElement.Style"时引发了异常。
    /// 整句话没有 Style 名、没有控件名、没有行号，光看日志根本定位不到。
    ///
    /// 本自检做的事：构造窗口 → 遍历每个 TabItem → 逐页渲染成 PNG →
    /// 记录每一页的可见控件数量。任何一页构造失败都会被单独报出来，
    /// 而不是笼统地说"窗口打不开"。
    ///
    /// 用法：WinLoop.exe --settingscheck [输出目录]
    /// </summary>
    internal static class SettingsSelfCheck
    {
        public static void Run(string outDir)
        {
            var sb = new StringBuilder();
            int fail = 0;

            void W(string line)
            {
                sb.AppendLine(line);
                try { Console.WriteLine(line); } catch { }
            }

            W("=== 设置窗口自检 ===");
            W("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            if (string.IsNullOrEmpty(outDir))
            {
                outDir = Path.Combine(Path.GetTempPath(), "WinLoopSettingsCheck");
            }
            try { Directory.CreateDirectory(outDir); } catch { }
            W("输出目录: " + outDir);
            W("");

            SettingsWindow win = null;

            // ---- 1) 构造窗口：这一步就能抓出 XAML 解析/样式类型的错误 ----
            try
            {
                win = new SettingsWindow();
                W("PASS: SettingsWindow 构造成功（XAML 可正常解析）");
            }
            catch (Exception ex)
            {
                W("FAIL: SettingsWindow 构造失败");
                W("      类型: " + ex.GetType().FullName);
                W("      消息: " + ex.Message);
                if (ex.InnerException != null)
                {
                    W("      内层: " + ex.InnerException.GetType().FullName);
                    W("            " + ex.InnerException.Message);
                }
                fail++;
                W("");
                W("结果: FAILED (1)");
                Flush(sb, outDir);
                return;
            }
            W("");

            // ---- 2) 显示窗口（离屏渲染拿不到真实布局，必须真正 Show 一次） ----
            try
            {
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.Left = -32000;      // 挪到屏幕外，不打扰用户
                win.Top = -32000;
                win.ShowInTaskbar = false;
                win.Show();

                // 让布局、绑定、模板全部跑完
                Pump();
                win.UpdateLayout();
                Pump();

                W(string.Format(CultureInfo.InvariantCulture,
                    "PASS: 窗口已显示，实际尺寸 {0}x{1}（期望 880x620）",
                    win.ActualWidth, win.ActualHeight));

                if (Math.Abs(win.ActualWidth - 880) > 1 || Math.Abs(win.ActualHeight - 620) > 1)
                {
                    W("WARN: 实际尺寸与设定值不一致");
                }
                if (win.ResizeMode != ResizeMode.NoResize)
                {
                    W("FAIL: ResizeMode 不是 NoResize");
                    fail++;
                }
                else
                {
                    W("PASS: ResizeMode = NoResize（不可缩放、不可最大化）");
                }
            }
            catch (Exception ex)
            {
                W("FAIL: 窗口显示失败: " + ex.Message);
                fail++;
            }
            W("");

            // ---- 3) 逐页切换并截图 ----
            var tabs = FindTabControl(win);
            if (tabs == null)
            {
                W("FAIL: 找不到 TabControl");
                fail++;
            }
            else
            {
                W("共 " + tabs.Items.Count + " 个页面");

                for (int i = 0; i < tabs.Items.Count; i++)
                {
                    string name = "page" + (i + 1);

                    try
                    {
                        tabs.SelectedIndex = i;
                        Pump();
                        win.UpdateLayout();
                        Pump();

                        int count = CountVisible(win);

                        string file = Path.Combine(outDir, name + ".png");
                        RenderToPng(win, file);

                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 页 {0}（{1}）已渲染，可见控件 {2} 个 -> {3}",
                            i + 1, TabHeader(tabs, i), count, Path.GetFileName(file)));
                    }
                    catch (Exception ex)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 页 {0} 渲染失败: {1}: {2}",
                            i + 1, ex.GetType().Name, ex.Message));
                        fail++;
                    }
                }
            }

            W("");

            try { win.Close(); } catch { }

            // ---- 4) 菜单样式的四个变体：配色行数量不同，逐一确认 ----
            // 这一步是为了验证「色值框从界面移除、色值改由按钮显示」的改动
            // 在**每一种样式**下都正常 —— 圆环 2 个配色行、蜘蛛网 2 个、
            // 八卦 1 个，数量不同，布局都得站得住。
            CheckMenuStyleVariants(outDir, ref fail, W);

            // ---- 5) 输入框：全部等宽 + 行内不再有任何单位/提示文字 ----
            CheckInputUniformity(ref fail, W);

            // ---- 6) 悬空寺图片面板：标题文案 + 预览区随宽度自适应 ----
            CheckXuanKongSiImagePanel(outDir, ref fail, W);

            W(fail == 0 ? "结果: ALL PASS" : ("结果: FAILED (" + fail + ")"));
            Flush(sb, outDir);
        }

        /// <summary>
        /// 两条断言：
        /// ① 所有单行输入框宽度完全一致（否则栅格就乱了）；
        /// ② 输入框所在行里不再出现"逻辑像素 @100%""层"%""毫秒"这类单位文字
        ///    —— 单位信息已经移到 ToolTip，行内再出现就是漏删。
        ///
        /// ⚠️ 必须**逐页切换**再收集控件：TabControl 只实例化当前选中页，
        /// 不切页的话另外三页的输入框根本不在可视树里，断言就会漏掉一大半。
        /// </summary>
        private static void CheckInputUniformity(ref int fail, Action<string> W)
        {
            W("");
            W("--- 输入框一致性 ---");

            SettingsWindow w = null;
            try
            {
                w = new SettingsWindow();
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -32000; w.Top = -32000;
                w.ShowInTaskbar = false;
                w.Show();
                Pump();

                var tabs = FindTabControl(w);
                int pageCount = tabs != null ? tabs.Items.Count : 1;

                // ⚠️ 为什么按"名字清单"逐个查，而不是遍历可视树收集：
                // TabControl 切走某页后，那页控件的 ActualHeight 会被重置为 0，
                // 而且并不保证还挂在可视树上。遍历收集必然漏掉非当前页的控件。
                // 按名字定位（只认名字，不依赖挂载状态）再切到它所属页量尺寸，
                // 才是唯一可靠的做法。
                //
                // 新增输入框时记得把名字加进这张表 —— 漏加只会让断言覆盖变少，
                // 不会误报，属于"安全的失败方向"。
                var known = new[]
                {
                    "BasicOuterRadiusBox",
                    "BasicInnerRadiusBox",
                    "SpiderWebRadiusBox",
                    "SpiderWebRingsBox",
                    "BaguaRadiusBox",
                    "BaguaSectorTransparencyBox",
                    "BaguaHighlightTransparencyBox",
                    "TriggerDelayBox",
                };

                // 第一趟：找出每个输入框归属哪一页。
                // ⚠️ 不能用 FindByName(w, ...) 去"碰" —— 逻辑树把所有页的内容都挂着，
                // 无论当前选哪页都可能先撞上别的页的控件（实测四个框全被误判成第 1 页）。
                // 必须**限定在单个 TabItem 的子树里**查，页归属才准确。
                var homePage = new System.Collections.Generic.Dictionary<string, int>();
                for (int p = 0; p < pageCount; p++)
                {
                    if (tabs != null) tabs.SelectedIndex = p;
                    Pump();

                    if (tabs == null) break;

                    var ti = tabs.Items[p] as TabItem;
                    if (ti == null) continue;

                    foreach (var nm in known)
                    {
                        if (homePage.ContainsKey(nm)) continue;
                        if (FindByName(ti, nm) != null) homePage[nm] = p;
                    }
                }

                // ⚠️ 这里的难点：菜单样式页（页 1）里，圆环/八角星/蜘蛛网/八卦
                // 是四组**互斥显示**的面板，只有当前选中样式那组是 Visible。
                // 所以量尺寸前必须先把对应样式也选上，否则拿到的永远是 0 ——
                // 这不是布局出问题，是控件本来就被 Collapsed 了。
                var styleForBox = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "BasicOuterRadiusBox",           "BasicRadialRadio" },
                    { "BasicInnerRadiusBox",           "BasicRadialRadio" },
                    { "SpiderWebRadiusBox",            "SpiderWebRadio"   },
                    { "SpiderWebRingsBox",             "SpiderWebRadio"   },
                    { "BaguaRadiusBox",                "BaguaRadio"       },
                    { "BaguaSectorTransparencyBox",    "BaguaRadio"       },
                    { "BaguaHighlightTransparencyBox", "BaguaRadio"       },
                    { "TriggerDelayBox",               null               },
                };

                var single = new System.Collections.Generic.List<string>();
                int missing = 0;

                foreach (var nm in known)
                {
                    int page;
                    if (!homePage.TryGetValue(nm, out page))
                    {
                        W("FAIL: 找不到输入框 " + nm);
                        missing++;
                        continue;
                    }

                    if (tabs != null) tabs.SelectedIndex = page;
                    Pump();

                    // 若该输入框属于某个菜单样式面板，先把那组样式选上让它可见
                    string styleKey;
                    if (styleForBox.TryGetValue(nm, out styleKey) && !string.IsNullOrEmpty(styleKey))
                    {
                        var f = typeof(SettingsWindow).GetField(styleKey,
                            System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.Public);
                        var rb = f?.GetValue(w) as System.Windows.Controls.RadioButton;
                        if (rb != null && rb.IsChecked != true)
                        {
                            rb.IsChecked = true;
                            Pump();
                        }
                    }

                    w.UpdateLayout();
                    Pump();

                    var scope = (tabs != null ? tabs.Items[page] as TabItem : null) as DependencyObject ?? w;
                    var b = FindByName(scope, nm) as System.Windows.Controls.TextBox;

                    if (b == null || b.ActualWidth <= 0.5)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 输入框 {0}（第 {1} 页）量到的宽度无效：{2}",
                            nm, page + 1,
                            b == null ? "(取不到控件)" : b.ActualWidth.ToString("F1", CultureInfo.InvariantCulture)));
                        missing++;
                        continue;
                    }

                    single.Add(nm + "=" + b.ActualWidth.ToString("F0", CultureInfo.InvariantCulture));
                }

                var multi = new System.Collections.Generic.List<string>();
                var editor = FindByName(w, "XuanKongSiTextEditor") as System.Windows.Controls.TextBox;
                if (editor != null) multi.Add("XuanKongSiTextEditor");
                else { W("FAIL: 找不到 Markdown 编辑器"); missing++; }

                W(string.Format(CultureInfo.InvariantCulture,
                    "命名输入框：单行 {0} 个、多行 {1} 个",
                    single.Count, multi.Count));

                if (single.Count > 0) W("      单行（参与等宽断言）：" + string.Join("、", single.ToArray()));
                if (missing > 0) fail += missing;

                if (single.Count == 0)
                {
                    W("FAIL: 一个单行输入框都没找到，断言本身可能失效了");
                    fail++;
                }
                else
                {
                    int badWidth = 0;
                    double firstW = -1;
                    foreach (var item in single)
                    {
                        int eq = item.IndexOf('=');
                        if (eq < 0) continue;
                        string nm = item.Substring(0, eq);
                        double wd;
                        double.TryParse(item.Substring(eq + 1), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out wd);

                        if (firstW < 0) firstW = wd;
                        if (Math.Abs(wd - firstW) > 0.5)
                        {
                            W(string.Format(CultureInfo.InvariantCulture,
                                "FAIL: 输入框 {0} 宽度 {1}，与首个 {2} 不一致", nm, wd, firstW));
                            badWidth++;
                        }
                    }

                    if (badWidth == 0)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: {0} 个单行输入框宽度全部一致（{1}）", single.Count, firstW));
                    }
                    else
                    {
                        fail += badWidth;
                    }

                    // ①b 颜色按钮必须和输入框同宽 —— 它们在同一列里上下相邻，
                    // 右边缘对不齐在视觉上非常刺眼。两边都吃 FormFieldW 令牌，
                    // 但一个是 Width、一个曾经是 MinWidth(146)，所以必须实测。
                    CheckColorButtonWidth(w, tabs, firstW, ref fail, W);
                }

                // ② 单位/提示文字残留检查
                // 只匹配行内单位提示的**完整串**，不做子串模糊匹配 ——
                // 像"层"这种单字太宽泛（正文里"图层""分层"都会命中），会误报。
                // 同样要逐页收集（非当前页的 TextBlock 不在可视树上）。
                var banned = new[] { "逻辑像素", "@100%" };
                int residue = 0;
                var seenText = new System.Collections.Generic.HashSet<string>();

                for (int p = 0; p < pageCount; p++)
                {
                    if (tabs != null) tabs.SelectedIndex = p;
                    Pump();
                    w.UpdateLayout();
                    Pump();

                    var texts = new System.Collections.Generic.List<TextBlock>();
                    CollectTextBlocks(w, texts);

                    foreach (var tb in texts)
                    {
                        string s = tb.Text ?? "";
                        if (s.Length == 0) continue;
                        if (!seenText.Add(s)) continue;

                        bool hit = s == "%";
                        if (!hit)
                        {
                            foreach (var b in banned)
                            {
                                if (s.IndexOf(b, StringComparison.Ordinal) >= 0) { hit = true; break; }
                            }
                        }
                        if (!hit) continue;

                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 行内仍有提示文字「{0}」", s));
                        residue++;
                    }
                }

                if (residue == 0)
                {
                    W("PASS: 行内已无任何单位/提示文字（单位信息仅在 ToolTip 中）");
                }
                else
                {
                    fail += residue;
                }

                w.Close();
            }
            catch (Exception ex)
            {
                W("FAIL: 输入框一致性检查异常: " + ex.GetType().Name + ": " + ex.Message);
                fail++;
                try { if (w != null) w.Close(); } catch { }
            }
        }

        /// <summary>
        /// 颜色按钮的宽度必须与单行输入框一致。
        ///
        /// 两者在同一列的上下相邻行里，但走的是完全不同的样式
        /// （InputStyle 定宽 vs ColorSwatchButton）。历史上 ColorSwatchButton
        /// 用的是 MinWidth=146，比输入框的 100 宽 46px，右边缘明显参差。
        /// 这类"两个样式各自定宽"的耦合没法靠编译发现，只能实测。
        ///
        /// 与输入框同理：量之前要先切到它所在的页 + 选中它所属的菜单样式组，
        /// 否则控件是 Collapsed，ActualWidth 恒为 0。
        /// </summary>
        private static void CheckColorButtonWidth(SettingsWindow w, TabControl tabs,
            double inputWidth, ref int fail, Action<string> W)
        {
            W("");
            W("--- 颜色按钮与输入框等宽 ---");

            if (inputWidth <= 0.5)
            {
                W("WARN: 输入框基准宽度无效，跳过颜色按钮等宽检查");
                return;
            }

            // 名字 + 它所属的菜单样式 RadioButton（页归属靠第一趟扫出来）
            var colors = new[]
            {
                new { Name = "BasicRingColorPickButton",           Style = "BasicRadialRadio" },
                new { Name = "BasicHighlightColorPickButton",      Style = "BasicRadialRadio" },
                new { Name = "SpiderWebLineColorPickButton",       Style = "SpiderWebRadio"   },
                new { Name = "SpiderWebHighlightColorPickButton",  Style = "SpiderWebRadio"   },
                new { Name = "BaguaLineColorPickButton",           Style = "BaguaRadio"       },
            };

            int pageCount = tabs != null ? tabs.Items.Count : 0;

            // 第一趟：判定每个按钮归属哪一页（限定在单个 TabItem 子树里查）
            var homePage = new System.Collections.Generic.Dictionary<string, int>();
            for (int p = 0; p < pageCount; p++)
            {
                tabs.SelectedIndex = p;
                Pump();
                var ti = tabs.Items[p] as TabItem;
                if (ti == null) continue;
                foreach (var c in colors)
                {
                    if (homePage.ContainsKey(c.Name)) continue;
                    if (FindByName(ti, c.Name) != null) homePage[c.Name] = p;
                }
            }

            int matched = 0, mismatch = 0;

            foreach (var c in colors)
            {
                int page;
                if (!homePage.TryGetValue(c.Name, out page))
                {
                    W("FAIL: 找不到颜色按钮 " + c.Name);
                    fail++;
                    continue;
                }

                tabs.SelectedIndex = page;
                Pump();

                // 让所属菜单样式组可见
                var f = typeof(SettingsWindow).GetField(c.Style,
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public);
                var rb = f?.GetValue(w) as System.Windows.Controls.RadioButton;
                if (rb != null && rb.IsChecked != true) { rb.IsChecked = true; Pump(); }

                w.UpdateLayout();
                Pump();

                var scope = (tabs.Items[page] as TabItem) as DependencyObject ?? w;
                var btn = FindByName(scope, c.Name) as Button;

                if (btn == null)
                {
                    W("FAIL: 取不到控件 " + c.Name + "（第 " + (page + 1) + " 页）");
                    fail++;
                    continue;
                }

                double aw = btn.ActualWidth;
                if (Math.Abs(aw - inputWidth) > 0.5)
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "FAIL: {0} 宽 {1}，与输入框 {2} 不一致（差 {3:F0}px，右边缘对不齐）",
                        c.Name, aw.ToString("F0", CultureInfo.InvariantCulture),
                        inputWidth.ToString("F0", CultureInfo.InvariantCulture),
                        aw - inputWidth));
                    mismatch++;
                }
                else
                {
                    matched++;
                }
            }

            if (mismatch == 0 && matched > 0)
            {
                W(string.Format(CultureInfo.InvariantCulture,
                    "PASS: {0} 个颜色按钮宽度与输入框一致（{1}）", matched,
                    inputWidth.ToString("F0", CultureInfo.InvariantCulture)));
            }
            else if (matched == 0)
            {
                W("FAIL: 一个颜色按钮都没量到，断言可能失效了");
                fail++;
            }
            else
            {
                fail += mismatch;
            }
        }

        /// <summary>
        /// 悬空寺图片面板：
        /// ① 分组标题必须是「图片」而不是「键位图」（用户可以选任意图片，不只是键位图）；
        /// ② 预览区宽度跟随卡片可用宽度（贴满，而非写死一个小尺寸）；
        /// ③ 有已选图片时预览可见 + 图片源非空。
        /// </summary>
        private static void CheckXuanKongSiImagePanel(string outDir, ref int fail, Action<string> W)
        {
            W("");
            W("--- 悬空寺图片面板 ---");

            SettingsWindow w = null;
            try
            {
                w = new SettingsWindow();
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -32000; w.Top = -32000;
                w.ShowInTaskbar = false;
                w.Show();
                Pump();

                // 切到悬空寺页
                var tabs = FindTabControl(w);
                if (tabs != null)
                {
                    for (int i = 0; i < tabs.Items.Count; i++)
                    {
                        if ((TabHeader(tabs, i) ?? "").IndexOf("悬空寺", StringComparison.Ordinal) >= 0)
                        {
                            tabs.SelectedIndex = i;
                            break;
                        }
                    }
                }
                Pump();
                w.UpdateLayout();
                Pump();

                // ① 标题文案
                var texts = new System.Collections.Generic.List<TextBlock>();
                CollectTextBlocks(w, texts);
                bool hasOldTitle = false, hasNewTitle = false;
                foreach (var tb in texts)
                {
                    if (!IsReallyVisible(tb)) continue;
                    if ((tb.Text ?? "") == "键位图") hasOldTitle = true;
                    if ((tb.Text ?? "") == "图片"
                        && tb.FontSize > 13) hasNewTitle = true;
                }

                if (hasOldTitle)
                {
                    W("FAIL: 分组标题仍为「键位图」（应改为「图片」——用户可选任意图片）");
                    fail++;
                }
                else if (hasNewTitle)
                {
                    W("PASS: 分组标题已改为「图片」");
                }
                else
                {
                    W("WARN: 未找到图片分组标题");
                }

                // ②③ 预览区：主动造一张临时图片塞进配置，确保这条路径被真正测到。
                // 只依赖"用户当前配置里有没有图"的话，很容易 SKIP 掉，
                // 等于这段代码从没验证过 —— 自检要能自己造出前置条件。
                var border = FindByName(w, "XuanKongSiImagePreviewBorder") as Border;
                var img = FindByName(w, "XuanKongSiImagePreview") as System.Windows.Controls.Image;

                if (border == null || img == null)
                {
                    W("FAIL: 找不到预览控件 XuanKongSiImagePreview(Border)");
                    fail++;
                }
                else
                {
                    string probePath = null;
                    try { probePath = MakeProbeImage(); } catch { }

                    if (string.IsNullOrEmpty(probePath))
                    {
                        W("WARN: 无法生成测试图片，跳过预览自适应验证");
                    }
                    else
                    {
                        // 通过公开路径注入：改配置对象 + 调刷新方法
                        var cfgField = typeof(SettingsWindow).GetField("_config",
                            System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance);
                        var refresh = typeof(SettingsWindow).GetMethod("UpdateXuanKongSiImagePreview",
                            System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance);

                        var cfg = cfgField?.GetValue(w) as WinLoop.Models.AppConfig;
                        if (cfg != null && refresh != null)
                        {
                            if (cfg.XuanKongSi == null) cfg.XuanKongSi = new WinLoop.Models.XuanKongSiConfig();
                            cfg.XuanKongSi.ImageFileName = Path.GetFileName(probePath);
                            refresh.Invoke(w, null);
                            Pump();
                            w.UpdateLayout();
                            Pump();

                            W(string.Format(CultureInfo.InvariantCulture,
                                "注入测试图 {0}（原始 900x360）",
                                Path.GetFileName(probePath)));

                            // ④ 默认图也要能预览：用户没选图时，展示的应该是随包分发的
                            // 默认键位图，而不是一片空白。只测"选了图能预览"是不够的
                            // —— 首次打开窗口的用户走的全是这条默认路径。
                            // 把 ImageFileName 清掉，模拟"从未选过图"的初始状态。
                            cfg.XuanKongSi.ImageFileName = null;
                            refresh.Invoke(w, null);
                            Pump();
                            w.UpdateLayout();
                            Pump();

                            if (border.Visibility != Visibility.Visible)
                            {
                                W("FAIL: 未选图时预览区被隐藏了 —— 默认图也必须展示预览");
                                fail++;
                            }
                            else if (img.Source == null)
                            {
                                W("FAIL: 未选图时预览区可见但图片源为空（默认图没加载上）");
                                fail++;
                            }
                            else
                            {
                                W(string.Format(CultureInfo.InvariantCulture,
                                    "PASS: 未选图时走默认图预览（{0}x{1}，源 {2}）",
                                    img.ActualWidth, img.ActualHeight,
                                    (img.Source as BitmapImage)?.UriSource?.ToString() ?? "<非文件源>"));
                            }

                            // 恢复到注入了测试图的状态，供后面几段量尺寸用
                            cfg.XuanKongSi.ImageFileName = Path.GetFileName(probePath);
                            refresh.Invoke(w, null);
                            Pump();
                            w.UpdateLayout();
                            Pump();
                        }
                        else
                        {
                            W("WARN: 反射拿不到 _config / UpdateXuanKongSiImagePreview");
                        }
                    }

                    if (border.Visibility != Visibility.Visible)
                    {
                        W("FAIL: 预览区未显示（已注入测试图，应当可见）");
                        fail++;
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 预览区可见，容器宽 {0}，图片实际 {1}x{2}",
                            border.ActualWidth, img.ActualWidth, img.ActualHeight));

                        if (img.Source == null)
                        {
                            W("FAIL: 预览区可见但图片源为空");
                            fail++;
                        }
                        else
                        {
                            W("PASS: 图片源已加载");

                            // 等比缩放：900x360 → 宽高比 2.5:1，缩放后应保持
                            double srcRatio = 900.0 / 360.0;
                            double dstRatio = img.ActualWidth / Math.Max(1.0, img.ActualHeight);
                            if (Math.Abs(srcRatio - dstRatio) > 0.05)
                            {
                                W(string.Format(CultureInfo.InvariantCulture,
                                    "FAIL: 宽高比变了（原 {0:F3} → 现 {1:F3}），不是等比缩放",
                                    srcRatio, dstRatio));
                                fail++;
                            }
                            else
                            {
                                W(string.Format(CultureInfo.InvariantCulture,
                                    "PASS: 等比缩放保持（宽高比 {0:F3}）", dstRatio));
                            }

                            // 宽度跟随容器：应接近容器内可用宽度（容器有 10 的 Padding）
                            double usable = border.ActualWidth - 20 - 2;   // padding + border
                            if (img.ActualWidth > usable + 1.5)
                            {
                                W(string.Format(CultureInfo.InvariantCulture,
                                    "FAIL: 图片宽度 {0} 超出容器可用宽度 {1}",
                                    img.ActualWidth, usable));
                                fail++;
                            }
                            else if (img.ActualWidth < usable - 40)
                            {
                                W(string.Format(CultureInfo.InvariantCulture,
                                    "WARN: 图片宽度 {0} 明显小于可用宽度 {1}（未撑满，可能是 DownOnly 生效——原图小于容器时不放大）",
                                    img.ActualWidth, usable));
                            }
                            else
                            {
                                W(string.Format(CultureInfo.InvariantCulture,
                                    "PASS: 宽度已自适应撑满容器（{0} / 可用 {1}）",
                                    img.ActualWidth, usable));
                            }
                        }
                    }
                }

                RenderToPng(w, Path.Combine(outDir, "xuankongsi-image.png"));
                W("      截图 -> xuankongsi-image.png");

                w.Close();
            }
            catch (Exception ex)
            {
                W("FAIL: 图片面板检查异常: " + ex.GetType().Name + ": " + ex.Message);
                fail++;
                try { if (w != null) w.Close(); } catch { }
            }
        }

        /// <summary>
        /// 造一张 900x360 的测试图片，用于验证预览区的等比缩放与宽度自适应。
        /// 刻意用宽扁尺寸（2.5:1），这样"宽度撑满、高度按比例缩"的行为容易观察。
        /// </summary>
        private static string MakeProbeImage()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinLoop", "Media");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "_selftest_preview.png");

            // 用 DrawingVisual 画，不依赖 System.Drawing
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xFA)), null,
                    new Rect(0, 0, 900, 360));
                var fill = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)), 2);
                for (int i = 0; i < 10; i++)
                    dc.DrawRectangle(fill, edge, new Rect(20 + i * 86, 40, 70, 160));
                var fill2 = new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD));
                for (int i = 0; i < 9; i++)
                    dc.DrawRectangle(fill2, edge, new Rect(63 + i * 86, 230, 70, 90));
            }

            var bmp = new RenderTargetBitmap(900, 360, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);

            return path;
        }

        /// <summary>收集 TextBlock，同样走逻辑树 + 可视树双遍历。</summary>
        private static void CollectTextBlocks(DependencyObject root, System.Collections.Generic.List<TextBlock> acc)
        {
            if (root is TextBlock t) acc.Add(t);

            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is DependencyObject d) CollectTextBlocks(d, acc);
            }

            if (root is System.Windows.Media.Visual || root is System.Windows.Media.Media3D.Visual3D)
            {
                int c = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < c; i++)
                {
                    CollectTextBlocks(VisualTreeHelper.GetChild(root, i), acc);
                }
            }
        }

        /// <summary>
        /// 逐一选中四种菜单样式并截图。
        /// 通过反射点 RadioButton.IsChecked，避免依赖鼠标。
        /// </summary>
        private static void CheckMenuStyleVariants(string outDir, ref int fail, Action<string> W)
        {
            W("");
            W("--- 菜单样式变体 ---");

            var variants = new[]
            {
                new[] { "BasicRadialRadio", "圆环" },
                new[] { "CSHeadshotRadio", "八角星" },
                new[] { "SpiderWebRadio", "蜘蛛网" },
                new[] { "BaguaRadio", "八卦" },
            };

            foreach (var v in variants)
            {
                SettingsWindow w2 = null;
                try
                {
                    w2 = new SettingsWindow();
                    w2.WindowStartupLocation = WindowStartupLocation.Manual;
                    w2.Left = -32000; w2.Top = -32000;
                    w2.ShowInTaskbar = false;
                    w2.Show();
                    Pump();
                    w2.UpdateLayout();
                    Pump();

                    // 勾上对应的 RadioButton（走反射拿私有字段）
                    var field = typeof(SettingsWindow).GetField(v[0],
                        System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public);

                    if (field == null)
                    {
                        W("WARN: 找不到控件 " + v[0]);
                    }
                    else if (field.GetValue(w2) is System.Windows.Controls.RadioButton rb)
                    {
                        rb.IsChecked = true;
                        Pump();
                        w2.UpdateLayout();
                        Pump();
                    }

                    string file = Path.Combine(outDir, "style-" + v[1] + ".png");
                    RenderToPng(w2, file);

                    // 数一下这一页可见的颜色按钮（色块按钮），确认配色行都渲染出来了
                    int colorBtns = CountColorButtons(w2);
                    W(string.Format(CultureInfo.InvariantCulture,
                        "PASS: 样式「{0}」已渲染，颜色按钮 {1} 个 -> {2}",
                        v[1], colorBtns, Path.GetFileName(file)));

                    // ---- 关键断言：改色后按钮上的色值文字必须跟着变 ----
                    // 色值框已从界面移除，色值改由按钮 Content 承担。
                    // 这条绑定链断了的话，取色器选完颜色按钮上还是旧值 ——
                    // 而截图上看不出来（静态截图里色值总是"某个正确值"）。
                    CheckColorTextSync(w2, ref fail, W, v[1]);

                    w2.Close();
                }
                catch (Exception ex)
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "FAIL: 样式「{0}」渲染失败: {1}: {2}", v[1], ex.GetType().Name, ex.Message));
                    fail++;
                    try { if (w2 != null) w2.Close(); } catch { }
                }
            }
        }

        /// <summary>
        /// 验证「改色值 → 按钮文字跟着更新」这条绑定链。
        /// 做法：直接改隐藏文本框的 Text（等价于取色器回写），
        /// 然后把消息泵空，读按钮的 Content 是否同步。
        /// </summary>
        private static void CheckColorTextSync(SettingsWindow win, ref int fail, Action<string> W, string styleName)
        {
            var pairs = new[]
            {
                new[] { "BasicRingColorBox",          "BasicRingColorPickButton" },
                new[] { "BasicHighlightColorBox",     "BasicHighlightColorPickButton" },
                new[] { "SpiderWebLineColorBox",      "SpiderWebLineColorPickButton" },
                new[] { "SpiderWebHighlightColorBox", "SpiderWebHighlightColorPickButton" },
                new[] { "BaguaLineColorBox",          "BaguaLineColorPickButton" },
            };

            const string probe = "#1A2B3C";
            int checkedCount = 0, bad = 0;

            foreach (var p in pairs)
            {
                var box = FindByName(win, p[0]) as System.Windows.Controls.TextBox;
                var btn = FindByName(win, p[1]) as System.Windows.Controls.Button;
                if (box == null || btn == null) continue;

                // 只测当前样式下可见的那些（隐藏的面板不该参与）
                bool visible = IsReallyVisible(btn);
                if (!visible) continue;

                checkedCount++;

                string original = box.Text;
                box.Text = probe;
                Pump();

                string shown = btn.Content as string;
                if (shown != probe)
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "FAIL: [{0}] {1} 文字未跟随（期望 {2}，实际 {3}）",
                        styleName, p[1], probe, shown ?? "(null)"));
                    bad++;
                }

                // 还原，避免影响后续截图
                box.Text = original;
                Pump();
            }

            if (checkedCount == 0)
            {
                W(string.Format(CultureInfo.InvariantCulture,
                    "WARN: [{0}] 没有可见的色块按钮可测", styleName));
                return;
            }

            if (bad == 0)
            {
                W(string.Format(CultureInfo.InvariantCulture,
                    "PASS: [{0}] {1} 个色块按钮的色值文字均随文本框实时同步",
                    styleName, checkedCount));
            }
            else
            {
                fail += bad;
            }
        }

        private static bool IsReallyVisible(DependencyObject el)
        {
            var cur = el;
            while (cur != null)
            {
                if (cur is UIElement ue && ue.Visibility != Visibility.Visible) return false;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return true;
        }

        /// <summary>
        /// 按名字找控件。**可视树 + 逻辑树双遍历** ——
        /// TabControl 里未被选中的页只在逻辑树上，单走可视树会找不到。
        /// </summary>
        private static FrameworkElement FindByName(DependencyObject root, string name)
        {
            if (root is FrameworkElement fe && fe.Name == name) return fe;

            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is DependencyObject d)
                {
                    var found = FindByName(d, name);
                    if (found != null) return found;
                }
            }

            if (root is System.Windows.Media.Visual || root is System.Windows.Media.Media3D.Visual3D)
            {
                int c = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < c; i++)
                {
                    var found = FindByName(VisualTreeHelper.GetChild(root, i), name);
                    if (found != null) return found;
                }
            }

            return null;
        }

        /// <summary>统计可见的颜色按钮（ColorSwatchButton 用 MinWidth=146 的特征识别不靠谱，改按内容特征）。</summary>
        private static int CountColorButtons(DependencyObject root)
        {
            int n = 0;
            int c = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < c; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.Button b
                    && b.Visibility == Visibility.Visible
                    && b.Tag is string s
                    && s.StartsWith("#"))
                {
                    n++;
                }
                n += CountColorButtons(child);
            }
            return n;
        }

        /// <summary>把消息泵空，让布局/绑定/样式应用全部完成。</summary>
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new DispatcherOperationCallback(f => { ((DispatcherFrame)f).Continue = false; return null; }),
                frame);
            Dispatcher.PushFrame(frame);
        }

        private static TabControl FindTabControl(DependencyObject root)
        {
            if (root is TabControl tc) return tc;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var found = FindTabControl(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private static string TabHeader(TabControl tc, int i)
        {
            if (tc.Items[i] is TabItem ti) return ti.Header as string ?? "?";
            return "?";
        }

        /// <summary>统计可见控件数量，作为"这一页真的渲染出东西了"的粗粒度证据。</summary>
        private static int CountVisible(DependencyObject root)
        {
            int n = 0;
            int c = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < c; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is UIElement ue && ue.Visibility == Visibility.Visible)
                {
                    if (ue is TextBox || ue is Button || ue is ComboBox
                        || ue is CheckBox || ue is RadioButton) n++;
                }
                n += CountVisible(child);
            }
            return n;
        }

        private static void RenderToPng(Window win, string path)
        {
            int w = (int)Math.Ceiling(win.ActualWidth);
            int h = (int)Math.Ceiling(win.ActualHeight);
            if (w <= 0 || h <= 0) throw new InvalidOperationException("窗口尺寸无效: " + w + "x" + h);

            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(win);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        private static void Flush(StringBuilder sb, string outDir)
        {
            try
            {
                File.WriteAllText(Path.Combine(outDir, "report.txt"), sb.ToString(),
                    new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
