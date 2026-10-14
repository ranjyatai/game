"""用 OpenStreetMap 真实路网渲染黑底白线城市图，作为 RAID 选本地图底图。
用法：python Tools/gen_osm_city_map.py tokyo|manhattan|chicago [--span 9000] [--tier 2] [--w 2400] [--h 1500]
图的正中心 = 塔的位置（城市中心点）。span = 图的横向宽度（米）。
tier: 1=只留高速/主干  2=加次干道(默认)  3=加支路(更密)
"""
import argparse

import osmnx as ox
from PIL import Image, ImageDraw, ImageFilter

CITIES = {
    "tokyo": (35.6852, 139.7528),       # 皇居
    "manhattan": (40.7580, -73.9855),   # 时代广场
    "chicago": (41.8781, -87.6298),     # Loop
    "tokyo_kanda": (35.6984, 139.7731), # 秋叶原/神田，老城密集区，中心无大块空地
}

TIERS = {
    1: ["motorway", "trunk", "primary"],
    2: ["motorway", "trunk", "primary", "secondary"],
    3: ["motorway", "trunk", "primary", "secondary", "tertiary"],
    4: ["motorway", "trunk", "primary", "secondary", "tertiary", "unclassified", "residential", "living_street"],
}
# 每个等级的线宽（占图宽的比例 * 2400 后取整）
WIDTH = {"motorway": 8, "trunk": 7, "primary": 6, "secondary": 4, "tertiary": 3, "unclassified": 1.4, "residential": 1.4, "living_street": 1.4}


import os
ox.settings.overpass_url = os.environ.get("OVERPASS", "https://overpass-api.de/api")
ox.settings.requests_timeout = 600


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("city")
    ap.add_argument("--span", type=float, default=9000)
    ap.add_argument("--tier", type=int, default=2)
    ap.add_argument("--w", type=int, default=2400)
    ap.add_argument("--h", type=int, default=1500)
    ap.add_argument("--out", default=None)
    ap.add_argument("--tiles", type=int, default=3, help="分块数 NxN")
    ap.add_argument("--lw", type=float, default=1.0, help="线宽倍率")
    a = ap.parse_args()
    lat, lon = CITIES[a.city]
    hw = a.span / 2
    hh = hw * a.h / a.w
    flt = '["highway"~"' + "|".join(TIERS[a.tier]) + '"]'
    import time
    import geopandas as gpd
    import pandas as pd
    from shapely.geometry import Point
    # 固定用中心点所在 UTM 带作为统一坐标系，分块结果才能拼起来
    crs = ox.projection.project_gdf(gpd.GeoDataFrame(geometry=[Point(lon, lat)], crs="EPSG:4326")).crs
    c = gpd.GeoSeries([Point(lon, lat)], crs="EPSG:4326").to_crs(crs).iloc[0]
    n = a.tiles
    tile_w, tile_h = a.span * 1.04 / n, (a.span * a.h / a.w) * 1.04 / n
    half = max(tile_w, tile_h) / 2
    parts = []
    for iy in range(n):
        for ix in range(n):
            ox_m = (ix - (n - 1) / 2) * a.span / n
            oy_m = (iy - (n - 1) / 2) * (a.span * a.h / a.w) / n
            ctr = gpd.GeoSeries([Point(c.x + ox_m, c.y + oy_m)], crs=crs).to_crs("EPSG:4326").iloc[0]
            for attempt in range(4):
                try:
                    Gt = ox.graph_from_point((ctr.y, ctr.x), dist=int(half), custom_filter=flt, simplify=True, retain_all=True)
                    Gt = ox.project_graph(Gt, to_crs=crs)
                    parts.append(ox.graph_to_gdfs(Gt, nodes=False))
                    print(f"tile {ix},{iy} ok", flush=True)
                    break
                except Exception as e:
                    print(f"tile {ix},{iy} retry {attempt}: {str(e)[:80]}", flush=True)
                    time.sleep(5)
            else:
                print(f"tile {ix},{iy} FAILED", flush=True)
    edges = pd.concat(parts)
    G = type("G", (), {"graph": {"crs": crs}})

    SS = 2
    W, H = a.w * SS, a.h * SS
    img = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(img)
    sx = W / a.span
    order = ["living_street", "residential", "unclassified", "tertiary", "secondary", "primary", "trunk", "motorway"]
    rank = {k: i for i, k in enumerate(order)}

    def tier_of(h):
        if isinstance(h, list):
            h = max(h, key=lambda k: rank.get(k, -1))
        return h

    rows = [(tier_of(r.highway), r.geometry) for r in edges.itertuples()]
    rows = [(t, g) for t, g in rows if t in rank]
    rows.sort(key=lambda x: rank[x[0]])
    for t, g in rows:
        pts = [((x - c.x) * sx + W / 2, H / 2 - (y - c.y) * sx) for x, y in g.coords]
        w = max(1, round(WIDTH[t] * a.lw * SS * a.w / 2400))
        d.line(pts, fill=255, width=w, joint="curve")
    # 水面挖黑（河/海），桥不处理：只把水面内的道路擦掉，保持河道干净连贯
    try:
        wt = ox.features_from_point((lat, lon), tags={"natural": "water", "waterway": "riverbank"}, dist=int(hw * 1.15))
        wt = wt.to_crs(G.graph["crs"])
        for geom in wt.geometry:
            polys = [geom] if geom.geom_type == "Polygon" else list(getattr(geom, "geoms", []))
            for pg in polys:
                if pg.geom_type != "Polygon" or pg.area < 20000:
                    continue
                d.polygon([((x - c.x) * sx + W / 2, H / 2 - (y - c.y) * sx) for x, y in pg.exterior.coords], fill=0)
    except Exception as e:
        print("water skipped:", e)
    img = img.resize((a.w, a.h), Image.LANCZOS)
    out = img.point(lambda v: 43 + int(v * (255 - 43) / 255))
    path = a.out or f"Assets/Screenshots/city_{a.city}.png"
    out.save(path)
    print("saved", path, "roads", len(rows))


if __name__ == "__main__":
    main()
