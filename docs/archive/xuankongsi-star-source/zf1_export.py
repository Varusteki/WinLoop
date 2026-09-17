# -*- coding: utf-8 -*-
"""导出三层几何 -> WinLoop/Menus/CSHeadshotLayers.cs（2026-09-16 第二版）

改动原因：
  ① 星尖做反了 —— 参考图 h2.jpg 里星臂是**实心黑楔 + 中间一道白色缺口**，
     之前的实现是「白三角 + 细黑描边 + 黑色缝」，正好颠倒。
  ② 骷髅沿用 h2.jpg 的描摹墨迹，脸颊/下颌那两条黑带是 h2 特有的手绘阴影，
     用户要求照他给的 `抠图骷髅头.png`（干净版）来做 —— 所以骷髅改成**从那张图重描**。

产出：
  ① 同心圆：两圈半径（沿用）
  ② 八角星：谷半径 + 臂的半边轮廓折线 + 白缝的半边轮廓折线
  ③ 骷髅头：剪影轮廓 + 全部墨迹轮廓（EvenOdd），描摹自 抠图骷髅头.png

坐标系：原点 = 菜单中心，1.0 = 星尖顶点半径，y 轴向下为正。
"""
import io, re, math
from PIL import Image, ImageDraw, ImageChops
from collections import deque

OUT = "WinLoop/Menus/CSHeadshotLayers.cs"
SRC_RING = "tmp/ref/h2.jpg"                       # 环形/星的来源
SRC_SKULL = r"C:\Users\Wu\.workbuddy\clipboard-images\clipboard-2026-09-16T05-47-48-632Z-8a8ca273.jpg"   # 骷髅来源（用户提供）

# ==================== ① 圆环（按 h2.jpg 实测重新校） ====================
# 参考图实测（逐角度取最外/次外墨迹带再对 1440 个角度平均）：
#   内圈 0.586~0.655 厚 0.070    外圈 0.738~0.797 厚 0.059
# 注意参考图的**内圈比外圈粗**，之前两圈做成一样粗是错的。
# 注意：绘制时圆环是用 `_seamPen`（宽 0.006 的同色描边）画的，
# 它会在几何之外再膨胀 ±0.003；加上抗锯齿，实测渲染出来的带比这里写的**厚约 0.007**、
# 内圈还会整体向内偏 0.006。下面这组值已按实测偏差补偿回去：
#   设计 0.598~0.659 -> 渲染 0.592~0.665 = 参考图实测（避开星臂角度，2880 个角度平均）
R_IN_LO, R_IN_HI = 0.602, 0.659
R_OUT_LO, R_OUT_HI = 0.744, 0.796

# ==================== ② 八角星（严格几何构造） ====================
# 用户指定的参考图（风车式八角星）反推出的**严格几何**构造，逐像素差异率 3.0%：
#
#   黑 = 标准星多边形(尖 R_TIP / 谷 R_VALLEY，全直线边)
#        − 8 个三角孔 [ (HOLE_TIP_R, 轴) , (HOLE_MID_R, 轴 + HOLE_ANGLE) , (圆心) ]
#
# 外轮廓实测（轮廓追踪 + RDP）：8 个尖 r=1.000 在 -90°+45°k，
# 8 个谷 r=0.458 在 -67.5°+45°k —— 是精确的标准星形。
# 内部实测只有 **1 个连通白孔**（面积 64884px），其边界每 45° 重复一次，
# 三个顶点依次为 (0.797, 轴) / (0.352, 轴−22.5°) / (~0, 轴−26.6°)，
# 即每支尖被斜削掉一块 -> 形成风车感。
R_TIP = 1.0000
R_VALLEY = 0.4580            # 谷半径（实测 0.4555~0.4614）
HOLE_TIP_R = 0.8000          # 三角孔在尖轴上的顶点半径
HOLE_MID_R = 0.3540          # 三角孔的中间顶点半径（=√2/4，实测 0.344~0.354）
HOLE_ANGLE = -22.5           # 中间顶点相对尖轴的偏角（负 = 逆时针侧）

