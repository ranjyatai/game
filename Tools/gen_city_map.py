"""虚构城市路网图生成器（黑底白线），用作 RAID 选本地图的占位底图。

用法：python Tools/gen_city_map.py [--seed 1] [--w 2400] [--h 1500] [--out Assets/Screenshots/city_map_placeholder.png]
同一个 seed 出同一张图；换 seed 反复出图挑一张。
层级：地形(河/公园) -> 主干道(放射+环线) -> 街区(带密度噪声的 Voronoi) -> 小巷(再细分) -> 桥。
"""
import argparse
import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.spatial import Voronoi

SS = 2  # 超采样倍数，最后缩小抗锯齿


def smooth_noise(w, h, scale, rng):
    """低频噪声 0~1：小随机图放大。"""
    gw, gh = max(2, int(w / scale) + 2), max(2, int(h / scale) + 2)
    g = rng.random((gh, gw)).astype(np.float32)
    im = Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return np.asarray(im, dtype=np.float32) / 255.0


def river_points(w, h, rng, width):
    """一条从左到右、带大弯的河中心线。"""
    n = 60
    pts = []
    phase = rng.uniform(0, 6.28)
    amp = h * rng.uniform(0.18, 0.28)
    base = h * rng.uniform(0.4, 0.6)
    f = rng.uniform(1.0, 1.8)
    for i in range(n + 1):
        t = i / n
        x = t * w * 1.2 - w * 0.1
        y = base + amp * math.sin(t * math.pi * 2 * f + phase) + amp * 0.35 * math.sin(t * 9 + phase * 2)
        pts.append((x, y))
    return pts


