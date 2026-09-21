using System;
using System.Windows;
using System.Windows.Media;

namespace WinLoop.Models
{
    /// <summary>
    /// 本屏 DPI 缩放比的读取工具。
    ///
    /// ============ 只用于「展示」，不参与菜单尺寸 ============
    ///
    /// 曾经这里还负责把菜单尺寸按 DPI 放大（配置值 × 本屏 DPI/96）。
    /// 那是**双重缩放**的错误做法 —— 程序是 Per-Monitor V2 感知的（见 app.manifest），
    /// WPF 渲染时已自动把 DIP 换算成物理像素，再乘一次会让菜单在
    /// 175% 缩放下被撑到 1.75 倍。详见 <c>RadialMenu.Scaled</c> 的注释。
    ///
    /// 现在菜单尺寸直接用配置里的 DIP（WPF 自动处理 DPI）。这里只剩一个用途：
    /// 设置面板要把「半径 90」讲成「本屏约 158px」给人看，需要这个换算比。
    /// </summary>
    public static class SizingScale
    {
        /// <summary>
        /// 取某个可视化元素所在显示器的 DPI 缩放系数（96 DPI → 1.0，168 DPI → 1.75）。
        ///
        /// 用 <see cref="PresentationSource.FromVisual"/> 的
        /// <c>CompositionTarget.TransformToDevice</c> 读：Per-Monitor V2 下它返回的是
        /// **该元素当前所在显示器**的换算矩阵，正是我们要的「本屏」缩放。
        ///
        /// 元素尚未接入视觉树（没有 PresentationSource）时退回 1.0 —— 宁可先按 100% 显示，
        /// 也不能抛异常。
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
