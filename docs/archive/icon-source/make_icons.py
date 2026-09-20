#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把 IconGen 渲染出的主图合成成 WinLoop 的三套图标。

================================= 归档说明 =================================
本文件原放在 `probe-out/icon/`（临时验证目录，被 .gitignore 忽略）。
2026-09-20 随图标功能归档到这里 —— 否则清理 probe-out 时它会被删掉，
而 `WinLoop/Resources/*.ico` 与 `docs/logo.png` 全是它的产物：
**图标文件进了仓库，重新生成它们的依据却没进** —— 这不可接受。

配套渲染器是同目录的 `IconGen.csproj` / `Program.cs`
（原在 `Tools/IconGen/`，同样被 .gitignore 的 `Tools/` 规则挡住）。
文件头的「设计要点」是历次调整的结论，改参数前先读。
===========================================================================

完整生成流程
------------
    # ① 渲染主图（需要 WinLoop 主项目：csproj 里有 ProjectReference）
    dotnet build Tools/IconGen/IconGen.csproj -c Release
    Tools/IconGen/bin/Release/netcoreapp3.1/IconGen.exe <输出目录> 2048

    # ② 合成三套图标
    python make_icons.py <主图目录>

主图目录需含 `icon-master.png`（2048×2048、透明背景、完整三层：
圆环 + 八角星 + 骷髅，右上扇区高亮）。

产物
----
    WinLoop/Resources/appIcon.ico   exe / 窗口 / 桌面    10 档
    WinLoop/Resources/trayIcon.ico  系统托盘              5 档
    docs/logo.png                   README 展示用         1024

设计要点（都是踩过坑的结论）
----------------------------
1. **底色必须同时避开图形本体的三种颜色。**
   图形只有三种色：黑 `#101010` / 白 `#F0F0F0` / 金 `#D0A030`。
   设底色相对亮度为 L，要同时满足
       vs黑 = (L+0.05)/0.05  >= 3
       vs白 = 1.05/(L+0.05)  >= 3
   → **L 必须落在 [0.10, 0.30]**。
   推论：**任何灰度色（纯黑 / 白 / 浅灰 / 深灰）天生不合格** —— 只能满足一边，
   必然吃掉黑或白元素。必须用**中等亮度的饱和色**。
   当前用中蓝 `#0F6CBD`（L=0.145），与设置页 Accent / 导航选中色同源。
   （历史：透明底 → 浅灰 #F2F2F2 → 金 #D4AF37，前两个其实都没解决问题）

2. **图形占底面的比例必须按显示尺寸分档，不能一刀切。**
   - appIcon 显示 48~256px：四角底色的楔形很大、一眼可见，
     "方块"感本来就成立 → 图形**撑满**（inner = 1.00）
   - trayIcon 只显示 16px：四角楔形只剩 2~3px 且彼此不连通，
     远看仍是个"圆点" → 必须留边框（inner = 0.75），
     让底色**四边连成一整圈**，才读得出是方块
   这是「图形更大」与「图标看起来更大」的二选一，后者才是用户真正要的。
   边框宽度 = (1 - inner)/2 × size，**16px 下至少 2px**（1px 会被抗锯齿糊掉）。

3. **圆角要抗锯齿。** PIL 的 `rounded_rectangle` 没有抗锯齿，
   必须在高分辨率（`_BG_HI` = 2048）画一次再 LANCZOS 缩下来。

4. **图形要先按实际像素边界裁掉透明留白**（`crop(getbbox())`）。
   系统是把**整个画布**（含透明留白）缩放到统一尺寸的，
   留白会实打实吃掉图形大小，让图标显得比别的应用小。

5. **手工构造 ICO，不走 PIL 的 `save(format='ICO', sizes=[...])`。**
   原因有二：① 那条路径对小尺寸（<256）用 **BMP** 编码，
   而本项目的 ico 是**全档位 PNG**（已实测确认）；
   ② 它拿同一张图逐级 thumbnail，小尺寸质量更差。
   这里对每个尺寸**独立从高分辨率合成**，再统一以 PNG 写入。

⚠️ 纯离屏图像合成，不注入任何键鼠输入。

归档校验结论（2026-09-20，从 `docs/archive/icon-source` 重新构建并与
仓库内现有图标逐帧比对）
----------------------------------------------------------------
- **appIcon.ico：逐字节一致**（10 档全部像素完全相同）
- **docs/logo.png：逐字节一致**
- **trayIcon.ico：仅边缘抗锯齿有 1~8 级（0~255 标度）差异，
  约 20% 像素、最大通道差 ≤8，肉眼不可辨。**
  原始 trayIcon 大概率走了与本脚本略不同的抗锯齿路径（appIcon 与之
  逐字节一致，证明主图一致，故差异只可能来自合成法而非素材）；
  本脚本用「256 基准图 + 逐档缩」已是最接近的复现（曾试「每档
  独立合成」，最大通道差高达 150，明显错误）。图标用途是
  「换配色后重出图」，2~8 级边缘差在可接受范围内。