def build(seed, W, H):
    rng_py = random.Random(seed)
    rng = np.random.default_rng(seed)
    w, h = W * SS, H * SS

    # ---------- 地形遮罩 ----------
    river_w = h * rng_py.uniform(0.045, 0.07)
    rpts = [(x * SS, y * SS) for x, y in river_points(W, H, rng_py, river_w / SS)]
    river = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(river)
    for i in range(len(rpts) - 1):
        ww = river_w * (0.8 + 0.4 * math.sin(i * 0.5))
        d.line([rpts[i], rpts[i + 1]], fill=255, width=int(ww))
        d.ellipse([rpts[i][0] - ww / 2, rpts[i][1] - ww / 2, rpts[i][0] + ww / 2, rpts[i][1] + ww / 2], fill=255)
    river = river.filter(ImageFilter.GaussianBlur(6 * SS))
    river = river.point(lambda v: 255 if v > 128 else 0)

    park = Image.new("L", (w, h), 0)
    dp = ImageDraw.Draw(park)
    for _ in range(rng_py.randint(3, 5)):
        cx, cy = rng_py.uniform(0.1, 0.9) * w, rng_py.uniform(0.1, 0.9) * h
        rx, ry = rng_py.uniform(0.03, 0.07) * w, rng_py.uniform(0.04, 0.09) * h
        dp.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=255)
    park = park.filter(ImageFilter.GaussianBlur(10 * SS)).point(lambda v: 255 if v > 128 else 0)

    river_a = np.asarray(river) > 0
    park_a = np.asarray(park) > 0
    blocked = river_a | park_a

    def is_blocked(x, y):
        xi, yi = int(x), int(y)
        if xi < 0 or yi < 0 or xi >= w or yi >= h:
            return False
        return blocked[yi, xi]

    def in_river(x, y):
        xi, yi = int(x), int(y)
        return 0 <= xi < w and 0 <= yi < h and river_a[yi, xi]

    img = Image.new("L", (w, h), 0)
    dr = ImageDraw.Draw(img)

    # ---------- 密度场：老城区密、新区疏 ----------
    dens = smooth_noise(w, h, w / 3.5, rng)

    def voronoi_streets(count, line_w, density_gain, margin=0.05):
        pts = []
        tries = 0
        while len(pts) < count and tries < count * 40:
            tries += 1
            x, y = rng_py.uniform(-margin, 1 + margin) * w, rng_py.uniform(-margin, 1 + margin) * h
            xi, yi = min(max(int(x), 0), w - 1), min(max(int(y), 0), h - 1)
            if rng_py.random() < dens[yi, xi] ** density_gain:
                pts.append((x, y))
        vor = Voronoi(np.array(pts))
        for rv in vor.ridge_vertices:
            if -1 in rv:
                continue
            a, b = vor.vertices[rv[0]], vor.vertices[rv[1]]
            if not (-50 < a[0] < w + 50 and -50 < a[1] < h + 50):
                continue
            mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
            if is_blocked(mx, my) or is_blocked(*a) or is_blocked(*b):
                continue
            dr.line([tuple(a), tuple(b)], fill=255, width=line_w)

    # 分区：每区一套随机朝向/间距的方格街道，区边界带噪声扭曲
    from scipy.spatial import cKDTree
    K = rng_py.randint(14, 20)
    seeds = np.array([(rng_py.uniform(0, w), rng_py.uniform(0, h)) for _ in range(K)])
    gw, gh = w // 4, h // 4
    yy, xx = np.mgrid[0:gh, 0:gw]
    warp = (smooth_noise(gw, gh, gw / 6, rng) - 0.5) * w * 0.12
    warp2 = (smooth_noise(gw, gh, gw / 6, rng) - 0.5) * w * 0.12
    q = np.stack([xx * 4 + warp, yy * 4 + warp2], -1).reshape(-1, 2)
    lab = cKDTree(seeds).query(q)[1].reshape(gh, gw)
    lab_img = Image.fromarray(lab.astype(np.uint8)).resize((w, h), Image.NEAREST)

    def grid_layer(theta, spacing, jitter, lw, gap):
        lay = Image.new("L", (w, h), 0)
        dl = ImageDraw.Draw(lay)
        diag = math.hypot(w, h)
        c, sn = math.cos(theta), math.sin(theta)
        off = -diag / 2
        while off < diag / 2:
            # 一条街，沿 theta 方向，切成若干段并随机缺口（形成断头路）
            cx, cy = w / 2 - sn * off, h / 2 + c * off
            t = -diag / 2
            while t < diag / 2:
                seg = rng_py.uniform(40, 220) * SS
                if rng_py.random() > gap:
                    dl.line([(cx + c * t, cy + sn * t), (cx + c * (t + seg), cy + sn * (t + seg))], fill=255, width=lw)
                t += seg
            off += spacing * rng_py.uniform(1 - jitter, 1 + jitter)
        return lay

    street = Image.new("L", (w, h), 0)
    for k in range(K):
        theta = rng_py.uniform(0, math.pi)
        dense = rng_py.random() ** 1.3          # 0=稀疏新区 1=密集老城
        sp = (24 - 14 * dense) * SS
        mask = lab_img.point(lambda v, kk=k: 255 if v == kk else 0)
        lay = grid_layer(theta, sp, 0.5, int(SS * 2.2), 0.08)
        lay = Image.composite(lay, Image.new("L", (w, h), 0), mask)
        street = Image.fromarray(np.maximum(np.asarray(street), np.asarray(lay)))
        theta2 = theta + math.pi / 2 + rng_py.uniform(-0.25, 0.25)
        lay2 = grid_layer(theta2, sp * rng_py.uniform(0.9, 1.4), 0.5, int(SS * 2.0), 0.15)
        lay2 = Image.composite(lay2, Image.new("L", (w, h), 0), mask)
        street = Image.fromarray(np.maximum(np.asarray(street), np.asarray(lay2)))
    # 去掉河/公园内的街
    street = Image.fromarray(np.where(blocked, 0, np.asarray(street)).astype(np.uint8))
    # 整体噪声扭曲：笔直的方格变成略带弯曲的老城街道
    from scipy.ndimage import map_coordinates
    amp = 9 * SS
    dx = (smooth_noise(w, h, 140 * SS, rng) - 0.5) * 2 * amp + (smooth_noise(w, h, 40 * SS, rng) - 0.5) * amp * 0.5
    dy = (smooth_noise(w, h, 140 * SS, rng) - 0.5) * 2 * amp + (smooth_noise(w, h, 40 * SS, rng) - 0.5) * amp * 0.5
    Y, X = np.mgrid[0:h, 0:w].astype(np.float32)
    sw = map_coordinates(np.asarray(street).astype(np.float32), [Y + dy, X + dx], order=1, mode="nearest")
    street = Image.fromarray(np.where(blocked, 0, sw).astype(np.uint8))
    img = street
    dr = ImageDraw.Draw(img)

    # ---------- 主干道：几个中心点放射 + 环线 ----------
    hubs = [(rng_py.uniform(0.25, 0.75) * w, rng_py.uniform(0.25, 0.75) * h) for _ in range(rng_py.randint(3, 4))]
    bridge_budget = {"n": 0}

    def draw_road(pts, width, allow_river):
        for i in range(len(pts) - 1):
            a, b = pts[i], pts[i + 1]
            mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
            if not allow_river and in_river(mx, my):
                continue
            if in_river(mx, my) and bridge_budget["n"] > 99:
                continue
            dr.line([a, b], fill=255, width=width)

    for hx, hy in hubs:
        for k in range(rng_py.randint(3, 5)):
            ang = rng_py.uniform(0, 6.28)
            length = rng_py.uniform(0.15, 0.4) * w
            curve = rng_py.uniform(-0.6, 0.6)
            pts, x, y, a = [], hx, hy, ang
            for s in range(80):
                pts.append((x, y))
                a += curve * 0.012 + rng_py.uniform(-0.03, 0.03)
                x += math.cos(a) * length / 80
                y += math.sin(a) * length / 80
            # 一半的放射线允许过河（成桥），其余在河边断开
            draw_road(pts, int(SS * 4), allow_river=(k % 2 == 0))

        # 环线
        r = rng_py.uniform(0.07, 0.16) * h
        ring = [(hx + math.cos(t / 60 * 6.283) * r * (1 + 0.08 * math.sin(t * 0.7)),
                 hy + math.sin(t / 60 * 6.283) * r) for t in range(61)]
        draw_road(ring, int(SS * 2.6), allow_river=False)

    # 跨河桥：在河上固定位置补几条横桥
    for i in range(rng_py.randint(4, 6)):
        t = rng_py.uniform(0.1, 0.9)
        idx = int(t * (len(rpts) - 1))
        px, py = rpts[idx]
        nx, ny = rpts[min(idx + 1, len(rpts) - 1)]
        tx, ty = nx - px, ny - py
        L = math.hypot(tx, ty) or 1
        nx_, ny_ = -ty / L, tx / L
        span = river_w * 1.8
        dr.line([(px - nx_ * span, py - ny_ * span), (px + nx_ * span, py + ny_ * span)], fill=255, width=int(SS * 3))

    # 河岸滨河路
    edge = river.filter(ImageFilter.MaxFilter(int(SS * 2 + 1) * 2 + 1))
    edge_a = np.asarray(edge) > 0
    shore = Image.fromarray(((edge_a & ~river_a) * 255).astype(np.uint8))
    img = Image.composite(Image.new("L", (w, h), 255), img, shore.filter(ImageFilter.MinFilter(3)))

    # 河面保持纯黑（桥除外：桥已画在河面上，这里只清除非桥的河内碎线）
    out = img.resize((W, H), Image.LANCZOS)
    out.paste(0, (0, 0, W, 8)); out.paste(0, (0, H - 8, W, H)); out.paste(0, (0, 0, 8, H)); out.paste(0, (W - 8, 0, W, H))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--w", type=int, default=2400)
    ap.add_argument("--h", type=int, default=1500)
    ap.add_argument("--out", default="Assets/Screenshots/city_map_placeholder.png")
    a = ap.parse_args()
    img = build(a.seed, a.w, a.h)
    # 轻微把纯黑提成深灰，贴近参考图的 #2b2b2b 底
    arr = np.asarray(img).astype(np.float32) / 255.0
    out = (43 + arr * (255 - 43)).astype(np.uint8)
    Image.fromarray(out).save(a.out)
    print("saved", a.out, "seed", a.seed)


if __name__ == "__main__":
    main()