# ③ 骷髅轮廓的等宽描边宽度（归一化单位）。
# 描摹出来的轮廓厚度不均（顶部 0.038·骷髅宽、两侧 0.091），
# 外面兜一圈等宽黑边，让它读起来是"均匀的粗轮廓"，与参考图的观感一致。
SKULL_STROKE = 0.047

# ==================== ③ 骷髅头：从 抠图骷髅头.png 重新描摹 ====================
# 放置：把源图骷髅的 bbox 映射到菜单坐标
#   y 方向与原来一致（-0.6248 ~ +0.7168），x 按源图自身比例
SK_CY_LO, SK_CY_HI = -0.6248, 0.7168      # 目标 y 范围
SK_CX_MID = 0.017                         # 目标 x 中心

TH = 128
im = Image.open(SRC_SKULL).convert("L")
W0, H0 = im.size
px0 = im.load()
xs = [x for x in range(W0) if any(px0[x, y] < TH for y in range(H0))]
ys = [y for y in range(H0) if any(px0[x, y] < TH for x in range(W0))]
BX0, BX1, BY0, BY1 = min(xs), max(xs), min(ys), max(ys)
print("骷髅源图 %dx%d  墨迹 bbox=(%d,%d)-(%d,%d)  宽%d 高%d"
      % (W0, H0, BX0, BY0, BX1, BY1, BX1-BX0, BY1-BY0))

M = 2                                     # 裁剪留边
CW, CH = (BX1-BX0+1+2*M), (BY1-BY0+1+2*M)
crop = im.crop((BX0-M, BY0-M, BX1+1+M, BY1+1+M))
INK = crop.point(lambda v: 255 if v < TH else 0)
p = INK.load()

SCALE = (SK_CY_HI - SK_CY_LO) / float(BY1-BY0)          # 归一化单位 / 源图像素
SRC_CX = (BX0 + BX1)/2.0
SRC_CY = (BY0 + BY1)/2.0
TGT_CY = (SK_CY_LO + SK_CY_HI)/2.0

def to_norm(x, y):      # 裁剪局部像素 -> 菜单归一化坐标
    sx = BX0 - M + x
    sy = BY0 - M + y
    return (SK_CX_MID + (sx - SRC_CX)*SCALE, TGT_CY + (sy - SRC_CY)*SCALE)

# ---- 掩膜工具 ----
mk = INK.load()
cells_ink = set()
for y in range(CH):
    for x in range(CW):
        if mk[x, y] > 128: cells_ink.add((x, y))

def components(cells, conn8=True):
    seen = set(); out = []
    for c in cells:
        if c in seen: continue
        q = deque([c]); seen.add(c); comp = []
        while q:
            a, b = q.popleft(); comp.append((a, b))
            for da in (-1, 0, 1):
                for db in (-1, 0, 1):
                    if da == 0 and db == 0: continue
                    if not conn8 and abs(da)+abs(db) != 1: continue
                    n = (a+da, b+db)
                    if n in cells and n not in seen:
                        seen.add(n); q.append(n)
        out.append(set(comp))
    return out

# 背景（连通到边框的白）用 4 连通
bg = set()
for y in range(CH):
    for x in range(CW):
        if (x, y) not in cells_ink: bg.add((x, y))
bg_comps = components(bg, conn8=False)
border_comps = [c for c in bg_comps if any(x == 0 or y == 0 or x == CW-1 or y == CH-1 for x, y in c)]
outside = set().union(*border_comps) if border_comps else set()
holes = bg - outside
print("墨迹 %d px，背景 %d px（其中外背景 %d，内孔 %d）" % (len(cells_ink), len(bg), len(outside), len(holes)))

# ---- 裂缝跟随追踪（曾用合成图形验证过：填充面积比像素面积多 ≈ 周长/2）----
RC = lambda x, y, d: [(x, y), (x-1, y), (x-1, y-1), (x, y-1)][d]
LC = lambda x, y, d: [(x, y-1), (x, y), (x-1, y), (x-1, y-1)][d]
DX = [1, 0, -1, 0]; DY = [0, 1, 0, -1]

