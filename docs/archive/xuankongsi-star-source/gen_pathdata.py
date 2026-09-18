# -*- coding: utf-8 -*-
"""gen_pathdata.py —— 由参考图 r2.jpg 生成 WinLoop/Menus/CSHeadshotPathData.cs

产出两组轮廓：
  InkLoops  —— 黑色墨迹区域（含镂空），用「奇偶填充」直接绘制图形本体
  BodyLoops —— 整体外轮廓（墨迹 + 被墨迹包围的空白），用作底衬填充
坐标：原点 = 图标中心，单位 = 尖刺顶点半径 R，y 轴向下为正。
"""
import math
import os
from PIL import Image

REF = r"C:\Users\Wu\WorkBuddy\2026-09-14-15-56-03\WinLoop\tmp\ref"
SRC = REF + r"\r2.jpg"
DST = r"C:\Users\Wu\WorkBuddy\2026-09-14-15-56-03\WinLoop\WinLoop\Menus\CSHeadshotPathData.cs"

CX, CY, RT = 269.37, 269.60, 270.0
TH = 128
TOL = 1.2
MIN_COMP = 8

im = Image.open(SRC).convert("L")
W, H = im.size
px = im.load()
blk = [[1 if px[x, y] < TH else 0 for x in range(W)] for y in range(H)]


def comps_of(mask):
    seen = [[0] * W for _ in range(H)]
    out = []
    for y0 in range(H):
        for x0 in range(W):
            if not mask[y0][x0] or seen[y0][x0]:
                continue
            st = [(x0, y0)]
            seen[y0][x0] = 1
            cells = []
            while st:
                x, y = st.pop()
                cells.append((x, y))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < W and 0 <= ny < H and mask[ny][nx] and not seen[ny][nx]:
                        seen[ny][nx] = 1
                        st.append((nx, ny))
            out.append(cells)
    out.sort(key=len, reverse=True)
    return out


def trace(keep):
    def ink(x, y):
        return 0 <= x < W and 0 <= y < H and blk[y][x] == 1 and (x, y) in keep

    oe = {}
    for (x, y) in keep:
        if not ink(x, y - 1):
            oe.setdefault((x, y), []).append((x + 1, y))
        if not ink(x + 1, y):
            oe.setdefault((x + 1, y), []).append((x + 1, y + 1))
        if not ink(x, y + 1):
            oe.setdefault((x + 1, y + 1), []).append((x, y + 1))
        if not ink(x - 1, y):
            oe.setdefault((x, y + 1), []).append((x, y))

    def rot(d, k):
        a, b = d
        for _ in range(k % 4):
            a, b = -b, a
        return (a, b)

    def dv(a, b):
        return (b[0] - a[0], b[1] - a[1])

    used = set()
    loops = []
    for s, tos in oe.items():
        for t in tos:
            if (s, t) in used:
                continue
            lp = [s]
            cur, nxt = s, t
            used.add((cur, nxt))
            while nxt != s:
                lp.append(nxt)
                d = dv(cur, nxt)
                cur = nxt
                pick = None
                for k in (1, 0, 3, 2):
                    w = rot(d, k)
                    for c in oe.get(cur, ()):
                        if (cur, c) not in used and dv(cur, c) == w:
                            pick = c
                            break
                    if pick:
                        break
                if pick is None:
                    break
                used.add((cur, pick))
                nxt = pick
            if len(lp) >= 6:
                loops.append(lp)
    return loops


def dp(pts, tol):
    n = len(pts)
    if n < 3:
        return pts
    kf = [False] * n
    kf[0] = kf[-1] = True
    st = [(0, n - 1)]
    while st:
        i, j = st.pop()
        if j <= i + 1:
            continue
        ax, ay = pts[i]
        bx, by = pts[j]
        dx, dy = bx - ax, by - ay
        L = math.hypot(dx, dy)
        best, bi = -1.0, -1
        for k in range(i + 1, j):
            qx, qy = pts[k]
            dd = math.hypot(qx - ax, qy - ay) if L < 1e-9 else abs(dx * (ay - qy) - (ax - qx) * dy) / L
            if dd > best:
                best, bi = dd, k
        if best > tol:
            kf[bi] = True
            st.append((i, bi))
            st.append((bi, j))
    return [pts[k] for k in range(n) if kf[k]]


def norm(loops):
    return [[(round((x - CX) / RT, 4), round((y - CY) / RT, 4)) for x, y in s] for s in loops]


# ---------- A. 墨迹 ----------
c = comps_of(blk)
keep_ink = set()
n_used = 0
for cc in c:
    if len(cc) >= MIN_COMP:
        keep_ink.update(cc)
        n_used += 1
print(f"墨迹连通分量 {len(c)}，保留 {n_used}（像素 {len(keep_ink)}）")

# ---------- B. 整体轮廓（从边界泛洪白色，未被淹没的即为图形实体） ----------
outside = [[0] * W for _ in range(H)]
st = []
for x in range(W):
    for y in (0, H - 1):
        if not blk[y][x] and not outside[y][x]:
            outside[y][x] = 1
            st.append((x, y))
