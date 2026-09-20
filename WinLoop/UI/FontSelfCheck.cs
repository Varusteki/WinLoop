using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace WinLoop.UI
{
    /// <summary>
    /// 内嵌字体的运行时自检。
    ///
    /// 为什么需要它：pack:// URI 写错时 WPF **不报错**，只是静默回退到
    /// 下一个候选字体。开发者本机往往装了同名字体，肉眼看截图"好像没问题"，
    /// 换台没装的机器才暴露 —— 这类问题靠编译和截图都抓不住，只能靠断言。
    ///
    /// 另一个坑：`pack://application:,,,` 的解析基准是**入口程序集**。
    /// 用外部探针程序去引 WinLoop.dll 里的字体资源永远解析不到
    /// （会回退到 Arial），那是探针的局限而非字体的问题。
    /// 因此本自检必须跑在 WinLoop.exe 自己的进程里。
    ///
    /// 用法：WinLoop.exe --fontcheck [输出文件]
    /// </summary>
    internal static class FontSelfCheck
    {
        public static void Run(string outPath)
        {
            var sb = new StringBuilder();
            int fail = 0;

            void W(string line)
            {
                sb.AppendLine(line);
                try { Console.WriteLine(line); } catch { /* 无控制台时忽略 */ }
            }

            W("=== 内嵌字体自检 ===");
            W("入口程序集: " + Assembly.GetEntryAssembly().GetName().Name);
            W("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            W("");

            // ---- 1) 资源是否编进程序集 ----
            var asm = typeof(App).Assembly;
            var fontKeys = new List<string>();
            try
            {
                using (var s = asm.GetManifestResourceStream("WinLoop.g.resources"))
                using (var rr = new System.Resources.ResourceReader(s))
                {
                    foreach (System.Collections.DictionaryEntry e in rr)
                    {
                        string k = e.Key as string;
                        if (k == null) continue;
                        if (k.IndexOf(".otf", StringComparison.OrdinalIgnoreCase) >= 0
                            || k.IndexOf(".ttf", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            long len = -1;
                            if (e.Value is Stream st) { len = st.Length; }
                            fontKeys.Add(k);
                            W(string.Format(CultureInfo.InvariantCulture,
                                "[资源] {0}  ({1} 字节)", k, len));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                W("[资源] 读取失败: " + ex.Message);
            }

            if (fontKeys.Count == 0)
            {
                W("FAIL: 程序集里没有任何字体资源");
                fail++;
            }
            else
            {
                W("PASS: 程序集内含 " + fontKeys.Count + " 个字体资源");
            }
            W("");

            // ---- 2) UiFontFamily 资源是否存在 ----
            var ff = Application.Current != null
                ? Application.Current.TryFindResource("UiFontFamily") as FontFamily
                : null;

            if (ff == null)
            {
                W("FAIL: 找不到 UiFontFamily 资源");
                fail++;
                Flush(sb, outPath);
                return;
            }

            W("[Uri] " + ff.Source);
            W("");

            // ---- 3) 解析到的家族名必须是 Noto，而不是回退字体 ----
            var names = new List<string>();
            foreach (var kv in ff.FamilyNames) names.Add(kv.Value);
            W("解析到的家族: " + string.Join(" | ", names.ToArray()));

            bool hasNoto = false;
            foreach (var n in names)
            {
                if (n.IndexOf("Noto", StringComparison.OrdinalIgnoreCase) >= 0) hasNoto = true;
            }

            if (hasNoto)
            {
                W("PASS: 内嵌 Noto 家族已加载（未回退到系统字体）");
            }
            else
            {
                W("FAIL: 家族名里没有 Noto —— pack URI 未命中，已回退到系统字体");
                fail++;
            }
            W("");

            // ---- 4) 各字重实际命中的字面 ----
            // 判定标准：界面里正文用 Normal(→Regular)，强调文字用 Medium(→Medium)。
            // 这两个必须落在**不同**字面上，标题才立得住。
            // 曾试过用 SemiBold(600) 去做强调，但 WPF 的字重匹配在这个
            // 只有 Regular(400)/Medium(500) 两档的家族里会落到 Regular，
            // 导致标题与正文无区别 —— 改用 Medium 后正常。
            string regFace = null, medFace = null;
            foreach (var weight in new[]
                     {
                         FontWeights.Normal, FontWeights.Medium,
                         FontWeights.SemiBold, FontWeights.Bold,
                     })
            {
                var tf = new Typeface(ff, FontStyles.Normal, weight, FontStretches.Normal);
                string face = FaceOf(tf);
                W(string.Format(CultureInfo.InvariantCulture,
                    "[字重] {0,-9} -> 字面 '{1}'", weight, face));

                if (weight == FontWeights.Normal) regFace = face;
                if (weight == FontWeights.Medium) medFace = face;
            }

            if (regFace != null && medFace != null && regFace != medFace)
            {
                W("PASS: Medium 与 Regular 命中不同字面（强调文字能与正文区分）");
            }
            else
            {
                W("FAIL: Medium 与 Regular 命中同一字面 —— 标题无法与正文区分");
                fail++;
            }
            W("");

            // ---- 5) 界面实际用到的字符是否都有字形 ----
            // 覆盖四个页面的标题、标签、提示文案里出现的字，
            // 以及色值框会用到的 0-9 / A-F / # / % / ( ) 等。
            //
            // ⚠️ 必须包含**完整大小写拉丁字母表**：菜单样式项现在有一个纯拉丁名
            //    「Headshot」，只测 A-F 的话，内嵌字体缺小写字形也查不出来。
            const string sample =
                "菜单样式操作配置悬空寺杂项设置圆环蜘蛛网八卦" +
                "外半径内半径逻辑像素配色线条颜色高亮色选区透明度" +
                "扇区动作触发按键展示内容操作提示文字内容键位图" +
                "开机自动启动最小化到托盘触发时长毫秒" +
                "恢复所有默认设置取消确定效果预览尺寸圈层外观缩放" +
                "已修改保存点击选择错误成功失败单位百分号" +
                "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                "abcdefghijklmnopqrstuvwxyz" +
                "0123456789#%()@.-+:";

            bool cjk = CanRender(ff, sample);
            if (cjk)
            {
                W("PASS: 界面所需字符全部有字形（中文 " + sample.Length + " 字样本）");
            }
            else
            {
                W("FAIL: 样本中存在缺字形的字符，会显示成方框");
                fail++;
            }
            W("");

            W(fail == 0 ? "结果: ALL PASS" : ("结果: FAILED (" + fail + ")"));
            Flush(sb, outPath);
        }

        private static void Flush(StringBuilder sb, string outPath)
        {
            if (string.IsNullOrEmpty(outPath)) return;
            try
            {
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { /* 写不出去就算了，控制台已经有输出 */ }
        }

        private static string FaceOf(Typeface tf)
        {
            try
            {
                if (tf.TryGetGlyphTypeface(out var gtf))
                {
                    foreach (var kv in gtf.FaceNames) return kv.Value;
                }
            }
            catch (Exception ex) { return "(" + ex.GetType().Name + ")"; }
            return "(取不到)";
        }

        private static bool CanRender(FontFamily ff, string text)
        {
            try
            {
                var tf = new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                if (!tf.TryGetGlyphTypeface(out var gtf)) return false;
                foreach (char ch in text)
                {
                    if (!gtf.CharacterToGlyphMap.ContainsKey(ch)) return false;
                }
                return true;
            }
            catch { return false; }
        }
    }
}
