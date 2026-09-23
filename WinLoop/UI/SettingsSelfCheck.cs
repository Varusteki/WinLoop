using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinLoop.SystemIntegration;

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

            // ---- 4b) 样式选择 ↔ 参数面板联动 ----
            // 改默认样式（AppConfig.MenuStyle 初始值）最容易踩「单选换了、面板没换」，
            // 这一步取配置加载后、用户未做任何操作的状态，正是默认值生效的路径。
            CheckMenuStylePanelSync(ref fail, W);

            // ---- 5) 输入框：全部等宽 + 行内不再有任何单位/提示文字 ----
            CheckInputUniformity(ref fail, W);

            // ---- 6) 悬空寺图片面板：标题文案 + 预览区随宽度自适应 ----
            CheckXuanKongSiImagePanel(outDir, ref fail, W);

            // ---- 7) 效果预览：必须是贴在设置窗口右侧界外的独立衍生窗口 ----
            CheckPreviewWindow(outDir, ref fail, W);

            // ---- 8) 操作配置：三栏布局 + 折线引线 ----
            // 引线是纯绘制，做坏了不报错、界面也照开，只有几何断言兜得住。
            CheckOperationConfigLayout(outDir, ref fail, W);

            W(fail == 0 ? "结果: ALL PASS" : ("结果: FAILED (" + fail + ")"));
            Flush(sb, outDir);
        }

        /// <summary>
        /// 断言「效果预览」是贴在设置窗口**右侧界外**的独立衍生窗口。
        ///
        /// 为什么值得钉：预览原先只是设置窗口里的一张卡片，与参数挤在同一个滚动容器里，
        /// 用户必须下滑才能同时看到参数和预览 —— 调参数时眼睛恰恰需要盯着预览。
        /// 现在它是独立窗口。而这个窗口**静态截图拍不到**（自检只渲染设置窗口），
        /// 所以「紧贴主窗口可见右缘」「与主窗口可见框上下沿齐平」「窗内无标题」「画布尺寸」
        /// 「外侧圆角 + 贴靠侧直角」「底色切换」这几条契约只能靠这里兜住：
        /// 做坏了截图依旧好看，问题要等到用户手上才暴露。
        ///
        /// ⚠️ 圆角那两条尤其容易"看着改对了、其实白做" —— <c>CornerRadius</c> 与
        /// <c>AllowsTransparency</c> 是一对：只改前者，圆角四角会露出窗口自己的方形底；
        /// 而只圆外侧两角，是因为贴靠侧正好在接缝上，做成圆角会露出三角缺口。
        ///
        /// ⚠️ 两次返工都出在**基准**上，所以断言也一律改用 DWM 的**可见外框**：
        /// 第一次只把间距 12 → 0，第二次干脆改成外层矩形基准 —— 屏幕上那条 8~9px 的缝
        /// 始终都在，因为 WPF 报的外层矩形含 Win10 的不可见拖拽边框。
        /// **断言和定位必须看同一个矩形**，否则它护不住真正的问题。
        ///
        /// ⚠️ 本检查会**临时**把设置窗口摆回屏内：自检出于"不打扰用户"把窗口停在
        /// (-32000,-32000)（见 Run 开头），而那个坐标下"贴在右侧界外"根本无从判定。
        /// 断言完立刻移回屏外。全程不注入键鼠输入，只有几何断言 + 离屏渲染截图。
        /// </summary>
        private static void CheckPreviewWindow(string outDir, ref int fail, Action<string> W)
        {
            W("");
            W("--- 效果预览：衍生窗口 ---");

            SettingsWindow w = null;
            try
            {
                w = new SettingsWindow();
                w.ShowInTaskbar = false;
                // 摆到屏内一个明确位置，让"贴右界外"成为可判定的命题
                w.Left = 120;
                w.Top = 80;
                w.Show();
                Pump();
                w.UpdateLayout();
                Pump();

                var f = typeof(SettingsWindow).GetField("_previewWindow",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var pw = f == null ? null : f.GetValue(w) as Window;

                if (pw == null)
                {
                    W("FAIL: 预览衍生窗口未创建（_previewWindow 为 null）");
                    fail++;
                    return;
                }

                if (!pw.IsVisible)
                {
                    W("FAIL: 预览衍生窗口未显示（停在「菜单样式」页时应当显示）");
                    fail++;
                    return;
                }

                W(string.Format(CultureInfo.InvariantCulture,
                    "      主窗口 ({0:0.#},{1:0.#}) {2:0.#}x{3:0.#} | 预览窗口 ({4:0.#},{5:0.#}) {6:0.#}x{7:0.#}",
                    w.Left, w.Top, w.ActualWidth, w.ActualHeight,
                    pw.Left, pw.Top, pw.ActualWidth, pw.ActualHeight));

                // ---- ⓪ 主窗口的**可见外框**（DWM 扩展边框，换算成 DIP）----
                // 全部贴靠/等高断言都以它为基准。若拿 WPF 的外层矩形（Left+ActualWidth）
                // 当基准，断言会"通过"，而屏幕上仍留着约 8px 的隐形边框缝 ——
                // 也就是说断言必须和定位逻辑看同一个矩形，否则它护不住真正的问题。
                Rect vis = Rect.Empty;
                double scale = 1.0;
                try
                {
                    var src = PresentationSource.FromVisual(w);
                    if (src != null && src.CompositionTarget != null)
                    {
                        double m = src.CompositionTarget.TransformToDevice.M11;
                        if (!double.IsNaN(m) && !double.IsInfinity(m) && m > 0) scale = m;
                    }

                    Rect visPx;
                    if (WindowManagementService.TryGetVisibleFrameBounds(
                            new WindowInteropHelper(w).Handle, out visPx))
                    {
                        vis = new Rect(visPx.Left / scale, visPx.Top / scale,
                                       visPx.Width / scale, visPx.Height / scale);
                        W(string.Format(CultureInfo.InvariantCulture,
                            "      主窗口可见外框 ({0:0.#},{1:0.#}) {2:0.#}x{3:0.#}（外层 {4:0.#}x{5:0.#}，隐形边框 {6:0.#}px/边，DPI {7:0.##}）",
                            vis.Left, vis.Top, vis.Width, vis.Height,
                            w.ActualWidth, w.ActualHeight,
                            (w.ActualWidth - vis.Width) / 2.0, scale));
                    }
                    else
                    {
                        W("WARN: 取不到主窗口可见外框（DWM 不可用？）→ 断言退回外层矩形基准");
                    }
                }
                catch (Exception ex)
                {
                    W("WARN: 取可见外框异常 → 退回外层矩形基准: " + ex.Message);
                }

                // 基准矩形：正常路径用可见外框；拿不到才退回外层矩形
                Rect baseRect = vis.IsEmpty
                    ? new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight)
                    : vis;

                // ---- ① 水平：紧贴主窗口**可见右缘**，右侧放不下则翻到可见左缘 ----
                double aRight = baseRect.Right;
                const double gap = 0.0;
                var wa = SystemParameters.WorkArea;
                bool roomRight = (wa.Right - 4) - (aRight + gap) >= pw.ActualWidth;

                if (roomRight)
                {
                    if (Math.Abs(pw.Left - (aRight + gap)) < 0.5)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 紧贴主窗口可见右缘（Left={0:0.#} = 可见右缘 {1:0.#} + 间距 {2:0.#}）",
                            pw.Left, aRight, gap));
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 没有贴住可见右缘：期望 Left={0:0.#}，实际 {1:0.#}（差 {2:0.#}）",
                            aRight + gap, pw.Left, pw.Left - (aRight + gap)));
                        fail++;
                    }
                }
                else if (pw.Left + pw.ActualWidth <= baseRect.Left + 0.5)
                {
                    W("PASS: 右侧空间不足 → 已翻到主窗口可见左缘之外");
                }
                else
                {
                    W("FAIL: 右侧放不下，但既没翻到左侧、也没贴住主窗口");
                    fail++;
                }

                // ---- ② 垂直：与主窗口可见框**上下沿齐平**（等高 + 顶对齐）----
                // 判"顶对齐 + 等高"而不是判中线：等高之下两者等价，
                // 但分开判能立刻指出是"高度没跟上"还是"位置没跟上"。
                double dTop = Math.Abs(pw.Top - baseRect.Top);
                double dH = Math.Abs(pw.ActualHeight - baseRect.Height);
                if (dTop < 0.5 && dH < 0.5)
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "PASS: 与主窗口可见框上下沿齐平（Top={0:0.#}，高 {1:0.#}，底 {2:0.#}）",
                        pw.Top, pw.ActualHeight, pw.Top + pw.ActualHeight));
                }
                else
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "FAIL: 与可见框未齐平：顶差 {0:0.#}（预览 {1:0.#} vs 主窗 {2:0.#}）、高差 {3:0.#}（预览 {4:0.#} vs 主窗 {5:0.#}）",
                        dTop, pw.Top, baseRect.Top, dH, pw.ActualHeight, baseRect.Height));
                    fail++;
                }

                // ---- ②b 无标题：窗内不应再有「效果预览」那行标题 ----
                // 这行标题是**纯视觉元素**，删没删不会影响任何功能，静态截图也不参与断言，
                // 不钉住就会被后续改动悄悄加回来。
                var texts = new System.Collections.Generic.List<TextBlock>();
                CollectTextBlocks(pw, texts);
                bool hasTitle = false;
                foreach (var tb in texts)
                {
                    if (tb.Text != null && tb.Text.Trim() == "效果预览") { hasTitle = true; break; }
                }

                if (!hasTitle)
                {
                    W("PASS: 预览窗口内已无「效果预览」标题行");
                }
                else
                {
                    W("FAIL: 预览窗口里仍残留「效果预览」标题行");
                    fail++;
                }

                // ---- ③ 窗口尺寸：宽度固定 340，高度必须撑得下 300 画布 ----
                // 高度本身在上一条已与主窗口可见高做了齐平断言（620 高必然够装 300 画布）。
                if (Math.Abs(pw.ActualWidth - 340) < 0.5 && pw.ActualHeight >= 340)
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "PASS: 窗口尺寸 {0:0.#}x{1:0.#}（宽 340 固定，高足以容纳 300 画布）",
                        pw.ActualWidth, pw.ActualHeight));
                }
                else
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "FAIL: 窗口尺寸异常 {0:0.#}x{1:0.#}（期望宽 340、高 ≥340）",
                        pw.ActualWidth, pw.ActualHeight));
                    fail++;
                }

                // ---- ④ 画布：300×300，且与运行时的 DIP 基准一致（不做 DPI 缩放）----
                var canvas = FindByName(pw, "PreviewCanvas") as Canvas;
                if (canvas == null)
                {
                    W("FAIL: 衍生窗口里找不到 PreviewCanvas");
                    fail++;
                }
                else if (Math.Abs(canvas.Width - 300) < 0.5 && Math.Abs(canvas.Height - 300) < 0.5)
                {
                    W("PASS: 预览画布 300x300");
                }
                else
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "FAIL: 预览画布尺寸异常 {0:0.#}x{1:0.#}（期望 300x300）", canvas.Width, canvas.Height));
                    fail++;
                }

                // ---- ④b 圆角修饰：外侧（右）两角圆角、贴靠侧（左）保持直角，且必须是分层窗口 ----
                // 两件事必须一起钉，少一件圆角就等于白做：
                //   · 只圆右侧 —— 左侧两角在接缝上，做了圆角会在主窗口可见右缘处各露出
                //     一个 8px 的三角缺口（透出桌面），比直角难看得多；
                //   · AllowsTransparency 必须为 true —— 不开分层窗口时，圆角四角会露出
                //     窗口自己的方形底，"圆角"看起来就是一块直角底板压着圆弧。
                var cardBorder = FindByName(pw, "CardBorder") as Border;
                if (cardBorder == null)
                {
                    W("FAIL: 预览窗口里找不到外层卡片 CardBorder");
                    fail++;
                }
                else if (cardBorder.CornerRadius.TopRight > 0
                         && cardBorder.CornerRadius.BottomRight > 0
                         && cardBorder.CornerRadius.TopLeft <= 0
                         && cardBorder.CornerRadius.BottomLeft <= 0)
                {
                    if (pw.AllowsTransparency)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 外侧圆角 {0:0.#}px + 贴靠侧直角（CornerRadius={1}），且为分层窗口（AllowsTransparency=true）",
                            cardBorder.CornerRadius.TopRight, cardBorder.CornerRadius));
                    }
                    else
                    {
                        W("FAIL: 设了 CornerRadius 但 AllowsTransparency=false —— 圆角四角会露出窗口方形底，圆角等于白做");
                        fail++;
                    }
                }
                else
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "FAIL: 圆角不符合约定：期望外侧两角 >0、贴靠侧两角 =0，实际 CornerRadius={0}",
                        cardBorder.CornerRadius));
                    fail++;
                }

                // ---- ④c 预览底色切换（白色 / 黑色）----
                // 这是本窗口唯一的交互控件，用途是检查"菜单在深色桌面上还看不看得清"。
                // 断言走**直接调方法**，不模拟鼠标点击 —— 模拟输入在无人值守环境不可靠，
                // 而这里本来就没有必须走鼠标的理由（Click 只是 SetPreviewBackground 的薄封装）。
                var mpw = pw as MenuPreviewWindow;
                if (mpw == null)
                {
                    W("FAIL: 预览窗类型不是 MenuPreviewWindow，无法验证底色切换");
                    fail++;
                }
                else
                {
                    bool hasToggle = FindByName(pw, "BgLightRadio") != null
                                  && FindByName(pw, "BgDarkRadio") != null;
                    if (hasToggle)
                    {
                        W("PASS: 底部有「白色背景 / 黑色背景」两个切换按钮");
                    }
                    else
                    {
                        W("FAIL: 找不到底色切换按钮（BgLightRadio / BgDarkRadio）");
                        fail++;
                    }

                    var bgBorder = FindByName(pw, "PreviewBgBorder") as Border;

                    // ---- 切到黑色 ----
                    mpw.SetPreviewBackground(true);
                    Pump();
                    pw.UpdateLayout();
                    Pump();

                    string err = null;
                    double lum = -1;
                    var sbr = bgBorder == null ? null : bgBorder.Background as SolidColorBrush;
                    if (sbr == null)
                    {
                        err = "底色块 PreviewBgBorder 或其 Background 取不到";
                    }
                    else
                    {
                        lum = (sbr.Color.R + sbr.Color.G + sbr.Color.B) / 3.0;
                        if (!mpw.IsDarkPreviewBackground) err = "IsDarkPreviewBackground 未变为 true";
                        else if (lum > 64) err = string.Format(CultureInfo.InvariantCulture, "底色不够黑（亮度 {0:0}）", lum);
                    }

                    if (err == null)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 切「黑色背景」→ 画布底色变深色（亮度 {0:0}）", lum));
                    }
                    else
                    {
                        W("FAIL: 切「黑色背景」未生效：" + err);
                        fail++;
                    }

                    // 深色状态单独留一张截图（浅色状态由最后那张 preview-window.png 覆盖）
                    RenderToPng(pw, Path.Combine(outDir, "preview-window-dark.png"));
                    W("      截图 -> preview-window-dark.png");

                    // ---- 切回白色并复核，避免把深色状态留给后面的断言与截图 ----
                    mpw.SetPreviewBackground(false);
                    Pump();
                    pw.UpdateLayout();
                    Pump();

                    var sbr2 = bgBorder == null ? null : bgBorder.Background as SolidColorBrush;
                    double lum2 = sbr2 == null ? -1 : (sbr2.Color.R + sbr2.Color.G + sbr2.Color.B) / 3.0;
                    if (!mpw.IsDarkPreviewBackground && lum2 > 191)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 切回「白色背景」→ 画布底色变浅色（亮度 {0:0}）", lum2));
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 切回「白色背景」未生效（亮度 {0:0}，IsDark={1}）", lum2, mpw.IsDarkPreviewBackground));
                        fail++;
                    }
                }

                // ---- ⑤ 切到别的页必须隐藏（预览只属于「菜单样式」页）----
                var tabs = FindByName(w, "MainTabControl") as TabControl;
                if (tabs != null && tabs.Items.Count > 1)
                {
                    tabs.SelectedIndex = 1;
                    Pump();
                    w.UpdateLayout();
                    Pump();

                    if (!pw.IsVisible)
                    {
                        W("PASS: 切到「操作配置」页后预览窗口已隐藏");
                    }
                    else
                    {
                        W("FAIL: 切页后预览窗口仍然显示（预览只应出现在「菜单样式」页）");
                        fail++;
                    }

                    tabs.SelectedIndex = 0;
                    Pump();
                    w.UpdateLayout();
                    Pump();

                    if (pw.IsVisible)
                    {
                        W("PASS: 切回「菜单样式」页后预览窗口恢复显示");
                    }
                    else
                    {
                        W("FAIL: 切回「菜单样式」页后预览窗口没有恢复");
                        fail++;
                    }
                }

                // ---- ⑥ 离屏渲染截图（只看，不注入输入）----
                RenderToPng(pw, Path.Combine(outDir, "preview-window.png"));
                W("      截图 -> preview-window.png");
            }
            catch (Exception ex)
            {
                W("FAIL: 预览衍生窗口检查抛异常: " + ex.Message);
                fail++;
            }
            finally
            {
                // 移回屏外再关，保持"自检不打扰用户"的既有约定
                try
                {
                    if (w != null)
                    {
                        w.Left = -32000;
                        w.Top = -32000;
                        w.Close();
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 断言「被勾选的样式」与「显示出来的参数面板」严格一致。
        ///
        /// 四个参数面板（圆环尺寸 / Headshot 大小 / 蜘蛛网 / 八卦）互斥显示，
        /// 界面里任何时候只能看到一组 —— 看错了组，用户会以为设置项丢了。
        ///
        /// **为什么单独立一条**：改默认菜单样式（<c>AppConfig.MenuStyle</c> 的初始值）
        /// 最容易踩的就是这里 —— 单选按钮换了，参数面板却没跟着换。
        /// 本断言取的是「配置加载完成、用户还没点任何东西」的状态，
        /// 正好就是默认值生效的那条路径。
        /// </summary>
        /// <summary>
        /// 断言「操作配置」页的三栏布局与引线。
        ///
        /// 为什么值得钉：这一页从「左预览 + 右单个下拉框」改成了
        /// 「左列 4 行 + 中间菜单 + 右列 4 行 + 引线」。引线是**纯绘制** ——
        /// 做坏了界面照样能开、配置照样能改，只是用户看不出哪一行对应哪一格。
        /// 更麻烦的是：**截图上"有线"也不代表连对了**，线连到隔壁扇区截图一样好看。
        /// 所以这里量的是几何：每条线末端点相对菜单中心的方向角，必须等于它那一格
        /// 扇区的中心角（-90° + 45°×i）。
        ///
        /// ⚠️ 2026-09-22 行内的「位置N · 方位」文字已按用户要求删除 —— 引线从"辅助标注"
        /// 变成了这一页**唯一**的对应关系表达。也就是说下面那几条方向角断言不再是锦上添花，
        /// 而是"这页还能不能读"的底线，别删也别放宽。
        ///
        /// ⚠️ 引线是在布局完成后按 Background 优先级补画的，断言前必须多泵一轮 ——
        /// 只泵一次时引线还没画，下面的断言会全部误报 FAIL（真跑过一次）。
        /// </summary>
        private static void CheckOperationConfigLayout(string outDir, ref int fail, Action<string> W)
        {
            W("");
            W("--- 操作配置：三栏布局 + 引线 ---");

            SettingsWindow w = null;
            try
            {
                w = new SettingsWindow();
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -32000; w.Top = -32000;
                w.ShowInTaskbar = false;
                w.Show();
                Pump();
                w.UpdateLayout();
                Pump();

                var tabs = FindByName(w, "MainTabControl") as TabControl;
                var grid = FindByName(w, "OpLayoutGrid") as Grid;
                var canvas = FindByName(w, "OpPreviewCanvas") as Canvas;
                var layer = FindByName(w, "OpConnectorLayer") as Canvas;
                var leftList = FindByName(w, "OpLeftList") as Panel;
                var rightList = FindByName(w, "OpRightList") as Panel;

                if (tabs == null || grid == null || canvas == null || layer == null
                    || leftList == null || rightList == null)
                {
                    W("FAIL: 操作配置页的三栏控件缺失（需要 OpLayoutGrid / OpPreviewCanvas / "
                      + "OpConnectorLayer / OpLeftList / OpRightList）");
                    fail++;
                    return;
                }

                tabs.SelectedIndex = 1;
                Pump();
                w.UpdateLayout();
                Pump();
                // 引线按 Background 优先级补画 —— 再泵一轮，否则量到的永远是"还没画"
                Pump();
                w.UpdateLayout();
                Pump();

                // 注：操作配置页的截图放在下面的"四样式循环"里逐张出，
                // 不在这里另拍一张 —— 那时样式还是配置里的旧值，与循环里的对不上。

                // ---- ① 8 个选区行 + 8 个下拉框齐备且可见 ----
                var rows = new Border[8];
                var combos = new ComboBox[8];
                int missing = 0, emptyCombo = 0;
                for (int i = 0; i < 8; i++)
                {
                    rows[i] = FindByName(w, "OpRowP" + (i + 1)) as Border;
                    combos[i] = FindByName(w, "OpComboP" + (i + 1)) as ComboBox;

                    if (rows[i] == null || combos[i] == null
                        || rows[i].ActualWidth <= 0.5 || !IsReallyVisible(rows[i]))
                    {
                        missing++;
                        continue;
                    }
                    // 回填没跑的话所有下拉框都是空的 —— 界面上看不出"空"和"值"的区别
                    // 只有真点开才知道，所以这条值得单独钉
                    if (combos[i].SelectedValue == null) emptyCombo++;
                }

                if (missing == 0)
                {
                    W("PASS: 8 个选区行与 8 个动作下拉框齐备且可见");
                }
                else
                {
                    W("FAIL: 有 " + missing + " 个选区行/下拉框缺失或不可见");
                    fail++;
                }

                if (emptyCombo == 0)
                {
                    W("PASS: 8 个下拉框均已按配置回填（无空选）");
                }
                else
                {
                    W("FAIL: 有 " + emptyCombo + " 个下拉框没有回填选中项");
                    fail++;
                }

                // ---- ② 行内不应再有位置文字，卡片顶部也不应有「扇区动作」组标题 ----
                // 两条都是**纯视觉、删没删都不影响功能**的条款，最容易被后续改动顺手加回来
                // （"补个标题更清楚吧"），所以各钉一条 —— 同 CheckPreviewWindow 里
                // "预览窗内无标题行"的做法。
                //
                // 判据分两层：① 按旧 Name 找（加回来时一般会沿用旧名）；
                // ② 在 OpLayoutGrid 内按文本特征扫（防改用新 Name 复活）。
                // 而「扇区动作」在卡片容器上、不在 grid 里，所以那一句扫整个窗口。
                int titleLeft = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (FindByName(w, "OpRowTitleP" + (i + 1)) != null) titleLeft++;
                }

                var gridTexts = new System.Collections.Generic.List<TextBlock>();
                CollectTextBlocks(grid, gridTexts);
                var posTexts = new System.Collections.Generic.List<string>();
                foreach (var t in gridTexts)
                {
                    string s = t.Text ?? "";
                    if (s.StartsWith("位置") && s.Contains("·")) posTexts.Add(s);
                }

                var allTexts = new System.Collections.Generic.List<TextBlock>();
                CollectTextBlocks(w, allTexts);
                bool hasGroupTitle = false;
                foreach (var t in allTexts)
                {
                    if ((t.Text ?? "") == "扇区动作") { hasGroupTitle = true; break; }
                }

                if (titleLeft == 0 && posTexts.Count == 0 && !hasGroupTitle)
                {
                    W("PASS: 行内已无「位置N · 方位」文字，卡片顶部也无「扇区动作」组标题");
                }
                else
                {
                    W("FAIL: 位置文字/组标题仍有残留 —— 按 Name 找到 " + titleLeft + " 个，"
                      + "格子内文本命中 " + posTexts.Count + " 处"
                      + (posTexts.Count > 0 ? "（例：" + posTexts[0] + "）" : "")
                      + "，组标题=" + (hasGroupTitle ? "仍在" : "已删"));
                    fail++;
                }

                // ---- ②b 页面大标题「操作配置」与卡片底部那行提示同样应已删除 ----
                // （2026-09-22 第二轮：用户要求去掉标题文案与底部提示。）
                // ⚠️ 判据必须**限定在这一页的内容里**：左侧导航那一项的文字也是「操作配置」
                //（它渲染出来同样是 TextBlock），扫整个窗口会误判成"标题还在"。
                {
                    var pageItem = tabs.Items.Count > 1 ? tabs.Items[1] as TabItem : null;
                    var pageRoot = pageItem == null ? null : pageItem.Content as DependencyObject;

                    int pageTitleHits = 0, pageHintHits = 0;
                    if (pageRoot != null)
                    {
                        var pageTexts = new System.Collections.Generic.List<TextBlock>();
                        CollectTextBlocks(pageRoot, pageTexts);
                        foreach (var t in pageTexts)
                        {
                            string s = t.Text ?? "";
                            if (s.Trim() == "操作配置") pageTitleHits++;
                            if (s.Contains("引线指向菜单")) pageHintHits++;
                        }
                    }

                    if (pageRoot == null)
                    {
                        W("FAIL: 取不到「操作配置」页的内容根，无法验证标题/提示是否已删");
                        fail++;
                    }
                    else if (pageTitleHits == 0 && pageHintHits == 0)
                    {
                        W("PASS: 页面大标题「操作配置」与底部提示文案均已删除");
                    }
                    else
                    {
                        W("FAIL: 删除不彻底 —— 页内「操作配置」标题 " + pageTitleHits
                          + " 处、底部提示 " + pageHintHits + " 处");
                        fail++;
                    }
                }

                // ---- ③ 菜单居中于左右两列之间 ----
                var canvasRect = canvas.TransformToAncestor(grid)
                    .TransformBounds(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight));
                var leftRect = leftList.TransformToAncestor(grid)
                    .TransformBounds(new Rect(0, 0, leftList.ActualWidth, leftList.ActualHeight));
                var rightRect = rightList.TransformToAncestor(grid)
                    .TransformBounds(new Rect(0, 0, rightList.ActualWidth, rightList.ActualHeight));

                double canvasMid = (canvasRect.Left + canvasRect.Right) / 2.0;
                double colMid = (leftRect.Right + rightRect.Left) / 2.0;
                if (Math.Abs(canvasMid - colMid) < 1.0)
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "PASS: 菜单居中于左右两列之间（画布中线 {0:0.#} = 两列中线 {1:0.#}）",
                        canvasMid, colMid));
                }
                else
                {
                    W(string.Format(CultureInfo.InvariantCulture,
                        "FAIL: 菜单未居中：画布中线 {0:0.#}，两列中线 {1:0.#}（差 {2:0.#}px）",
                        canvasMid, colMid, canvasMid - colMid));
                    fail++;
                }

                // ---- ④ 左右分组：Position1~4 在右列、5~8 在左列 ----
                int sideBad = 0;
                string sideWhat = "";
                for (int i = 0; i < 8; i++)
                {
                    if (rows[i] == null || rows[i].ActualWidth <= 0.5) continue;

                    var rr = rows[i].TransformToAncestor(grid)
                        .TransformBounds(new Rect(0, 0, rows[i].ActualWidth, rows[i].ActualHeight));
                    double mid = (rr.Left + rr.Right) / 2.0;

                    bool expectRight = i < 4;
                    bool isRight = mid > canvasRect.Right;
                    bool isLeft = mid < canvasRect.Left;

                    if (!(expectRight ? isRight : isLeft))
                    {
                        sideBad++;
                        if (sideBad == 1)
                        {
                            sideWhat = "位置" + (i + 1) + " 应在" + (expectRight ? "右" : "左")
                                     + "列（行中线 " + mid.ToString("F0", CultureInfo.InvariantCulture)
                                     + "，画布 " + canvasRect.Left.ToString("F0", CultureInfo.InvariantCulture)
                                     + "~" + canvasRect.Right.ToString("F0", CultureInfo.InvariantCulture) + "）";
                        }
                    }
                }
                if (sideBad == 0)
                {
                    W("PASS: 左列 4 行为左半侧扇区（左/左上/左下/下），右列 4 行为右半侧（上/右上/右/右下）");
                }
                else
                {
                    W("FAIL: " + sideBad + " 行站错了列 —— " + sideWhat);
                    fail++;
                }

                // ---- ⑤ 引线：8 条 Path，结构必须是「1 段水平直线」或「3 段（横 + 竖 + 横）」----
                // 判据有三层，缺一层就会被后面某轮改动静默带偏：
                //   a. 每段必须水平或垂直（拦"斜线回潮"）—— 光数段数拦不住，斜线版同样是合法段数；
                //   b. 段数只许 1 或 3（拦"悄悄把走廊/引出段拿掉"或"多塞一段"）；
                //   c. **形态字符串**只许 "H" 或 "HVH"。这一条是 2026-09-22 第四轮的关键：
                //      起点固定在下拉框侧边**中点**（用户标红线要求），线必须先**水平**从框里出来，
                //      所以首段与末段都必须是水平段、竖段只许夹在中间。反成 "VH"（虚线段贴着
                //      框边走，像给框描边）或 "HV"（缺水平接入段）都会被这条拦下。
                // 圆角回归（含 Bezier / Arc 段）也在这里一并拦下。
                //
                // 单段直线是**退化形态**而不是例外：锚点与起点同高时只剩一段水平直线
                // （见 UpdateOpConnectors / BuildOpConnectorGeometry）。
                // 所以下面这条 PASS 要把"1 段几条、3 段几条、合计几段"都报出来 ——
                // 段数变多就是这页在退化。
                int lineCount = 0, shapedBad = 0, straightOnly = 0, threeSegOnly = 0;
                int roundedBack = 0, skewLines = 0, orderReversed = 0;
                double maxHSeg = 0, maxVSeg = 0;
                var orderedPaths = new System.Windows.Shapes.Path[8];
                var segCount = new int[8];          // 每条线的段数，报告里一并打出来便于定位
                foreach (var child in layer.Children)
                {
                    var lp = child as System.Windows.Shapes.Path;
                    if (lp == null) continue;
                    if (lineCount < 8) orderedPaths[lineCount] = lp;
                    lineCount++;

                    var g = lp.Data as PathGeometry;
                    PathFigure f = (g != null && g.Figures.Count == 1) ? g.Figures[0] : null;

                    bool healthy = f != null && (f.Segments.Count == 1 || f.Segments.Count == 3);
                    int curves = 0, skew = 0, orderBad = 0;
                    string shape = "";

                    if (healthy)
                    {
                        // 先给每段定方向：H 水平 / V 竖直 / S 斜 / . 零长 / ? 非直线段
                        var dirs = new char[f.Segments.Count];
                        Point cur = f.StartPoint;
                        for (int k = 0; k < f.Segments.Count; k++)
                        {
                            var ls = f.Segments[k] as LineSegment;
                            if (ls == null)
                            {
                                curves++;                       // Bezier / Arc = 圆角转弯被加回来了
                                dirs[k] = '?';
                                continue;
                            }
                            double dx = Math.Abs(ls.Point.X - cur.X);
                            double dy = Math.Abs(ls.Point.Y - cur.Y);
                            if (dx < 0.6 && dy < 0.6) dirs[k] = '.';        // 零长段（去重逻辑本该防住）
                            else if (dx < 0.6)
                            {
                                dirs[k] = 'V';
                                if (dy > maxVSeg) maxVSeg = dy;
                            }
                            else if (dy < 0.6)
                            {
                                dirs[k] = 'H';
                                if (dx > maxHSeg) maxHSeg = dx;
                            }
                            else { dirs[k] = 'S'; skew++; }                 // 斜段
                            cur = ls.Point;
                        }

                        shape = new string(dirs);
                        bool shapeOk = f.Segments.Count == 1 ? (shape == "H") : (shape == "HVH");
                        if (!shapeOk) orderBad++;
                    }

                    if (f != null)
                    {
                        if (lineCount - 1 >= 0 && lineCount - 1 < 8) segCount[lineCount - 1] = f.Segments.Count;
                        if (f.Segments.Count == 1) straightOnly++;
                        else if (f.Segments.Count == 3) threeSegOnly++;
                    }

                    if (!healthy || curves > 0 || skew > 0 || orderBad > 0)
                    {
                        shapedBad++;
                        if (curves > 0) roundedBack++;
                        if (skew > 0) skewLines++;
                        if (orderBad > 0) orderReversed++;
                    }
                }

                if (lineCount == 8 && shapedBad == 0)
                {
                    W("PASS: 引线 8 条齐备，结构合法 —— 全部正交（只有水平/垂直段）；"
                      + "单段水平直线 " + straightOnly + " 条、三段（横+竖+横）" + threeSegOnly + " 条"
                      + "（合计 " + (straightOnly + 3 * threeSegOnly) + " 段）；"
                      + "各位置段数 " + string.Join(" ", Array.ConvertAll(segCount,
                            v => v.ToString(CultureInfo.InvariantCulture))) + "；"
                      + "最长水平段 " + maxHSeg.ToString("0.#", CultureInfo.InvariantCulture)
                      + "px、最长竖直段 " + maxVSeg.ToString("0.#", CultureInfo.InvariantCulture) + "px");
                }
                else
                {
                    W("FAIL: 引线结构异常 —— 实际 " + lineCount + " 条，其中 " + shapedBad
                      + " 条不合法（" + roundedBack + " 条含曲线段=圆角转弯被加回来了；"
                      + skewLines + " 条含斜段=没有走正交走线；"
                      + orderReversed + " 条段序不对=形态须为 H 或 H-V-H"
                      + "（H 开头才说明线是从下拉框侧边中点**水平**出来的）；段数须为 1 或 3）");
                    fail++;
                }

                // ---- ⑤b 起点必须是**下拉框侧边的中点**（2026-09-22 第四轮，用户标红线要求）----
                // 这一条是"这条线属于哪一行"的唯一凭据（行内已无位置文字），所以起点必须
                // ① 横向上贴住下拉框的朝内边缘（框边 ∓ OpEdgeGap）、
                // ② 纵向上落在框侧边的**正中间**（±0.6px）。
                // 第三轮曾让起点"沿边缘滑到离锚点最近的高度"以省段数（24 → 12），
                // 代价是起点滑到框的上下角上、看着不像从这一行引出来的 —— 用户标红线否掉了。
                // 谁再把滑动加回来，这条断言会立刻 FAIL。
                // ⚠️ 判据必须落在**下拉框**上、不是行 Border 上：行 Border 带内边距，
                // 比下拉框宽一圈，按行算会让线头悬在行的留白里（2026-09-22 踩过）。
                {
                    int startBad = 0;
                    string startWhat = "";
                    var colStartX = new double[2];
                    var colStartN = new int[2];
                    double boxH = 0;                    // 下拉框高，报告里打出来便于核对
                    double maxOffset = 0;               // 起点偏离框侧边中点的最大量（应为 0）
                    double edgeGap = ReflectStaticConst("OpEdgeGap", 2.0);

                    for (int i = 0; i < 8; i++)
                    {
                        var lp = orderedPaths[i];
                        if (lp == null) continue;
                        if (combos[i] == null || combos[i].ActualWidth <= 0.5) continue;

                        var gg = lp.Data as PathGeometry;
                        var ff = (gg != null && gg.Figures.Count == 1) ? gg.Figures[0] : null;
                        if (ff == null) continue;

                        Rect boxRect;
                        try
                        {
                            boxRect = combos[i].TransformToAncestor(grid)
                                .TransformBounds(new Rect(0, 0, combos[i].ActualWidth, combos[i].ActualHeight));
                        }
                        catch { continue; }

                        // 引线层与下拉框都是 OpLayoutGrid 的直接子级（兄弟），坐标系相同 ——
                        // 起点坐标可以直接和 Rect 比，不需要再变换一次。
                        boxH = boxRect.Height;
                        double sx = ff.StartPoint.X, sy = ff.StartPoint.Y;
                        double expectX = i >= 4 ? boxRect.Right + edgeGap : boxRect.Left - edgeGap;
                        double midY = boxRect.Top + boxRect.Height / 2.0;

                        if (Math.Abs(sx - expectX) > 1.0)
                        {
                            startBad++;
                            if (startWhat.Length == 0)
                            {
                                startWhat = "位置" + (i + 1) + " 起点 x="
                                          + sx.ToString("F1", CultureInfo.InvariantCulture)
                                          + "，应贴住下拉框的朝内边缘 "
                                          + expectX.ToString("F1", CultureInfo.InvariantCulture);
                            }
                        }

                        if (Math.Abs(sy - midY) > 0.6)
                        {
                            startBad++;
                            if (startWhat.Length == 0)
                            {
                                startWhat = "位置" + (i + 1) + " 起点 y="
                                          + sy.ToString("F1", CultureInfo.InvariantCulture)
                                          + "，不在下拉框侧边中点 "
                                          + midY.ToString("F1", CultureInfo.InvariantCulture)
                                          + "（框 " + boxRect.Top.ToString("F1", CultureInfo.InvariantCulture)
                                          + "~" + boxRect.Bottom.ToString("F1", CultureInfo.InvariantCulture)
                                          + "，高 " + boxH.ToString("F1", CultureInfo.InvariantCulture)
                                          + "）—— 起点不许沿框边滑动";
                            }
                        }
                        else if (Math.Abs(sy - midY) > maxOffset) maxOffset = Math.Abs(sy - midY);

                        int side = i >= 4 ? 0 : 1;                          // 0=左列 1=右列
                        if (colStartN[side] == 0) colStartX[side] = sx;
                        colStartN[side]++;
                    }

                    if (startBad == 0 && colStartN[0] + colStartN[1] == 8)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 8 条引线的起点全部落在各自下拉框侧边的**中点** —— "
                            + "左列同在 x={0:0.#}、右列同在 x={1:0.#}（框边 ∓{2:0.#}px）；"
                            + "下拉框高 {3:0.#}，偏离中点最大 {4:0.#}px",
                            colStartX[0], colStartX[1], edgeGap, boxH, maxOffset));
                    }
                    else
                    {
                        W("FAIL: 引线起点不合法（" + startBad + " 处）—— " + startWhat);
                        fail++;
                    }
                }

                // ---- ⑤c 8 条引线两两不重叠（2026-09-22 用户明确要求）----
                // 两种重叠都要拦：
                //   · 竖直段：同列 4 条的 x 相同（都落在本列那条走廊上），y 区间一旦相交就是两条线叠在一起；
                //   · 水平段：引出段都在**行中心**高度、接入段都在**锚点**高度；同一高度上的两条线，
                //     x 区间一旦相交就是叠在一起（左右两列各有 4 条，同高度的必须分居图形两侧）。
                // ⚠️ 第四轮起每条线有**两段水平段**（引出 + 接入），所以必须逐段收集、不能只记最后一段 ——
                //    只记最后一段会把"引出段叠了"整片漏掉。
                {
                    var vseg = new System.Collections.Generic.List<double[]>[8];   // [x, y1, y2]
                    var hseg = new System.Collections.Generic.List<double[]>[8];   // [y, x1, x2]
                    for (int i = 0; i < 8; i++)
                    {
                        vseg[i] = new System.Collections.Generic.List<double[]>();
                        hseg[i] = new System.Collections.Generic.List<double[]>();
                    }

                    for (int i = 0; i < 8; i++)
                    {
                        var lp = orderedPaths[i];
                        if (lp == null) continue;

                        var gg = lp.Data as PathGeometry;
                        var ff = (gg != null && gg.Figures.Count == 1) ? gg.Figures[0] : null;
                        if (ff == null) continue;

                        Point cur = ff.StartPoint;
                        foreach (var seg in ff.Segments)
                        {
                            var ls = seg as LineSegment;
                            if (ls == null) continue;
                            Point nx = ls.Point;
                            double dx = Math.Abs(nx.X - cur.X), dy = Math.Abs(nx.Y - cur.Y);

                            if (dx < 0.6 && dy >= 0.6)
                                vseg[i].Add(new[] { cur.X, Math.Min(cur.Y, nx.Y), Math.Max(cur.Y, nx.Y) });
                            else if (dy < 0.6 && dx >= 0.6)
                                hseg[i].Add(new[] { cur.Y, Math.Min(cur.X, nx.X), Math.Max(cur.X, nx.X) });
                            cur = nx;
                        }
                    }

                    int ovl = 0;
                    string ovlWhat = "";
                    for (int a = 0; a < 8; a++)
                    {
                        for (int b = a + 1; b < 8; b++)
                        {
                            foreach (var s1 in vseg[a])
                            {
                                foreach (var s2 in vseg[b])
                                {
                                    if (Math.Abs(s1[0] - s2[0]) >= 0.6) continue;
                                    double lo = Math.Max(s1[1], s2[1]), hi = Math.Min(s1[2], s2[2]);
                                    if (hi - lo > 0.5)
                                    {
                                        ovl++;
                                        if (ovlWhat.Length == 0)
                                        {
                                            ovlWhat = "位置" + (a + 1) + " 与位置" + (b + 1) + " 的竖直段叠了 "
                                                    + (hi - lo).ToString("0.#", CultureInfo.InvariantCulture) + "px";
                                        }
                                    }
                                }
                            }

                            foreach (var s1 in hseg[a])
                            {
                                foreach (var s2 in hseg[b])
                                {
                                    if (Math.Abs(s1[0] - s2[0]) >= 0.6) continue;
                                    double lo = Math.Max(s1[1], s2[1]), hi = Math.Min(s1[2], s2[2]);
                                    if (hi - lo > 0.5)
                                    {
                                        ovl++;
                                        if (ovlWhat.Length == 0)
                                        {
                                            ovlWhat = "位置" + (a + 1) + " 与位置" + (b + 1) + " 的水平段叠了 "
                                                    + (hi - lo).ToString("0.#", CultureInfo.InvariantCulture) + "px";
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (ovl == 0)
                    {
                        W("PASS: 8 条引线两两不重叠（含每条两段水平段的比对）");
                    }
                    else
                    {
                        W("FAIL: 引线重叠 " + ovl + " 处 —— " + ovlWhat);
                        fail++;
                    }
                }

                // ---- ⑤e 竖直段必须落在两列各一条的**走廊**上（第四轮的新契约）----
                // 起点被钉在下拉框侧边中点之后，"起点高度"不再可调，锚点 y 与它不等时只能靠
                // 走廊上的竖段把高度差走完。走廊 x = 菜单中心 ±(锚点环半径 + OpCorridorLead)，
                // 由**锚点环半径推导**（不是写死像素）—— 所以这里量锚点环、再核对走廊：
                //   ① 同列所有竖直段的 x 必须共线（各自为政 → 看着乱）；
                //   ② 两列相对菜单中心左右对称；
                //   ③ 走廊落在锚点环之外、也在图形可见外缘之外（LineOfSight：走廊不能压到图案上）。
                {
                    // 菜单中心（**图形**中心，不是画布中心 —— 各样式的 Width 未必等于 VisualRadius×2）。
                    Point ctr = new Point(double.NaN, double.NaN);
                    try
                    {
                        var mf = typeof(SettingsWindow).GetField("_opPreviewMenu",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        object mo = mf == null ? null : mf.GetValue(w);
                        var mel = mo as FrameworkElement;
                        var pr = mo == null ? null : mo.GetType().GetProperty("VisualCenter");
                        if (mel != null && pr != null)
                            ctr = mel.TranslatePoint((Point)pr.GetValue(mo, null), grid);
                    }
                    catch { }

                    // 锚点环半径：直接用位置 1（正上）那条线的**终点**量 —— 它本身就是被 ⑥⑦
                    // 的方向角断言钉住的量，拿它推走廊的期望值，两边不会有第二套真值。
                    var ringDist = new double[8];
                    int gotRing = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        var lp = orderedPaths[i];
                        if (lp == null) continue;
                        var gg = lp.Data as PathGeometry;
                        var ff = (gg != null && gg.Figures.Count == 1) ? gg.Figures[0] : null;
                        if (ff == null) continue;

                        Point end = ff.StartPoint;
                        foreach (var seg in ff.Segments)
                        {
                            var ls = seg as LineSegment;
                            if (ls != null) end = ls.Point;
                        }
                        ringDist[i] = Math.Sqrt((end.X - ctr.X) * (end.X - ctr.X) + (end.Y - ctr.Y) * (end.Y - ctr.Y));
                        gotRing++;
                    }

                    double[] colVx = new double[2];  colVx[0] = colVx[1] = double.NaN;
                    var colVn = new int[2];
                    int colVN = 0, colSpread = 0;
                    double maxDev = 0;
                    if (gotRing == 8 && !double.IsNaN(ctr.X))
                    {
                        double ring = (ringDist[0] + ringDist[1] + ringDist[2] + ringDist[3]
                                     + ringDist[4] + ringDist[5] + ringDist[6] + ringDist[7]) / 8.0;
                        double expectCorridor = ring + ReflectStaticConst("OpCorridorLead", 30.0);

                        for (int i = 0; i < 8; i++)
                        {
                            var lp = orderedPaths[i];
                            if (lp == null) continue;
                            var gg = lp.Data as PathGeometry;
                            var ff = (gg != null && gg.Figures.Count == 1) ? gg.Figures[0] : null;
                            if (ff == null) continue;

                            Point cur = ff.StartPoint;
                            foreach (var seg in ff.Segments)
                            {
                                var ls = seg as LineSegment;
                                if (ls == null) continue;
                                Point nx = ls.Point;
                                double dx = Math.Abs(nx.X - cur.X), dy = Math.Abs(nx.Y - cur.Y);
                                if (dx < 0.6 && dy >= 0.6)
                                {
                                    int side = i >= 4 ? 0 : 1;
                                    double d = Math.Abs(cur.X - ctr.X);
                                    if (colVn[side] == 0) colVx[side] = cur.X;
                                    else if (Math.Abs(colVx[side] - cur.X) > 0.6) colSpread++;
                                    if (Math.Abs(d - expectCorridor) > maxDev) maxDev = Math.Abs(d - expectCorridor);
                                    colVn[side]++;
                                    colVN++;
                                }
                                cur = nx;
                            }
                        }

                        if (colVN > 0 && colSpread == 0 && maxDev <= 1.5)
                        {
                            W(string.Format(CultureInfo.InvariantCulture,
                                "PASS: 8 条引线的竖直段全部落在两列各一条的走廊上 —— "
                                + "左列 x={0:0.#}、右列 x={1:0.#}（菜单中心 {2:0.#}，锚点环 {3:0.#}，"
                                + "走廊比锚点环再外 {4:0.#}px，共 {5} 条竖段，无散点）",
                                colVx[0], colVx[1], ctr.X, ring, expectCorridor - ring, colVN));
                        }
                        else
                        {
                            W(string.Format(CultureInfo.InvariantCulture,
                                "FAIL: 竖直走廊不合法 —— 共 {0} 条竖段，其中 {1} 条不在本列走廊上，"
                                + "偏离期望走廊（{2:0.#}）最大 {3:0.#}px；左列 x={4:0.#}、右列 x={5:0.#}",
                                colVN, colSpread, expectCorridor, maxDev, colVx[0], colVx[1]));
                            fail++;
                        }
                    }
                    else
                    {
                        W("FAIL: 竖直走廊校验无法进行（锚点环半径没量全）");
                        fail++;
                    }
                }

                // ---- ⑤d 两列的**联合行组**必须相对菜单中心上下对称 ----
                // 第五轮（2026-09-22）改成"右列整体上移 9px、左列整体下移 9px"之后，两列不再
                // 各自居中，但**等量反向**的平移让联合外沿（右列最高行的顶 / 左列最低行的底）
                // 仍然对称于菜单中心：rowTop = −(R + 行半高) = −124、rowBot = +124 → 中心 0 ✓
                // 这条守的是"末尾行距"那个老坑：`OpRowStyle` 只写 bottom 行距时 StackPanel 会把
                // 末尾那段空距也算进高度 → 两列**同时**上移，联合中心飘到 −5px
                // （2026-09-22 踩过：行心 −98/−36/+26/+88）→ 左上那条线凭空多出竖段。
                // ⚠️ 它只判"整体有没有飘"，判不了"两列各自的偏移量对不对" —— 后者归 ⑤f。
                {
                    double rowTop = double.MaxValue, rowBot = double.MinValue;
                    int gotRows = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (rows[i] == null || rows[i].ActualWidth <= 0.5) continue;

                        Rect r2;
                        try
                        {
                            r2 = rows[i].TransformToAncestor(grid)
                                .TransformBounds(new Rect(0, 0, rows[i].ActualWidth, rows[i].ActualHeight));
                        }
                        catch { continue; }

                        rowTop = Math.Min(rowTop, r2.Top);
                        rowBot = Math.Max(rowBot, r2.Bottom);
                        gotRows++;
                    }

                    double menuMidY2 = (canvasRect.Top + canvasRect.Bottom) / 2.0;
                    double rowMidY = (rowTop + rowBot) / 2.0;

                    if (gotRows == 8 && Math.Abs(rowMidY - menuMidY2) <= 1.0)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 两列行组相对菜单中心上下对称（行组中心 {0:0.#}，菜单中心 {1:0.#}，差 {2:0.#}px）",
                            rowMidY, menuMidY2, Math.Abs(rowMidY - menuMidY2)));
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 行组未相对菜单中心居中（行组中心 {0:0.#}，菜单中心 {1:0.#}，差 {2:0.#}px）"
                            + " —— 检查 OpRowStyle 的 bottom 行距是否被 StackPanel 的负 Margin 抵消",
                            rowMidY, menuMidY2, Math.Abs(rowMidY - menuMidY2)));
                        fail++;
                    }
                }

                // ---- ⑤f 行布局契约：同列间距一致 + 轴线两条对齐锚点环 + 两列偏移方向 ----
                // 三条约束一起管：
                //   a. **同列相邻间隙一致** —— 一行紧一行松会显得没排版（用户原话"注意同列其他
                //      下拉控件的垂直间距保持一致"）；
                //   b. **右列第 1 行（位置1 正上）行心 = −锚点环半径、左列第 4 行（位置5 正下）
                //      行心 = +锚点环半径**（本项目 ±99）—— 这两行的锚点正好落在菜单竖直轴上，
                //      行心与锚点同高时那两条引线只剩**一段水平直线**：就是用户说的
                //      "把左下与右上的引线拉直不要转角"（2026-09-22 第五轮）；
                //   c. **两列偏移方向**：右列整体在上、左列整体在下（各 9px）。
                // 达成 b 的唯一解是**两列反向平移**，不是"加大行距"：锚点 y 是圆周均布反算的
                // （0、±70、±99），等距行心只能对齐其中一对，而对齐竖直轴那对要求
                // 1.5×行心间距 = R = 99 —— 要么行距 16（顶满画布，用户否掉），
                // 要么保持行距 10、让两列各平移 9px（用户明确说"不需要保持左右控件的对齐"）。
                // ⚠️ 这条是"行距为什么是 10、两列 Margin 为什么是 8 / 18"的唯一守卫：
                // 谁把行距调回 16、或把某个 Margin 抹平成对称，它会立刻 FAIL。
                {
                    // 每列 4 行的行心（0=左列 1=右列），自上而下。
                    // 右列自上而下 = Position1..4（i=0..3）；左列自上而下 = Position8..5（i=7..4）。
                    var colCenters = new double[2, 4];
                    var colTop = new double[2, 4];
                    var colBot = new double[2, 4];
                    int got = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (rows[i] == null || rows[i].ActualWidth <= 0.5) continue;
                        Rect r3;
                        try
                        {
                            r3 = rows[i].TransformToAncestor(grid)
                                   .TransformBounds(new Rect(0, 0, rows[i].ActualWidth, rows[i].ActualHeight));
                        }
                        catch { continue; }

                        int col = i >= 4 ? 0 : 1;
                        int slot = i >= 4 ? (7 - i) : i;    // 左列：i=7→0(顶) … i=4→3(底)
                        colTop[col, slot] = r3.Top;
                        colBot[col, slot] = r3.Bottom;
                        colCenters[col, slot] = (r3.Top + r3.Bottom) / 2.0;
                        got++;
                    }

                    // 各列的相邻"可见间隙"（框下沿 → 下一个框上沿）
                    double gapSpread = 0;
                    string gapWhat = "";
                    double pitch = 0;
                    for (int col = 0; col < 2; col++)
                    {
                        double gmax = double.MinValue, gmin = double.MaxValue;
                        for (int k = 0; k < 3; k++)
                        {
                            double gp = colTop[col, k + 1] - colBot[col, k];
                            gmax = Math.Max(gmax, gp);
                            gmin = Math.Min(gmin, gp);
                        }
                        double spread = gmax - gmin;
                        if (spread > gapSpread)
                        {
                            gapSpread = spread;
                            gapWhat = (col == 0 ? "左列" : "右列") + " 间隙 "
                                      + gmin.ToString("0.#", CultureInfo.InvariantCulture) + "~"
                                      + gmax.ToString("0.#", CultureInfo.InvariantCulture) + "px";
                        }
                        double pt = colCenters[col, 1] - colCenters[col, 0];
                        if (pt > pitch) pitch = pt;
                    }

                    // 引线终点（= 锚点）y 与行心的差：位置1 / 位置5 应当为 0（引线退化成一段直线）
                    var dyA = new double[8];
                    for (int i = 0; i < 8; i++)
                    {
                        dyA[i] = double.NaN;
                        var lp2 = orderedPaths[i];
                        if (lp2 == null) continue;
                        var g2 = lp2.Data as PathGeometry;
                        var f2 = (g2 != null && g2.Figures.Count == 1) ? g2.Figures[0] : null;
                        if (f2 == null || f2.Segments.Count == 0) continue;
                        var last = f2.Segments[f2.Segments.Count - 1] as LineSegment;
                        if (last == null) continue;
                        int col = i >= 4 ? 0 : 1;
                        int slot = i >= 4 ? (7 - i) : i;
                        dyA[i] = Math.Abs(last.Point.Y - colCenters[col, slot]);
                    }

                    double midY = (canvasRect.Top + canvasRect.Bottom) / 2.0;
                    double axisDelta = Math.Max(double.IsNaN(dyA[0]) ? 999.0 : dyA[0],
                                                double.IsNaN(dyA[4]) ? 999.0 : dyA[4]);

                    // 两列 4 行的行心（相对菜单中心；负 = 偏上）—— 本轮的核心参数。
                    // 右列被"整体上移 9"、左列被"整体下移 9"，两列**刻意错开 18px**，
                    // 所以这里不是"两列相同"而是"两列各有一组值"。直接打进报告，
                    // 便于核对 XAML 里那两个 Margin 有没有跟着行距同步。
                    string colOffs = string.Format(CultureInfo.InvariantCulture,
                        "右列行心 {0:0.#}/{1:0.#}/{2:0.#}/{3:0.#}、左列行心 {4:0.#}/{5:0.#}/{6:0.#}/{7:0.#}（相对菜单中心）",
                        colCenters[1, 0] - midY, colCenters[1, 1] - midY,
                        colCenters[1, 2] - midY, colCenters[1, 3] - midY,
                        colCenters[0, 0] - midY, colCenters[0, 1] - midY,
                        colCenters[0, 2] - midY, colCenters[0, 3] - midY);

                    // c. 偏移方向：右列首行必须在菜单中心之上、左列末行在之下。
                    // 方向反了（左列上移 / 右列下移）时，位置1/位置5 的台阶会变成 2×偏移量
                    //（最大能到 ~40px），dyA 只会笼统报"有台阶"、说不出是方向错，
                    // 所以单列这条断言，报告里好一次定位。
                    bool dirOk = (colCenters[1, 0] - midY) < -1.0 && (colCenters[0, 3] - midY) > 1.0;

                    if (got == 8 && gapSpread <= 0.5 && axisDelta <= 1.0 && dirOk)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 同列相邻下拉框的垂直间距一致（行距 {0:0.#}，间隙差 ≤ {1:0.#}px）；"
                            + "位置1（右上）/ 位置5（左下）行心分别对齐 ±锚点环半径，引线台阶 {2:0.#}px "
                            + "→ 单段直线；{3}",
                            pitch, gapSpread, axisDelta, colOffs));
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 行距不一致 / 轴线两条仍有台阶 / 两列偏移方向错 —— {0}；行距 {1:0.#}；"
                            + "位置1 台阶 {2:0.#}px、位置5 台阶 {3:0.#}px（应 ≤1px）；{4}"
                            + " —— 检查 OpRowStyle 的 bottom 行距（应为 10）与两列 StackPanel 的 Margin"
                            + "（右列应为 \"0,0,0,8\"、左列应为 \"0,18,0,-10\"）",
                            gapWhat.Length > 0 ? gapWhat : "间隙均匀",
                            pitch, dyA[0], dyA[4], colOffs));
                        fail++;
                    }
                }


                // ---- ⑥⑦ 引线几何：四种菜单样式各验一遍 ----
                // 这是本检查的核心：方向角对不上 = 线连错了格。
                // 只管一种样式不够 —— 各样式的 VisualRadius 带不同外扩系数
                //（蜘蛛网 1.1、八卦 1.2…），fit 也可能不同，只在圆环下量过等于另外三种从没测过。
                //
                // 反射取菜单实例是为了拿 VisualCenter（**图形**中心）——
                // 各样式的 Width 未必等于 VisualRadius×2，用画布中心代替会整体偏掉。
                // 把每次量到的尺寸带到断言文案里：菜单"变小了"是这一页反复返工的主题，
                // 报告里直接写下"显示比例 / 可见半径 / 屏上直径 / 元素尺寸"，
                // 下次再有人觉得小/大，读报告比看图猜快得多。
                double lastFit = 1.0, lastDrawn = 0, lastMenuW = 0, lastMenuH = 0;

                var measure = new Func<string>(() =>
                {
                    Canvas lay = FindByName(w, "OpConnectorLayer") as Canvas;
                    if (lay == null) return "找不到引线层";

                    double vr = 0;
                    double dr = 0;                 // 可见外缘（DrawnRadius），锚点环就是按它算的
                    double elemW = 0, elemH = 0;   // 菜单元素尺寸（= GetOpMenuSize，缩放基数）
                    Point ctr = new Point(0, 0);
                    try
                    {
                        var mf = typeof(SettingsWindow).GetField("_opPreviewMenu",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        object mo = mf == null ? null : mf.GetValue(w);
                        var mel = mo as FrameworkElement;
                        var pr = mo == null ? null : mo.GetType().GetProperty("VisualRadius");
                        if (mel != null && pr != null)
                        {
                            vr = Convert.ToDouble(pr.GetValue(mo, null), CultureInfo.InvariantCulture);
                            ctr = mel.TranslatePoint(new Point(vr, vr), lay);

                            elemW = mel.Width; if (double.IsNaN(elemW) || elemW <= 0) elemW = vr * 2;
                            elemH = mel.Height; if (double.IsNaN(elemH) || elemH <= 0) elemH = vr * 2;
                        }

                        // 可见外缘：锚点贴着它放（见 UpdateOpConnectors / RadialMenu.DrawnRadius）。
                        // 老样式没实现该属性时退回 VisualRadius —— 那样下面的"贴边"断言会 FAIL，
                        // 正好把"某样式忘了实现 DrawnRadius"这件事暴露出来。
                        var drp = mo == null ? null : mo.GetType().GetProperty("DrawnRadius");
                        if (mo != null && drp != null)
                        {
                            dr = Convert.ToDouble(drp.GetValue(mo, null), CultureInfo.InvariantCulture);
                        }
                    }
                    catch { }

                    if (vr <= 0) return "取不到菜单的 VisualRadius";
                    if (double.IsNaN(dr) || dr <= 0) dr = vr;
                    // 可见外缘不可能比元素半尺寸还大（外扩系数只能 ≥1）
                    if (dr > vr * 1.001)
                    {
                        return "DrawnRadius(" + dr.ToString("F1", CultureInfo.InvariantCulture)
                             + ") 大于 VisualRadius(" + vr.ToString("F1", CultureInfo.InvariantCulture) + ")";
                    }

                    // 锚点与可见外缘的期望空隙（反射读常量，改常量断言跟着走 ——
                    // 与 ComputeOpFitCap 里那几个常量的处理方式一致）。
                    double wantGap = ReflectStaticConst("OpAnchorGap", 6.0);

                    // 菜单当前被缩小到多少。正常窗口下是 1.0（真实尺寸）；
                    // 只有在"锚点环将要越进行的内边缘"时才会 <1（见 ComputeOpFitCap）。
                    // 锚点半径是按"显示后"的半径算的，判断"锚点在不在图形外"必须同一口径。
                    double fit = 1.0;
                    try
                    {
                        var ff = typeof(SettingsWindow).GetField("_opFit",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (ff != null) fit = Convert.ToDouble(ff.GetValue(w), CultureInfo.InvariantCulture);
                    }
                    catch { }
                    if (double.IsNaN(fit) || fit <= 0) fit = 1.0;
                    lastFit = fit;
                    lastDrawn = dr;
                    lastMenuW = elemW;
                    lastMenuH = elemH;

                    var pls = new System.Windows.Shapes.Path[8];
                    int n = 0;
                    foreach (var ch in lay.Children)
                    {
                        var lp = ch as System.Windows.Shapes.Path;
                        if (lp != null && n < 8) pls[n++] = lp;
                    }
                    if (n != 8) return "引线条数 " + n + "（期望 8）";

                    for (int i = 0; i < 8; i++)
                    {
                        Point end;
                        if (!TryGetConnectorEnd(pls[i], out end)) return "位置" + (i + 1) + " 取不到引线终点";
                        double dx = end.X - ctr.X, dy = end.Y - ctr.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);

                        double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                        double expect = -90.0 + i * 45.0;
                        double diff = deg - expect;
                        while (diff > 180) diff -= 360;
                        while (diff < -180) diff += 360;

                        if (Math.Abs(diff) > 3.0)
                        {
                            return "位置" + (i + 1) + " 末端 "
                                 + deg.ToString("F1", CultureInfo.InvariantCulture) + "° ≠ 该扇区中心 "
                                 + expect.ToString("F1", CultureInfo.InvariantCulture) + "°";
                        }

                        // 锚点必须落在图形**之外**：放进图形内部的话，从侧面过来的引线
                        // 必然横穿相邻扇区，看着像连错了。
                        //
                        // ⚠️ 基准是**可见外缘**（DrawnRadius × fit），不是 VisualRadius × fit ——
                        // 后者含各样式为定位/标签预留的外扩（蜘蛛网 1.1、八卦 1.2），
                        // 拿它当"图形半径"会把明明贴在图案边上的锚点算成"在留白里飘 22px"，
                        // 而这恰恰是 2026-09-22 用户第二轮反馈要修的东西（"起点更接近菜单图案"）。
                        //
                        // 两头都要卡：太近 = 压进图形里；太远 = 浮在留白里、8 条线松紧不一。
                        double shownR = dr * fit;
                        double gap = dist - shownR;
                        if (gap < -0.5)
                        {
                            return "位置" + (i + 1) + " 锚点压进图形（离可见外缘 "
                                 + gap.ToString("F1", CultureInfo.InvariantCulture) + "px，应为正）";
                        }
                        if (Math.Abs(gap - wantGap) > 1.5)
                        {
                            return "位置" + (i + 1) + " 锚点未贴住图形边缘（离可见外缘 "
                                 + gap.ToString("F1", CultureInfo.InvariantCulture) + "px，期望 "
                                 + wantGap.ToString("F1", CultureInfo.InvariantCulture)
                                 + "px）—— 锚点环半径的基准可能又用回了 VisualRadius（含留白 "
                                 + (vr - dr).ToString("F1", CultureInfo.InvariantCulture) + "px）";
                        }
                    }
                    return null;
                });

                var styleRadios = new[] { "BasicRadialRadio", "CSHeadshotRadio", "SpiderWebRadio", "BaguaRadio" };
                var styleNames = new[] { "圆环", "Headshot", "蜘蛛网", "八卦" };

                for (int s = 0; s < styleRadios.Length; s++)
                {
                    var rf = typeof(SettingsWindow).GetField(styleRadios[s],
                        System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public);
                    var rb = rf == null ? null : rf.GetValue(w) as RadioButton;
                    if (rb == null)
                    {
                        W("WARN: 找不到样式单选 " + styleRadios[s]);
                        continue;
                    }

                    if (rb.IsChecked != true) rb.IsChecked = true;
                    Pump();
                    w.UpdateLayout();
                    Pump();
                    // 换样式会重建菜单，引线同样按 Background 优先级补画
                    Pump();
                    w.UpdateLayout();
                    Pump();

                    string err = measure();

                    // 每种样式都留一张图：引线是纯绘制，肉眼复核比读断言直观
                    RenderToPng(w, Path.Combine(outDir, "opconfig-" + styleNames[s] + ".png"));

                    if (err == null)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 样式「{0}」下 8 条引线均指向正确扇区、锚点均贴住图形可见外缘"
                            + "（显示比例 {1:0.###}，可见半径 {2:0.#} → 屏上 {3:0.#}，元素 {4:0.#}×{5:0.#}）",
                            styleNames[s], lastFit, lastDrawn, lastDrawn * lastFit, lastMenuW, lastMenuH));
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 样式「{0}」引线异常 —— {1}", styleNames[s], err));
                        fail++;
                    }
                }

                // ---- ⑧ 菜单显示比例 + 卡片撑满整页 + 「设置项获得焦点 → 高亮切到对应选区」----
                // ① 菜单尺寸：这一页**不再乘固定缩小系数**。旧版固定 0.72，连 50px 半径的圆环
                //    也被一路缩到 36px —— 用户 2026-09-22 反馈"菜单有点太小了"，就是这么来的。
                //    现在只剩两条约束：画布可用尺寸、**锚点环不许越进行的内边缘**
                //    （越过去点就压到行上、引线退化成贴行的短线）。这里量两件几何事实：
                //      a) 8 颗锚点里最远的那颗 + 选中态点半径，仍落在行内边缘之内；
                //      b) 菜单**用满**可用空间：fit ≈ min(画布上限, 锚点环约束)。
                //    b 是防回归钉子：谁把固定系数加回来（fit 恒 ≤0.72），b 立刻 FAIL。
                // ② 卡片撑满整页：防"面板吊在窗口里"（2026-09-22 用户反馈）。
                // ③ 焦点联动：把两个 handler 删掉，界面照开、其余断言照样全 PASS，
                //    只是"点哪一行菜单都不亮"。所以必须有一条量它的。
                {
                    w.UpdateLayout();
                    Pump();

                    double gapConst = ReflectStaticConst("OpAnchorGap", 6.0);
                    // 统一锚点环半径 —— 取代了原来的"留给水平引出段的 8px"（那个常量已删）。
                    // 它现在是引线几何的**主约束**：见 SettingsWindow.OpAnchorRingVisibleR。
                    double ringRConst = ReflectStaticConst("OpAnchorRingVisibleR", 93.0);
                    double dotRConst = ReflectStaticConst("OpDotSelectedRadius", 5.0);

                    // 菜单中心与可见外缘（反射取实例字段/属性）。
                    Point ctr = new Point(0, 0);
                    double drawnR = 0;
                    double menuPixelW = 0, menuPixelH = 0;   // 菜单元素尺寸（算"画布还装得下多少"要用）
                    double actualScale = 1.0;                // 挂在菜单上的**实际**缩放
                    bool gotMenu = false;
                    try
                    {
                        var mf = typeof(SettingsWindow).GetField("_opPreviewMenu",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        object mo = mf == null ? null : mf.GetValue(w);
                        var mel = mo as FrameworkElement;
                        var pr = mo == null ? null : mo.GetType().GetProperty("VisualRadius");
                        var drp = mo == null ? null : mo.GetType().GetProperty("DrawnRadius");
                        if (mel != null && pr != null)
                        {
                            double vr = Convert.ToDouble(pr.GetValue(mo, null), CultureInfo.InvariantCulture);
                            drawnR = drp == null ? vr
                                : Convert.ToDouble(drp.GetValue(mo, null), CultureInfo.InvariantCulture);
                            // ⚠️ 参照系必须取 OpLayoutGrid（三栏的共同祖先）。
                            // 引线层是行的**兄弟**、不是祖先 —— 拿它做 TransformToAncestor
                            // 会抛 "指定的 Visual 不是此 Visual 的上级"（改这版时就踩了）。
                            // 引线层铺在三栏左上角，坐标原点与 OpLayoutGrid 相同，
                            // 所以 _opDotCenters 里的坐标可以直接与这里的量值比。
                            ctr = mel.TranslatePoint(new Point(vr, vr), grid);

                            menuPixelW = mel.Width; if (double.IsNaN(menuPixelW) || menuPixelW <= 0) menuPixelW = vr * 2;
                            menuPixelH = mel.Height; if (double.IsNaN(menuPixelH) || menuPixelH <= 0) menuPixelH = vr * 2;

                            // 实际生效的缩放：⚠️ 必须量它，不能只信 _opFit 字段。
                            // 改这一版时踩过：ApplyMenuFit 里写成 `if (fit < 1.0)` 才挂变换，
                            // 允许放大时 fit = 2.36 走了 else 分支把变换清成 null ——
                            // _opFit 报 2.36、图形却按 1.0 画，锚点全浮在图形外面一大圈，
                            // 而所有读字段的断言照样 PASS。只有截图能看出来。
                            var rtg = mel.RenderTransform as TransformGroup;
                            if (rtg != null)
                            {
                                foreach (var t in rtg.Children)
                                {
                                    var st = t as ScaleTransform;
                                    if (st != null) actualScale = st.ScaleX;
                                }
                            }
                            gotMenu = true;
                        }
                    }
                    catch { }

                    double fitNow = 1.0;
                    try
                    {
                        var ff = typeof(SettingsWindow).GetField("_opFit",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (ff != null) fitNow = Convert.ToDouble(ff.GetValue(w), CultureInfo.InvariantCulture);
                    }
                    catch { }
                    if (double.IsNaN(fitNow) || fitNow <= 0) fitNow = 1.0;

                    // a) 行内边缘距中心（最小者）+ 锚点环实际半径（8 颗点里最远的那颗）。
                    double innerDist = double.NaN, ringR = 0;
                    if (gotMenu)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            if (rows[i] == null || rows[i].ActualWidth <= 0.5) continue;

                            var rr = rows[i].TransformToAncestor(grid)
                                .TransformBounds(new Rect(0, 0, rows[i].ActualWidth, rows[i].ActualHeight));
                            double d = i >= 4 ? (ctr.X - rr.Right) : (rr.Left - ctr.X);
                            if (double.IsNaN(innerDist) || d < innerDist) innerDist = d;
                        }

                        try
                        {
                            var df = typeof(SettingsWindow).GetField("_opDotCenters",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var arr = df == null ? null : df.GetValue(w) as Point[];
                            if (arr != null)
                            {
                                for (int i = 0; i < 8 && i < arr.Length; i++)
                                {
                                    double dx = arr[i].X - ctr.X, dy = arr[i].Y - ctr.Y;
                                    double d = Math.Sqrt(dx * dx + dy * dy);
                                    if (d > ringR) ringR = d;
                                }
                            }
                        }
                        catch { }
                    }

                    if (!gotMenu || double.IsNaN(innerDist) || ringR <= 0)
                    {
                        W("FAIL: 取不到菜单中心/锚点环/行内边缘，无法验证锚点环是否侵入左右两列");
                        fail++;
                    }
                    else if (ringR + dotRConst <= innerDist)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 锚点环未侵入左右两列 —— 锚点环半径 {0:0.#} + 选中态点半径 {1:0.#} ≤ 行内边缘距中心 {2:0.#}（余量 {3:0.#}）",
                            ringR, dotRConst, innerDist, innerDist - ringR - dotRConst));
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 锚点环越进行的内边缘 —— 半径 {0:0.#} + 点半径 {1:0.#} > {2:0.#}，"
                            + "点会压到行上、引线退化成贴行短线", ringR, dotRConst, innerDist));
                        fail++;
                    }

                    // b) 菜单应当**用满**可用空间，且不多不少：
                    //    期望值 = min(画布可用尺寸允许的比例, 锚点环约束允许的比例)。
                    //    旧版固定乘 0.72（连 50px 半径的圆环也被缩到 36px）在这条上必然 FAIL ——
                    //    这就是"又加了个固定缩小系数"的防回归钉子。
                    double expectFit = 1.0;
                    if (gotMenu && menuPixelW > 0 && menuPixelH > 0 && canvas.ActualWidth > 0)
                    {
                        double padConst = ReflectStaticConst("MenuFitPad", 8.0);
                        double canvasLimit = Math.Min(
                            (canvas.ActualWidth - padConst * 2) / menuPixelW,
                            (canvas.ActualHeight - padConst * 2) / menuPixelH);
                        double rowCap = (innerDist - gapConst - dotRConst) / drawnR;
                        double ringCap = ringRConst / drawnR;   // 统一锚点环半径，通常比 rowCap 更紧
                        expectFit = Math.Min(Math.Min(canvasLimit, rowCap), ringCap);
                        if (expectFit < 0.2) expectFit = 0.2;
                    }

                    // b0) 报告的比例必须与**实际挂在元素上的缩放**一致。
                    //     这条盯的是"字段对了但没生效"——改这一版时 ApplyMenuFit 只在 fit<1 时挂变换，
                    //     允许放大后 _opFit=2.36 却把变换清成 null，图形按 1.0 画、
                    //     锚点按 2.36 摆，读字段的断言全 PASS。只有量实际缩放能抓住它。
                    if (Math.Abs(actualScale - fitNow) <= 0.02)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 菜单的实际缩放与显示比例一致（RenderTransform {0:0.###} = fit {1:0.###}）",
                            actualScale, fitNow));
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 菜单的显示比例**没有真正生效** —— RenderTransform {0:0.###} ≠ fit {1:0.###}"
                            + "（锚点会按 fit 摆、图形按实际缩放画，两者错位）", actualScale, fitNow));
                        fail++;
                    }

                    if (Math.Abs(fitNow - expectFit) <= 0.02)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 中间菜单用满可用空间（fit {0:0.###} ≈ 期望 {1:0.###}）—— 不再有固定缩小系数",
                            fitNow, expectFit));
                    }                    else if (fitNow < expectFit)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 菜单比可用空间允许的还小（fit {0:0.###} < 期望 {1:0.###}）—— "
                            + "是不是又加了个固定缩小系数？（旧版 0.72 就是这么把圆环缩到 36px 的）",
                            fitNow, expectFit));
                        fail++;
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 菜单超出可用空间（fit {0:0.###} > 期望 {1:0.###}）—— "
                            + "锚点环可能已越进行内边缘或图形被画布裁掉", fitNow, expectFit));
                        fail++;
                    }

                    // ② 卡片撑满整页（"面板吊在窗口里"的防回归）。
                    var pageScroll = FindByName(w, "OpPageScroll") as ScrollViewer;
                    var pageCard = FindByName(w, "OpPageCard") as Border;
                    if (pageScroll == null || pageCard == null)
                    {
                        W("FAIL: 找不到操作配置页的 OpPageScroll / OpPageCard，无法验证卡片是否撑满整页");
                        fail++;
                    }
                    else if (pageScroll.ViewportHeight <= 1)
                    {
                        W("FAIL: 操作配置页的视口高度读不到（ViewportHeight ≤ 1），卡片高度无从验证");
                        fail++;
                    }
                    else if (pageCard.ActualHeight < pageScroll.ViewportHeight - 2)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 卡片没撑满整页 —— 卡片高 {0:0.#} < 视口高 {1:0.#}（差 {2:0.#}px 会露在下面，"
                            + "看着就是「面板吊在窗口里」）",
                            pageCard.ActualHeight, pageScroll.ViewportHeight,
                            pageScroll.ViewportHeight - pageCard.ActualHeight));
                        fail++;
                    }
                    else if (pageCard.ActualHeight > pageScroll.ViewportHeight + 2)
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "FAIL: 卡片超出视口 —— 卡片高 {0:0.#} > 视口高 {1:0.#}，会凭空逼出一条滚动条",
                            pageCard.ActualHeight, pageScroll.ViewportHeight));
                        fail++;
                    }
                    else
                    {
                        W(string.Format(CultureInfo.InvariantCulture,
                            "PASS: 卡片撑满整页（卡片高 {0:0.#} = 视口高 {1:0.#}），不再吊在页面顶部",
                            pageCard.ActualHeight, pageScroll.ViewportHeight));
                    }

                    var probe = FindByName(w, "OpComboP3") as ComboBox;
                    if (probe == null)
                    {
                        W("FAIL: 找不到 OpComboP3，无法验证焦点联动");
                        fail++;
                    }
                    else
                    {
                        bool realFocus = false;
                        try { realFocus = probe.Focus(); } catch { }
                        Pump();
                        w.UpdateLayout();
                        Pump();

                        if (!realFocus)
                        {
                            // 窗口摆在离屏位置（-32000）时未必拿得到真实键盘焦点 ——
                            // 退化成直接抛 GotFocus 路由事件，只验"事件有没有接上"。
                            // 弱一档，但结果里会如实标出来，不假装等价。
                            try { probe.RaiseEvent(new RoutedEventArgs(UIElement.GotFocusEvent, probe)); } catch { }
                            Pump();
                        }

                        int active = -1;
                        try
                        {
                            var af = typeof(SettingsWindow).GetField("_selectedSectorIndex",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (af != null) active = Convert.ToInt32(af.GetValue(w), CultureInfo.InvariantCulture);
                        }
                        catch { }

                        // 光看字段不够：字段对了但没重绘，界面上还是不会亮 ——
                        // 顺带比一比"被聚焦那条引线"与"没聚焦那条"的画笔是否真的不一样。
                        bool repainted = false;
                        try
                        {
                            var lf = typeof(SettingsWindow).GetField("_opLines",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var lines = lf == null ? null : lf.GetValue(w)
                                        as System.Windows.Shapes.Path[];
                            if (lines != null && lines[2] != null && lines[0] != null)
                            {
                                repainted = !ReferenceEquals(lines[2].Stroke, lines[0].Stroke);
                            }
                        }
                        catch { }

                        RenderToPng(w, Path.Combine(outDir, "opconfig-focus.png"));

                        if (active == 2 && repainted)
                        {
                            W("PASS: 设置项获得焦点后菜单高亮切到对应选区（Position3，引线已重绘）"
                              + (realFocus ? "" : "（注：离屏窗口未拿到真实键盘焦点，改用路由事件验证接线）"));
                        }
                        else
                        {
                            W("FAIL: 焦点联动未生效 —— 聚焦 OpComboP3 后 当前选中 = " + active
                              + "（期望 2），引线重绘 = " + (repainted ? "是" : "否"));
                            fail++;
                        }
                    }
                }

                w.Close();
            }
            catch (Exception ex)
            {
                W("FAIL: 操作配置页检查异常: " + ex.GetType().Name + ": " + ex.Message);
                fail++;
                try { if (w != null) w.Close(); } catch { }
            }
        }

        private static void CheckMenuStylePanelSync(ref int fail, Action<string> W)
        {
            W("");
            W("--- 样式选择与参数面板联动 ---");

            SettingsWindow w = null;
            try
            {
                w = new SettingsWindow();
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -32000; w.Top = -32000;
                w.ShowInTaskbar = false;
                w.Show();
                Pump();
                w.UpdateLayout();
                Pump();

                // { 单选框名, 它对应的参数面板名 }
                var pairs = new[]
                {
                    new[] { "BasicRadialRadio", "BasicRadialConfigGrid" },
                    new[] { "CSHeadshotRadio",  "CSHeadshotConfigGrid" },
                    new[] { "SpiderWebRadio",   "SpiderWebConfigGrid" },
                    new[] { "BaguaRadio",       "BaguaConfigGrid" },
                };

                int checkedCount = 0;
                string checkedName = null;
                var bad = new System.Collections.Generic.List<string>();

                foreach (var p in pairs)
                {
                    var rb = FindByName(w, p[0]) as System.Windows.Controls.RadioButton;
                    var panel = FindByName(w, p[1]);
                    if (rb == null || panel == null)
                    {
                        W("FAIL: 找不到控件 " + p[0] + " 或 " + p[1]);
                        fail++;
                        return;
                    }

                    bool isChecked = rb.IsChecked == true;
                    bool panelVisible = panel.Visibility == Visibility.Visible;
                    if (isChecked) { checkedCount++; checkedName = p[0]; }

                    if (isChecked != panelVisible)
                    {
                        bad.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0}[勾选={1} 面板可见={2}]", p[0], isChecked, panelVisible));
                    }
                }

                if (checkedCount != 1)
                {
                    W("FAIL: 应当恰好有一个样式被勾选，实际 " + checkedCount + " 个");
                    fail++;
                }
                else if (bad.Count > 0)
                {
                    W("FAIL: 参数面板与勾选样式不一致 —— " + string.Join("、", bad.ToArray()));
                    fail++;
                }
                else
                {
                    W("PASS: 参数面板与勾选样式一致（" + checkedName + "），四组互斥");
                }
            }
            catch (Exception ex)
            {
                W("FAIL: 样式面板联动检查异常: " + ex.GetType().Name + ": " + ex.Message);
                fail++;
            }
            finally
            {
                try { if (w != null) w.Close(); } catch { }
            }
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

        /// <summary>
        /// 取一条引线（Path）的**终点** —— 即扇区端的锚点坐标。
        /// 引线是「直线 → 圆角贝塞尔 → 直线」三段，最后一段必然是 LineSegment。
        /// </summary>
        private static bool TryGetConnectorEnd(System.Windows.Shapes.Path path, out Point end)
        {
            end = new Point(0, 0);

            var geo = path == null ? null : path.Data as PathGeometry;
            if (geo == null || geo.Figures.Count == 0) return false;

            var fig = geo.Figures[0];
            if (fig.Segments.Count == 0) return false;

            var last = fig.Segments[fig.Segments.Count - 1] as LineSegment;
            if (last == null) return false;

            end = last.Point;
            return true;
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

            // 每项 = { 反射字段名, 界面上应显示的名称 }
            // ⚠️ 第二列以 XAML 里 RadioButton 的 Content 为准。改了显示名必须同步这张表，
            //    否则下面的「显示名」断言会立刻报出来（不会再悄悄生成名字对不上的截图）。
            var variants = new[]
            {
                new[] { "BasicRadialRadio", "圆环" },
                new[] { "CSHeadshotRadio", "Headshot" },
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
                        // 显示名断言：界面上的名称必须与清单一致。
                        // 以前只把第二列当"截图文件名"用，改错了也没人发现；现在它是有约束的。
                        string shown = rb.Content == null ? "" : rb.Content.ToString();
                        if (shown == v[1])
                        {
                            W(string.Format(CultureInfo.InvariantCulture,
                                "PASS: 样式项显示名为「{0}」", shown));
                        }
                        else
                        {
                            W(string.Format(CultureInfo.InvariantCulture,
                                "FAIL: 样式项显示名不符（期望「{0}」，界面实际「{1}」）", v[1], shown));
                            fail++;
                        }

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
        /// <summary>
        /// 反射读 SettingsWindow 的私有静态常量。
        /// <para>
        /// 断言里**不要**再抄一份数字：常量改了断言要跟着走，抄一份就变成"两处真相"，
        /// 改一处忘一处时断言会静默失效（读不出来就退回 <paramref name="fallback"/>，
        /// 至少不会崩）。
        /// </para>
        /// </summary>
        private static double ReflectStaticConst(string name, double fallback)
        {
            try
            {
                var f = typeof(SettingsWindow).GetField(name,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (f != null) return Convert.ToDouble(f.GetValue(null), CultureInfo.InvariantCulture);
            }
            catch { }
            return fallback;
        }

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