for y in range(H):
    for x in (0, W - 1):
        if not blk[y][x] and not outside[y][x]:
            outside[y][x] = 1
            st.append((x, y))
while st:
    x, y = st.pop()
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < W and 0 <= ny < H and not blk[ny][nx] and not outside[ny][nx]:
            outside[ny][nx] = 1
            st.append((nx, ny))
blk = [[0 if outside[y][x] else 1 for x in range(W)] for y in range(H)]
c2 = comps_of(blk)
keep_body = set()
for cc in c2:
    if len(cc) >= MIN_COMP:
        keep_body.update(cc)
print(f"整体轮廓连通分量 {len(c2)}，保留 {sum(1 for x in c2 if len(x) >= MIN_COMP)}（像素 {len(keep_body)}）")

ink_loops = norm([dp(l, TOL) for l in trace(keep_ink)])
body_loops = norm([dp(l, TOL) for l in trace(keep_body)])
print(f"墨迹回路 {len(ink_loops)} 条 / {sum(len(s) for s in ink_loops)} 点；"
      f"轮廓回路 {len(body_loops)} 条 / {sum(len(s) for s in body_loops)} 点")

mx = max(math.hypot(x, y) for s in ink_loops + body_loops for x, y in s)
print(f"最大归一化半径 {mx:.4f}")

# ---------- 输出 ----------
L = []
L.append("using System;")
L.append("using System.Collections.Generic;")
L.append("using System.Globalization;")
L.append("using System.Windows;")
L.append("")
L.append("namespace WinLoop.Menus")
L.append("{")
L.append("    /// <summary>")
L.append("    /// 「八角星」菜单的轮廓数据 —— 由参考图矢量化（描摹）得到，请勿手工编辑。")
L.append("    ///")
L.append("    /// 坐标系：原点 = 图标中心（菜单中心），单位 = 尖刺顶点半径 R，y 轴向下为正。")
L.append("    ///   InkLoops  —— 黑色墨迹区域。多条回路按「奇偶填充(EvenOdd)」合成，")
L.append("    ///                因此圆环凹槽、尖刺中缝、骷髅五官等镂空会自动成为孔洞。")
L.append("    ///   BodyLoops —— 图形整体外轮廓（墨迹 + 被墨迹包围的空白），用作底衬填充。")
L.append("    ///")
L.append("    /// 生成脚本：tmp/ref/gen_pathdata.py（源图 tmp/ref/r2.jpg，DP 容差 1.2px）")
L.append(f"    /// 数据规模：墨迹 {len(ink_loops)} 回路/{sum(len(s) for s in ink_loops)} 点，"
         f"轮廓 {len(body_loops)} 回路/{sum(len(s) for s in body_loops)} 点。")
L.append("    /// </summary>")
L.append("    internal static class CSHeadshotPathData")
L.append("    {")
L.append("        /// <summary>图形最大半径（归一化单位）。菜单半尺寸按它留白，避免尖角被裁。</summary>")
L.append(f"        public const double MaxRadius = {mx:.4f};")
L.append("")


def emit(name, src, doc):
    L.append(f"        /// <summary>{doc}</summary>")
    L.append(f"        public static readonly string[] {name} =")
    L.append("        {")
    for s in src:
        L.append('            "' + " ".join(f"{x:.4f},{y:.4f}" for x, y in s) + '",')
    L.append("        };")
    L.append("")


emit("InkLoops", ink_loops, '墨迹回路，每项格式："x,y x,y ..."（不变文化，4 位小数）。')
emit("BodyLoops", body_loops, '整体外轮廓回路，格式同上。')

L.append("        /// <summary>把字符串数组解析成点序列。返回 null 表示数据损坏。</summary>")
L.append("        public static List<Point[]> Parse(string[] source)")
L.append("        {")
L.append("            if (source == null) return null;")
L.append("            var result = new List<Point[]>(source.Length);")
L.append("            foreach (string line in source)")
L.append("            {")
L.append("                string[] tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);")
L.append("                if (tokens.Length < 3) return null;")
L.append("                var pts = new Point[tokens.Length];")
L.append("                for (int i = 0; i < tokens.Length; i++)")
L.append("                {")
L.append("                    int comma = tokens[i].IndexOf(',');")
L.append("                    if (comma <= 0 || comma >= tokens[i].Length - 1) return null;")
L.append("                    double x, y;")
L.append("                    if (!double.TryParse(tokens[i].Substring(0, comma), NumberStyles.Float,")
L.append("                            CultureInfo.InvariantCulture, out x)) return null;")
L.append("                    if (!double.TryParse(tokens[i].Substring(comma + 1), NumberStyles.Float,")
L.append("                            CultureInfo.InvariantCulture, out y)) return null;")
L.append("                    pts[i] = new Point(x, y);")
L.append("                }")
L.append("                result.Add(pts);")
L.append("            }")
L.append("            return result;")
L.append("        }")
L.append("    }")
L.append("}")

with open(DST, "w", encoding="utf-8-sig", newline="\r\n") as f:
    f.write("\n".join(L))
print(f"已写出 {DST}（{os.path.getsize(DST)} 字节）")
