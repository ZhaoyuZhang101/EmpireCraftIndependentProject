"""Render four regional seasonal cycles from two score.py JSON reports for the same footprint."""

import argparse
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
from matplotlib import font_manager, pyplot as plt


def draw(before, after, output):
    if before["source"] != after["source"] or before["split"] != after["split"]:
        raise ValueError("Choose reports computed against the same source and split.")
    available = set(font_manager.get_font_names())
    cjk = next((name for name in ("Microsoft YaHei", "Noto Sans CJK SC", "Microsoft JhengHei",
                                   "PingFang SC", "WenQuanYi Zen Hei") if name in available), None)
    if cjk:
        plt.rcParams["font.family"] = [cjk, "DejaVu Sans"]
    def label(chinese, english):
        return chinese if cjk else english

    sites = [
        ("Eastern Siberia", "西伯利亚东部", "temperature_c"),
        ("Indian monsoon", "印度季风区", "precipitation_mm"),
        ("Amazon lowlands", "亚马孙低地", "precipitation_mm"),
        ("Sahara interior", "撒哈拉腹地", "precipitation_mm"),
    ]
    fig, axes = plt.subplots(2, 2, figsize=(10, 6.5))
    fig.subplots_adjust(left=0.092, right=0.98, bottom=0.14, top=0.81, hspace=0.46, wspace=0.24)
    fig.suptitle(label("四个地区的气温与降水季节循环", "Seasonal climate in four land regions"),
                 y=0.97, fontsize=18, fontweight="bold")
    months = range(1, 13)
    for ax, (name, chinese, field) in zip(axes.flat, sites):
        if name not in before["regions"] or name not in after["regions"]:
            raise ValueError(f"Missing region {name!r} in split {after['split']!r}; use --split all.")
        old, new = before["regions"][name], after["regions"][name]
        observed = old["observed"][field]
        if observed != new["observed"][field] or old["cells"] != new["cells"]:
            raise ValueError(f"Regional coverage differs for {name}.")
        ax.plot(months, observed, color="#182b38", lw=2.25, marker="o", markersize=3.8,
                label=label("CRU 观测常年值", "CRU normals"), zorder=3)
        ax.plot(months, old["modeled"][field], color="#b16831", lw=1.8, linestyle="--",
                label=label("原模型", "Original model"))
        ax.plot(months, new["modeled"][field], color="#007687", lw=2.3, marker="s", markersize=3.3,
                markerfacecolor="white", label=label("调整后模型", "Revised model"))
        ax.set_title(label(chinese, name), loc="left", fontweight="bold", fontsize=12)
        if field == "temperature_c":
            ax.set_ylabel(label("平均气温（°C）", "Mean temperature (°C)"))
        else:
            ax.set_ylabel(label("降水量（mm/月）", "Precipitation (mm/month)"))
            ax.set_ylim(bottom=0)
        ax.set_xlim(0.72, 12.28)
        ax.set_xticks(list(months))
        ax.tick_params(length=0, pad=4, colors="#405260", labelsize=8.5)
        ax.grid(axis="y", color="#d6dce0", lw=0.7)
        ax.set_axisbelow(True)
        ax.spines[["top", "right"]].set_visible(False)
        for direction in ("left", "bottom"):
            ax.spines[direction].set_color("#b5c1c9")
    for ax in axes[-1]:
        ax.set_xlabel(label("月份", "Month"))
    handles, labels = axes[0, 0].get_legend_handles_labels()
    fig.legend(handles, labels, loc="upper center", bbox_to_anchor=(0.5, 0.91),
               ncol=3, frameon=False, handlelength=2.6, columnspacing=2)
    caption = label("CRU-CL v2.0，1961–1990 年。各区域按观测陆地面积加权，降水面板分别标度。",
                    "CRU-CL v2.0, 1961–1990. Means use observed land area. Rain panels have different scales.")
    fig.text(0.092, 0.047, caption, fontsize=9, color="#51616a")
    fig.text(0.092, 0.019, "Climatic Research Unit, University of East Anglia  ·  ODbL 1.0 / DbCL 1.0  ·  DOI 10.3354/cr021001",
             fontsize=8.5, color="#51616a")
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, dpi=165, facecolor="white")
    plt.close(fig)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    draw(json.loads(args.before.read_text(encoding="utf-8")), json.loads(args.after.read_text(encoding="utf-8")), args.output)