def trace(cells, sx, sy):
    g = lambda x, y: 1 if (x, y) in cells else 0
    x, y, d = sx, sy, 0; start = (x, y); path = []; seen = set()
    while True:
        if (x, y, d) in seen: break
        seen.add((x, y, d)); path.append((x, y))
        moved = False
        for turn in (0, 1, 3):
            nd = (d + turn) % 4
            nx, ny = x + DX[nd], y + DY[nd]
            if g(*RC(x, y, nd)) and not g(*LC(x, y, nd)):
                x, y, d = nx, ny, nd; moved = True; break
        if not moved: break
        if (x, y) == start and len(path) >= 4: break
    ded = [path[0]]
    for q in path[1:]:
        if q != ded[-1]: ded.append(q)
    return ded

def rdp(pts, eps):
    if len(pts) < 3: return pts
    x0, y0 = pts[0]; x1, y1 = pts[-1]
    dx, dy = x1-x0, y1-y0; L = math.hypot(dx, dy)
    imax, dmax = 0, -1.0
    for i in range(1, len(pts)-1):
        px_, py_ = pts[i]
        dd = abs(dy*(px_-x0) - dx*(py_-y0))/L if L > 1e-9 else math.hypot(px_-x0, py_-y0)
        if dd > dmax: dmax, imax = dd, i
    if dmax > eps:
        return rdp(pts[:imax+1], eps)[:-1] + rdp(pts[imax:], eps)
    return [pts[0], pts[-1]]

def rdp_closed(pts, eps):
    p0 = pts[0]
    far = max(range(len(pts)), key=lambda i: (pts[i][0]-p0[0])**2 + (pts[i][1]-p0[1])**2)
    return rdp(pts[:far+1], eps)[:-1] + rdp(pts[far:] + [pts[0]], eps)[:-1]

def start_of(cells):
    sy = min(y for x, y in cells)
    sx = min(x for x, y in cells if y == sy)
    return sx, sy

def trace_simple(cells):
    """裂缝跟随：只走「一侧在内部、另一侧在外部」的边界边（凹角处不会跑飞）。"""
    sx, sy = start_of(cells)
    return trace(cells, sx, sy)

def xor_polygon(acc, pts):
    """把多边形按 EvenOdd 累进 acc（L 图，255 = 已填充）。"""
    one = Image.new("L", (CW, CH), 0); ImageDraw.Draw(one).polygon(pts, fill=255)
    return ImageChops.difference(acc, one)

# 墨迹的每个连通块 + 每个内孔，各取一条轮廓
ink_comps = components(cells_ink, conn8=True)
hole_comps = components(holes, conn8=True) if holes else []
print("墨迹连通块 %d 个，内孔 %d 个" % (len(ink_comps), len(hole_comps)))

# 剪影 = 墨迹并上内孔
sil_cells = set(cells_ink) | set(holes)
print("剪影连通块 %d 个" % len(components(sil_cells, conn8=True)))

LOOPS = []          # (归一化点列, 像素数)
ACC = Image.new("L", (CW, CH), 0)              # EvenOdd 累加，用于端到端自检
for i, comp in enumerate(ink_comps):
    if len(comp) < 40:
        print("   跳过碎块 %d px" % len(comp)); continue
    pts = trace_simple(comp)
    ACC = xor_polygon(ACC, pts)
    simp = rdp_closed(pts, 1.5)
    LOOPS.append(([to_norm(x, y) for x, y in simp], len(comp)))
    print("   墨迹#%d 像素=%-7d 角点=%-6d 简化=%d" % (i, len(comp), len(pts), len(simp)))
for i, comp in enumerate(hole_comps):
    if len(comp) < 40:
        print("   跳过小孔 %d px" % len(comp)); continue
    pts = trace_simple(comp)
    ACC = xor_polygon(ACC, pts)
    simp = rdp_closed(pts, 1.5)
    LOOPS.append(([to_norm(x, y) for x, y in simp], -len(comp)))
    print("   内孔#%d 像素=%-7d 角点=%-6d 简化=%d" % (i, len(comp), len(pts), len(simp)))