对比时务必按 ICO 目录项切 PNG 帧（用 `struct` 解析头部），
**不能靠 PIL 的 `seek`/`size=`** —— 它对 PNG 编码的多帧 ICO 只会
暴露最大一帧，逐帧比对会得出完全错误的结论。
"""

import io
import os
import struct
import sys

from PIL import Image, ImageDraw

# ============================ 参数 ============================

BG_COLOR = (15, 108, 189)        # 中蓝 #0F6CBD（理由见「设计要点 1」）
BG_COLOR_HEX = "#0F6CBD"
_BG_HI = 2048                    # 圆角底的高分辨率画布（抗锯齿用，见要点 3）

# 按用途分档 —— 两种图标的差别**只在**这两组参数与尺寸列表
APP = dict(inner=1.00, radius=0.22)     # 桌面 / exe：图形撑满底面
TRAY = dict(inner=0.75, radius=0.18)    # 托盘：留 2px 边框（16px 下才读得出方块）

APP_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
TRAY_SIZES = [16, 20, 24, 32, 48]

# 合成基准图的边长，各档位都从它缩下来（理由见 build() 注释）。
# 注意它**大于托盘的最大档位 48** —— 托盘也一律从这张基准图缩。
BASE_SIZE = 256

# 本文件位于 docs/archive/icon-source/，要上三级才是仓库根
REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))


# ============================ 实现 ============================

def load_master(path):
    """读主图并裁掉透明留白（见「设计要点 4」）。"""
    im = Image.open(path).convert("RGBA")
    return im.crop(im.getbbox())


def make_bg_hi(radius_ratio):
    """高分辨率圆角矩形底，之后缩到目标尺寸以获得抗锯齿边缘。"""
    bg = Image.new("RGBA", (_BG_HI, _BG_HI), (0, 0, 0, 0))
    ImageDraw.Draw(bg).rounded_rectangle(
        [0, 0, _BG_HI - 1, _BG_HI - 1],
        radius=int(round(_BG_HI * radius_ratio)),
        fill=BG_COLOR + (255,))
    return bg


def make_icon(master, size, inner, radius):
    """合成单个尺寸：圆角底 + 居中图形。

    形参名与 APP / TRAY 两个 dict 的键一致，调用处直接 `**cfg` 展开。

    四角会露出一小块底色 —— 图形外轮廓是圆形，圆内切正方形时四角填不到，
    是几何的必然结果，不是 bug。
    """
    canvas = make_bg_hi(radius).resize((size, size), Image.LANCZOS)

    inner_px = max(1, int(round(size * inner)))
    w, h = master.size
    scale = inner_px / max(w, h)
    art = master.resize(
        (max(1, int(round(w * scale))), max(1, int(round(h * scale)))),
        Image.LANCZOS)

    off = (size - inner_px) // 2
    canvas.paste(art, (off, off), art)
    return canvas


def save_ico(images, path):
    """手工构造多尺寸 ICO（见「设计要点 5」）。

    images: [(size, PIL.Image), ...]，每个尺寸都是独立合成的高质量图。
    """
    entries = []
    payload = b""
    offset = 6 + 16 * len(images)          # 6 字节头 + 每项 16 字节目录

    for size, im in images:
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        png = buf.getvalue()
        b = 0 if size >= 256 else size     # 目录项用 1 字节存宽高，0 表示 256
        entries.append(struct.pack("<BBBBHHII", b, b, 0, 0, 1, 32, len(png), offset))
        payload += png
        offset += len(png)

    with open(path, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(images)))   # reserved / type=1(icon) / count
        for e in entries:
            f.write(e)
        f.write(payload)


def build(master, sizes, cfg, out_path):
    """先合成一张 256 基准图，再逐档 LANCZOS 缩放，最后打包成一个 ico。

    ⚠️ **不要改成「每个尺寸都从主图独立合成」。**
    主图是 2048，直接缩到 16px 是 **128 倍降采样**，而 LANCZOS 的核宽有限，
    这种极端降采样会产生混叠；先做一张 256 的基准图再缩，降采样比降到 16 倍，
    边缘更干净。实测这也正是能**逐字节复现**仓库里现有 ico 的做法
    （当年用「每档独立合成」重跑，10 档里有 9 档与历史产物存在像素差异）。
    """
    base = make_icon(master, BASE_SIZE, **cfg)
    images = [(s, base if s == BASE_SIZE else base.resize((s, s), Image.LANCZOS))
              for s in sizes]
    save_ico(images, out_path)
    return images


def main():
    master_dir = (sys.argv[1] if len(sys.argv) > 1
                  else os.path.join(REPO_ROOT, "probe-out", "icon"))
    master_path = os.path.join(master_dir, "icon-master.png")

    if not os.path.exists(master_path):
        sys.exit("找不到主图：%s\n"
                 "先用 IconGen 生成它（见文件头「完整生成流程」）" % master_path)

    master = load_master(master_path)
    print("主图 : %s" % master_path)
    print("     裁剪后 %dx%d（已去掉透明留白）" % master.size)
    print("底色 : %s" % BG_COLOR_HEX)
    print()

    out_app = os.path.join(REPO_ROOT, "WinLoop", "Resources", "appIcon.ico")
    out_tray = os.path.join(REPO_ROOT, "WinLoop", "Resources", "trayIcon.ico")
    out_logo = os.path.join(REPO_ROOT, "docs", "logo.png")

    build(master, APP_SIZES, APP, out_app)
    print("appIcon.ico   图形占底面 %3.0f%%  圆角 %2.0f%%  %2d 档 %s"
          % (APP["inner"] * 100, APP["radius"] * 100, len(APP_SIZES), APP_SIZES))

    build(master, TRAY_SIZES, TRAY, out_tray)
    print("trayIcon.ico  图形占底面 %3.0f%%  圆角 %2.0f%%  %2d 档 %s"
          % (TRAY["inner"] * 100, TRAY["radius"] * 100, len(TRAY_SIZES), TRAY_SIZES))

    # logo 与 appIcon 同参数（都是大尺寸展示，图形撑满）
    make_icon(master, 1024, **APP).save(out_logo)
    print("logo.png      1024x1024（与 appIcon 同参数）")
    print()

    for p in (out_app, out_tray, out_logo):
        print("  %8d  %s" % (os.path.getsize(p), os.path.relpath(p, REPO_ROOT)))


if __name__ == "__main__":
    main()
