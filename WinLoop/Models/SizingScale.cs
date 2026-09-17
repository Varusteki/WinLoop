using System;
using System.Windows;
using System.Windows.Media;

namespace WinLoop.Models
{
    /// <summary>
    /// 尺寸单位换算：把配置里存的「100% 缩放基准值」换成「当前屏幕实际逻辑像素」。
    ///
    /// ============ 为什么需要这一层 ============
    ///
    /// 配置里的半径/粗细都是**逻辑像素且以 100% 缩放为基准**的数字（见
    /// <see cref="AppConfig.SizingUnitCurrent"/>）。在 100% 缩放的屏幕上，WPF 的
    /// 1 逻辑单位就是 1 物理像素，一切正常；但在 200% 缩放的 4K 屏上，
    /// 同一个「半径 90」只占屏幕的一小格 —— 视觉大小只剩 1080p 下的四分之一
    /// （按面积算）。用户调着舒服的值换台机器就变得不可用。
    ///
    /// 修法很直白：菜单渲染前把基准值乘以「本屏 DPI / 96」。
    ///   - 100% 缩放 → ×1.0，与老版本像素级一致；
    ///   - 125%      → ×1.25；
    ///   - 200%      → ×2.0，菜单物理尺寸翻倍，视觉大小与 100% 屏相同。
    ///
    /// ============ 为什么不做屏幕分辨率自适应 ============
    ///
    /// 只在 DPI 缩放下换算，**不**按分辨率（1080p / 4K）缩放。
    /// 因为 DPI 缩放表达的是"用户希望界面元素多大"，是操作系统的用户偏好；
    /// 分辨率本身不改变这个偏好 —— 4K + 100% 缩放的用户就是想要小元素、大工作区，
    /// 强行放大反而是夺走他的选择。所以这里只追随 DPI，不追随分辨率。
    /// </summary>
    public static class SizingScale
    {
        /// <summary>WPF 的逻辑 DPI 基准：96 DPI ↔ 缩放 100%。</summary>
        public const double BaselineDpi = 96.0;

        /// <summary>
        /// 预览用基准：设置面板里的预览永远按 100% 缩放绘制。
        ///
        /// 理由：预览的作用是让用户比较"我调大了一点"，如果预览跟着用户当前屏幕的
        /// 缩放一起变，同一个数值在不同机器上预览大小不同，反而看不出自己改了什么。
        /// 用固定基准表示"相对大小"才是预览该有的语义。
        /// </summary>
        public const double PreviewScale = 1.0;

        /// <summary>把 DPI 换成缩放系数（96 → 1.0，120 → 1.25，192 → 2.0）。</summary>
        public static double FromDpi(double dpi)
        {
            if (double.IsNaN(dpi) || double.IsInfinity(dpi) || dpi <= 0)
            {
                return 1.0;
            }
            return dpi / BaselineDpi;
        }

        /// <summary>
        /// 取某个可视化元素所在显示器的 DPI 缩放系数。
        ///
        /// 用 <see cref="PresentationSource.FromVisual"/> 的
        /// <c>CompositionTarget.TransformToDevice</c> 读：Per-Monitor V2 下它返回的是
        /// **该元素当前所在显示器**的换算矩阵，正是我们要的"本屏"缩放。
        ///
        /// 元素尚未接入视觉树（没有 PresentationSource）时退回 1.0 —— 宁可先按 100% 画，
        /// 也不能抛异常让菜单弹不出来。
        /// </summary>
        public static double FromVisual(Visual visual)
        {
            try
            {
                if (visual == null) return 1.0;

                var source = PresentationSource.FromVisual(visual);
                if (source?.CompositionTarget != null)
                {
                    var m = source.CompositionTarget.TransformToDevice;
                    // 矩阵的 M11 分量就是 X 方向的设备像素/逻辑单位之比。
                    // 正常情况下 X/Y 缩放相同；Y 不同时取 X 即可（菜单是正圆，不拉椭圆）。
                    double scale = m.M11;
                    if (!double.IsNaN(scale) && !double.IsInfinity(scale) && scale > 0)
                    {
                        return scale;
                    }
                }
            }
            catch
            {
                // 交给下面的兜底
            }
            return 1.0;
        }
    }
}