# ---- 端到端自检：EvenOdd 叠出来的墨迹 应 与源图墨迹一致 ----
ac = ACC.load()
ink_xor = sum(1 for y in range(CH) for x in range(CW)
              if (ac[x, y] > 128) != ((x, y) in cells_ink))
print("EvenOdd 重建 vs 源墨迹：异或 %d px / 墨迹 %d px = %.2f%%"
      % (ink_xor, len(cells_ink), 100.0*ink_xor/len(cells_ink)))
assert ink_xor < 0.06*len(cells_ink), "EvenOdd 重建与原墨迹偏差过大"

# 剪影轮廓（写字用）
sil_big = max(components(sil_cells, conn8=True), key=len)
SIL = rdp_closed(trace(sil_big, *start_of(sil_big)), 1.6)
SIL_N = [to_norm(x, y) for x, y in SIL]
print("剪影轮廓 %d 点；墨迹/内孔轮廓合计 %d 条" % (len(SIL_N), len(LOOPS)))

# ==================== 写 C# ====================
def s_pairs(pts): return " ".join("%.4f,%.4f" % (x, y) for x, y in pts)
def s_norm(pts): return " ".join("%.4f,%.4f" % (x, y) for x, y in pts)

L = []; A = L.append
A("using System.Collections.Generic;")
A("using System.Windows;")
A("")
A("namespace WinLoop.Menus")
A("{")
A("    /// <summary>")
A("    /// 「八角星」菜单的三层几何 —— 由参考图量取，请勿手工编辑。")
A("    ///")
A("    /// 堆叠顺序（自下而上）：① 同心圆  ② 八角星  ③ 骷髅头。")
A("    ///")
A("    /// ② 八角星 = **严格几何构造**（用户给定参考图反推，逐像素差异率 3.0%）：")
A("    ///     黑 = 标准星多边形（尖 StarTipRadius / 谷 StarValleyRadius，全直线边）")
A("    ///          − 8 个三角孔 [ (StarHoleTipRadius, 尖轴) ,")
A("    ///                          (StarHoleMidRadius, 尖轴 + StarHoleAngleDeg) ,")
A("    ///                          (圆心) ]")
A("    ///     尖轴 = -90° + 45°k；画完黑星再用底色填 8 个孔，")
A("    ///     所以孔会顺带把圆环一起切开（与参考图一致）。")
A("    /// ③ 骷髅头描摹自用户提供的 `抠图骷髅头.png`（干净版）：")
A("    ///     SkullSilhouetteLoops 是剪影（白底，负责遮挡下层），")
A("    ///     SkullInkLoops 是全部墨迹轮廓（黑，按 EvenOdd 填，内含眼白/牙缝等）。")
A("    ///")
A("    /// 坐标系：原点 = 菜单中心，1.0 = 星尖顶点半径，y 轴向下为正。")
A("    /// 生成脚本：tmp/ref/zf1_export.py")
A("    /// </summary>")
A("    internal static class CSHeadshotLayers")
A("    {")
A("        // ---- ① 同心圆：两圈完整圆环的半径区间 ----")
A("        // 注意：这几个数是**已补偿过绘制膨胀**的设计值，不是参考图的实测值！")
A("        // 圆环用 _seamPen(宽 %.3f) 描边绘制，会在几何外再膨胀 ±%.3f，" % (0.006, 0.003))
A("        // 加上抗锯齿，实测渲染出来的带比这里写的厚约 0.007、内圈整体向内偏 0.006。")
A("        // 参考图实测 = 内圈 %.3f~%.3f、外圈 %.3f~%.3f（避开星臂角度平均）。" % (0.592, 0.665, 0.742, 0.800))
A("        public const double RingInnerLow = %.4f;" % R_IN_LO)
A("        public const double RingInnerHigh = %.4f;" % R_IN_HI)
A("        public const double RingOuterLow = %.4f;" % R_OUT_LO)
A("        public const double RingOuterHigh = %.4f;" % R_OUT_HI)
A("")
A("        // ---- ② 八角星（严格几何构造） ----")
A("        /// <summary>星尖顶点半径。</summary>")
A("        public const double StarTipRadius = %.4f;" % R_TIP)
A("        /// <summary>相邻两尖之间谷处的半径（标准星形的内顶点）。</summary>")
A("        public const double StarValleyRadius = %.4f;" % R_VALLEY)
A("        /// <summary>三角孔在尖轴上的顶点半径（每支尖被斜削掉一块的外端）。</summary>")
A("        public const double StarHoleTipRadius = %.4f;" % HOLE_TIP_R)
A("        /// <summary>三角孔的中间顶点半径。</summary>")
A("        public const double StarHoleMidRadius = %.4f;" % HOLE_MID_R)
A("        /// <summary>三角孔的中间顶点相对尖轴的偏角（负 = 逆时针那一侧）。</summary>")
A("        public const double StarHoleAngleDeg = %.1f;" % HOLE_ANGLE)
A("")
A("        // ---- ③ 骷髅头（描摹自 抠图骷髅头.png） ----")
A("        /// <summary>")
A("        /// 骷髅轮廓的等宽描边宽度（归一化单位）。描摹出来的轮廓厚薄不均，")
A("        /// 外面兜一圈等宽黑边，读数才接近参考图那种均匀的粗轮廓。")
A("        /// </summary>")
A("        public const double SkullStrokeWidth = %.4f;" % SKULL_STROKE)
A("")
A("        /// <summary>剪影：整块填底色的外轮廓，负责遮挡下层。</summary>")
A("        public static readonly string[] SkullSilhouetteLoops =")
A("        {")
A('            "%s",' % s_norm(SIL_N))
A("        };")
A("")
A("        /// <summary>墨迹轮廓（含内孔），按 EvenOdd 填充。</summary>")
A("        public static readonly string[] SkullInkLoops =")
A("        {")
for pts, area in LOOPS:
    A('            "%s",' % s_norm(pts))
