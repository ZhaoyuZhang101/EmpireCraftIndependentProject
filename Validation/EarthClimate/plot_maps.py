"""Map CRU and two ORBIS Earth exports on the same observed land footprint."""
import argparse
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
from matplotlib import colors, font_manager, pyplot as plt
import numpy as np

from score import HEIGHT, WIDTH, collocate, observation, tile_area


def draw(before, after, output):
    ids, area, observed_t, observed_p = observation("all")
    keep = area >= tile_area(HEIGHT)[ids // WIDTH] * .5
    ids = ids[keep]
    fields = [(observed_t[keep].mean(1), observed_p[keep].sum(1))]
    for path in (before, after):
        _, temp, rain = collocate(path)
        fields.append((temp[ids].mean(1), rain[ids].sum(1)))

    available = set(font_manager.get_font_names())
    font = next((s for s in ("Microsoft YaHei", "Noto Sans CJK SC", "Microsoft JhengHei",
                             "PingFang SC") if s in available), None)
    if font:
        plt.rcParams["font.family"] = [font, "DejaVu Sans"]
    def label(zh, en):
        return zh if font else en

    fig = plt.figure(figsize=(13.5, 6.1), facecolor="white")
    grid = fig.add_gridspec(2, 4, width_ratios=(1, 1, 1, .034), left=.025, right=.95,
                            top=.855, bottom=.085, wspace=.09, hspace=.21)
    titles = (label("CRU 观测常年值", "CRU normals"), label("调整前", "Before"), label("调整后", "After"))
    fig.text(.025, .985, label("地球陆地气候", "Earth land climate"), fontsize=20, weight="bold", va="top")
    fig.text(.025, .911, label("全球 256 × 128 格  ·  观测期 1961–1990",
                               "Matched land cells  ·  256 × 128 grid  ·  Reference period 1961–1990"),
             fontsize=11, color="#43545c")
    lon = np.linspace(-np.pi, np.pi, WIDTH+1)
    lat = np.linspace(-np.pi/2, np.pi/2, HEIGHT+1)
    rain_cmap = colors.ListedColormap(["#c08c5b","#d5b483","#e5d7ad","#c5d6bd",
                                      "#8abeb2","#4a9d9b","#2a7486","#234c72","#293650"])
    for row, (cmap, levels, unit) in enumerate((
            (plt.get_cmap("RdYlBu_r", 10), np.arange(-20, 31, 5), "°C"),
            (rain_cmap, [0,100,250,500,750,1000,1500,2000,3000,5000], label("mm/年","mm/yr")))):
        for col, (t, p) in enumerate(fields):
            ax = fig.add_subplot(grid[row, col], projection="mollweide")
            values = np.full(WIDTH*HEIGHT, np.nan)
            values[ids] = t if row == 0 else p
            image = ax.pcolormesh(lon, lat, values.reshape(HEIGHT, WIDTH),
                                  cmap=cmap, norm=colors.BoundaryNorm(levels, cmap.N, clip=True),
                                  rasterized=True, shading="flat")
            ax.set_facecolor("#edf1f3")
            ax.set_xticks([]); ax.set_yticks([])
            ax.spines["geo"].set_color("#b4bfc5")
            ax.spines["geo"].set_linewidth(.7)
            if row == 0:
                ax.set_title(titles[col], fontsize=12, pad=6)
        bar = fig.colorbar(image, cax=fig.add_subplot(grid[row, 3]), ticks=levels)
        bar.ax.tick_params(labelsize=8, length=2)
        bar.ax.set_title(unit, fontsize=9, loc="left", pad=6)
    fig.text(.025, .025, label("上排为年均气温，下排为年降水量。灰色区域缺少纳入评分的陆地观测。数据来自 UEA CRU-CL v2.0。",
                               "Top: annual mean air temperature. Bottom: annual precipitation. Grey areas are outside the CRU land mask. Source: UEA CRU-CL v2.0."),
             fontsize=9, color="#43545c")
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, dpi=150)
    plt.close(fig)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    draw(args.before, args.after, args.output)
