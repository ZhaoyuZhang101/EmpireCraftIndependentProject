"""Plot simulated and observed ocean temperature structure and the frozen circulation diagnostics."""
import argparse
import hashlib
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
from matplotlib import font_manager, pyplot as plt
import numpy as np

from check_ocean_currents import model_at_oisst, observed_sst
from check_wind_belts import read_export


def draw(export, wind_report, ocean_report, cache, output):
    wind, sea = [json.loads(path.read_text(encoding="utf-8")) for path in (wind_report, ocean_report)]
    digest = hashlib.sha256(export.read_bytes()).hexdigest()
    if wind["export_sha256"] != digest or sea["export_sha256"] != digest:
        raise ValueError("Use wind and ocean reports from the same OWB2 export")
    if wind["split"] != "ocean-holdout" or sea["open_ocean_zonal_anomaly"]["split"] != "ocean-holdout":
        raise ValueError("The direction chart requires the registered ocean holdout")
    ocean, _, currents = read_export(export)
    simulated, _, open_ocean = model_at_oisst(ocean, currents[0])
    observed = observed_sst(cache)
    valid = open_ocean & np.isfinite(observed)
    font = next((s for s in ("Microsoft YaHei", "Noto Sans CJK SC", "Microsoft JhengHei", "PingFang SC")
                 if s in font_manager.get_font_names()), None)
    if font:
        plt.rcParams["font.family"] = [font, "DejaVu Sans"]
    def label(zh, en):
        return zh if font else en

    fig = plt.figure(figsize=(13.6, 8.1), facecolor="white")
    grid = fig.add_gridspec(2, 2, left=.075, right=.96, top=.84, bottom=.12, hspace=.53, wspace=.22,
                            height_ratios=(1.35, 1))
    fig.text(.075, .975, label("地球风带与洋流", "Earth wind belts and ocean circulation"),
             va="top", fontsize=22, weight="bold")
    fig.text(.075, .91, label("同一纬度上各处海温比开阔海域平均值暖或冷多少？两幅图使用相同的海域和色阶。",
                              "Annual sea temperature relative to the open-ocean mean at each latitude, on a common mask and scale."),
             fontsize=11, color="#384852")
    edges_x = np.linspace(-np.pi, np.pi, 361)
    edges_y = np.linspace(-np.pi/2, np.pi/2, 181)
    for col, (field, title) in enumerate(((simulated, label("ORBIS 风生洋流", "ORBIS wind-driven circulation")),
                                          (observed, label("NOAA OISST 观测，1991–2020", "NOAA OISST, 1991–2020")))):
        ax = fig.add_subplot(grid[0, col], projection="mollweide")
        zonal_mean = np.where(valid, field, 0).sum(1) / np.maximum(1, valid.sum(1))
        anomaly = np.where(valid, field-zonal_mean[:, None], np.nan)
        image = ax.pcolormesh(edges_x, edges_y, np.roll(anomaly[::-1], 180, axis=1),
                              cmap="RdBu_r", vmin=-5, vmax=5, rasterized=True)
        ax.set_title(title, fontsize=13, pad=7)
        ax.set_facecolor("#dbdfde")
        ax.set_xticks([])
        ax.set_yticks([])
        ax.spines["geo"].set_color("#adb9bc")
    cax = fig.add_axes([.367, .49, .30, .014])
    bar = fig.colorbar(image, cax=cax, orientation="horizontal", extend="both", ticks=(-5, -2.5, 0, 2.5, 5))
    bar.ax.tick_params(labelsize=9, length=2)
    bar.set_label(label("相对同纬平均海温（°C）", "Difference from the latitude mean (°C)"), fontsize=10, labelpad=2)

    def bars(ax, title, ticks, before, after, first, second, unit):
        x = np.arange(len(ticks))
        for offset, values, color, name in ((-.19, before, "#a3b0b9", first), (.19, after, "#176a76", second)):
            b = ax.bar(x+offset, values, width=.34, color=color, label=name)
            ax.bar_label(b, fmt="%.1f", padding=2, fontsize=9)
        ax.set_title(title, loc="left", fontsize=12, pad=10)
        ax.set_xticks(x, ticks, fontsize=10)
        ax.set_ylabel(unit, fontsize=10)
        ax.set_ylim(0, max(max(before), max(after))*1.38)
        ax.spines[["top", "right"]].set_visible(False)
        ax.spines[["bottom", "left"]].set_color("#adb9bc")
        ax.tick_params(length=2)
        ax.set_axisbelow(True)
        ax.yaxis.grid(color="#e5e9ea", lw=.7)
        ax.legend(loc="upper left", ncol=2, frameon=False, fontsize=9)

    regions = [wind["by_absolute_latitude_deg"][key] for key in ("0-30", "30-60", "60-90")]
    bars(fig.add_subplot(grid[1, 0]), label("留出海域的月平均风向误差", "Monthly wind direction error on reserved oceans"),
         ("0–30°", "30–60°", "60–90°"), [r["previous_belts"]["direction_mae_deg"] for r in regions],
         [r["seasonal_belts"]["direction_mae_deg"] for r in regions], label("原风带", "Previous"),
         label("季节风带", "Seasonal"), label("角度（°）", "Degrees"))
    pairs = [sea["west_minus_east_annual_sst"][name] for name in ("North Atlantic", "North Pacific", "South Atlantic")]
    bars(fig.add_subplot(grid[1, 1]), label("同纬度西侧与东侧洋流的年均海温差", "Annual west minus east temperature contrast"),
         [label("北大西洋", "North Atlantic"), label("北太平洋", "North Pacific"), label("南大西洋", "South Atlantic")],
         [p["observed_difference_c"] for p in pairs], [p["simulated_difference_c"] for p in pairs],
         "NOAA OISST", "ORBIS", "°C")
    fig.text(.075, .041, label("灰色区域为陆地及海岸取样空缺。风向来自 NOAA NCEP/NCAR R1 的 10 米风，海温来自 NOAA OISST v2。",
                               "Gray marks land and excluded coastal samples. Wind reference: NOAA NCEP/NCAR R1 at 10 m. SST: NOAA OISST v2."),
             fontsize=9, color="#52616a")
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, dpi=145)
    plt.close(fig)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("export", "wind_report", "ocean_report", "output"):
        parser.add_argument(name, type=Path)
    parser.add_argument("--cache", type=Path, default=Path("ValidationOutput/EarthClimate/sources"))
    args = parser.parse_args()
    draw(args.export, args.wind_report, args.ocean_report, args.cache, args.output)