A("        };")
A("")
A('        /// <summary>解析 "x,y x,y ..." 形式的折线；点数不足 3 返回 null。</summary>')
A("        public static Point[] ParseOne(string text)")
A("        {")
A("            if (string.IsNullOrWhiteSpace(text)) return null;")
A("            var list = new List<Point>();")
A("            foreach (string pair in text.Split(' '))")
A("            {")
A("                int comma = pair.IndexOf(',');")
A("                if (comma <= 0) continue;")
A("                double x, y;")
A("                if (double.TryParse(pair.Substring(0, comma), System.Globalization.NumberStyles.Float,")
A("                        System.Globalization.CultureInfo.InvariantCulture, out x) &&")
A("                    double.TryParse(pair.Substring(comma + 1), System.Globalization.NumberStyles.Float,")
A("                        System.Globalization.CultureInfo.InvariantCulture, out y))")
A("                {")
A("                    list.Add(new Point(x, y));")
A("                }")
A("            }")
A("            return list.Count >= 3 ? list.ToArray() : null;")
A("        }")
A("")
A("        public static List<Point[]> Parse(string[] source)")
A("        {")
A("            var result = new List<Point[]>();")
A("            if (source == null) return result;")
A("            foreach (string item in source)")
A("            {")
A("                Point[] loop = ParseOne(item);")
A("                if (loop != null) result.Add(loop);")
A("            }")
A("            return result;")
A("        }")
A("    }")
A("}")
io.open(OUT, "w", encoding="utf-8-sig").write("\r\n".join(L) + "\r\n")
print("wrote %s" % OUT)
print("  骷髅剪影 %d 点；墨迹轮廓 %d 条（合计 %d 点）"
      % (len(SIL_N), len(LOOPS), sum(len(t) for t, _ in LOOPS)))
