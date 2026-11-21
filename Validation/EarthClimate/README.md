# 地球月气候观测验证

本目录用地球陆地的逐月气候常年值，评估游戏通过 `FlatGenerator.Generate` 生成的气温和降水。开发集与空间留出集分开存放。游戏加载资源、生成世界和读档均无需访问观测数据。

[本次模型与历史版本的对照结果](RESULTS.md)

## 复现

在仓库根目录运行。月常年值文件已经随验证工具提供。评分使用 Python 3.11 或更新版本和 NumPy，导出使用 .NET SDK，复现历史版本还需要 Git。评分 JSON 会记录导出地图的尺寸和 SHA-256。

```powershell
python Validation/EarthClimate/export_baseline.py
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- ValidationOutput/EarthClimate/current.bin
python Validation/EarthClimate/score.py ValidationOutput/EarthClimate/baseline.bin --json ValidationOutput/EarthClimate/baseline-calibration.json
python Validation/EarthClimate/score.py ValidationOutput/EarthClimate/current.bin --json ValidationOutput/EarthClimate/current-calibration.json
```

参数拟合完成后，使用固定留出区做最终比较。每次评分均可离线运行。

```powershell
python Validation/EarthClimate/score.py ValidationOutput/EarthClimate/baseline.bin --split holdout --json ValidationOutput/EarthClimate/baseline-holdout.json
python Validation/EarthClimate/score.py ValidationOutput/EarthClimate/current.bin --split holdout --check-improvement --json ValidationOutput/EarthClimate/current-holdout.json
```

默认地图为 256×128，种子为 `ORBIS-2D-2026`。当前导出器在输出文件后另接受地图宽度和种子，宽度需有对应地球资源。历史基线脚本从固定提交 `fcb360fd872b6a7621868006ac72e1cd00b0ee37` 提取当时的运行时代码和高程，放入临时目录构建。该提交原有的地球宽度为 256、512、1024。本机默认基线二进制的 SHA-256 为 `0e8b2e4bcdbf42cd8b62f9b7be6f1542e5a66e8e7d6f8614e769165435d50113`。

512 宽地图使用同一批留出区独立复核。`--check-improvement` 要求 1,806 个留出单元覆盖同一组 17 个地理块，逐月气温和降水的 MAE、RMSE 四项均有限，且均优于相同宽度的历史版本。这四项属于整体误差门槛，各月和各地区的表现可在 JSON 中查阅。

```powershell
python Validation/EarthClimate/export_baseline.py --width 512 --output ValidationOutput/EarthClimate/baseline-512.bin
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- ValidationOutput/EarthClimate/current-512.bin 512
python Validation/EarthClimate/score.py ValidationOutput/EarthClimate/baseline-512.bin --split holdout
python Validation/EarthClimate/score.py ValidationOutput/EarthClimate/current-512.bin --split holdout --check-improvement
```

开发集气温系数的复算使用 256 宽地图的地形与海陆几何。`fit_temperature.py` 读取开发集并输出年平均和年周期的拟合系数。

```powershell
python Validation/EarthClimate/fit_temperature.py ValidationOutput/EarthClimate/current.bin
```

## 数据、字段与许可

**Climatic Research Unit, University of East Anglia.** CRU-CL v2.0 使用气象站观测插值得到 1961–1990 年的逐月气候常年值，分辨率为 10 角分，覆盖南极洲以外的全球陆地。本文使用逐月平均气温（°C）、月累计降水的多年平均（mm/月）和同格网的地面高程（原单位 km）。[数据目录与使用条件](https://crudata.uea.ac.uk/cru/data/hrg/) · [源文件](https://crudata.uea.ac.uk/cru/data/hrg/tmc/) · [字段说明](https://crudata.uea.ac.uk/cru/data/hrg/tmc/readme.txt)

引用 New, M., Lister, D., Hulme, M. 和 Makin, I.（2002），*A high-resolution data set of surface climate over global land areas*，Climate Research 21，1–25。[论文](https://doi.org/10.3354/cr021001)

派生数据库 `calibration.csv.gz`、`holdout.csv.gz`、`land-mask.bin.gz` 继续按 Open Database License 1.0 提供，独立内容适用 Database Contents License 1.0。使用和再分发时保留上述机构署名及相应许可。[ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/) · [DbCL 1.0](https://opendatacommons.org/licenses/dbcl/1-0/)

| CSV 字段 | 含义 |
| --- | --- |
| `tile` | 256×128 参考网格从南向北的单元编号，等于 `y * 256 + x`。经度起点为西经 180°。 |
| `area_km2` | 原始 CRU 有效陆地覆盖该单元的球面面积，单位 km²。 |
| `elevation_m` | CRU 原始高程按有效陆地面积求平均，换算为 m。 |
| `t01` 至 `t12` | 1 月至 12 月的陆地平均温度，单位 °C。 |
| `p01` 至 `p12` | 1 月至 12 月各月累计降水量的多年平均，单位 mm/月。 |

`prepare.py` 对照从来源网站取样时记录的 SHA-256 校验三个文件，取气温、降水与高程均有效的 566,262 个细格。细格与游戏单元相交的部分按实际经纬边界计算球面面积，再合并成 256×128 参考网格。面积采用平均地球半径 6371.0088 km。10′ 细网格的覆盖范围另存为从南向北、每行 2,160 位、每字节高位在前的压缩位图。用下列命令可重新下载并生成全部验证数据。

```powershell
python Validation/EarthClimate/prepare.py
```

## 地理留出与评分范围

空间分组先按 22.5°×22.5° 划分。每个 90°×22.5° 扇区含四个相邻块，用扇区编号的固定 SHA-256 选择一个整块留出。该选择只用地理坐标。整个留出块的各月与各变量同时保留供最终评价。

| 分区 | 含观测陆地的单元 | 评分单元 | 评分所覆盖的观测陆地面积 |
| --- | ---: | ---: | ---: |
| 开发集 | 7,681 | 6,113 | 102.86 百万 km²，占本区 95.9% |
| 空间留出集 | 2,108 | 1,806 | 28.89 百万 km²，占本区 97.1% |

主评分采用观测陆地覆盖至少一半的参考单元，按每个单元实际覆盖的观测陆地面积加权。游戏把实测陆地标成海、湖或河，同一地点仍进入主评分，因此地形和水域的改变会反映在成绩中。较细游戏网格按 CRU 的实际陆地覆盖面积聚合，较粗的嵌套网格则沿用其所在游戏单元的月值。JSON 的 `common_land` 另列游戏中同时属于陆地或河道的单元成绩。

温度与降水分别计算有符号偏差（游戏减观测）、平均绝对误差 MAE、均方根误差 RMSE 和按面积加权的 Pearson 相关系数。12 个月各占相同权重。`by_month` 提供逐月误差，`seasonal` 先减去各单元自己的全年月平均，衡量季节变化。年降水量为 12 个月之和。年雨量小于 250 mm 及至少 1500 mm 的观测单元另有面积、召回率和年雨量误差统计。

游戏每月固定 30 天。降水比较采用游戏与观测各自的月累计量，公历月长的差异也会反映在月误差中。

`blocks` 记录每个有评分单元的 22.5° 地理块的月误差，便于检查总体变化是否集中在少数地区。开发集覆盖 57 个有效块，空间留出集覆盖 17 个。

区域表按 `score.py` 中的地理矩形统计，包括华北平原、长江中下游、华南、四川盆地和塔里木盆地。主指标覆盖整个对应分区。区域表给出年雨量、6–9 月降水占比，以及 6–8 月平均气温减去 12–2 月平均气温。一个分区中没有观测单元的区域不会出现在该分区的表中。

在仓库根目录运行下列命令，可用玩家菜单相同的高程、河湖资料和种子复现东亚的地表分类。

```powershell
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --east-asia-climate 2048
```

这个回归同时检查各地雨量、雨季集中度、柯本沙漠气候和实际沙漠地表。将宽度换为 256、512、1024 或 1536 可复核其他地图尺寸。`--environment` 另外用一个跨赤道的架空大陆检验两半球东岸的夏季向岸风与降水。

这些比较衡量陆地气候的多年平均空间分布与季节循环。开阔海洋、南极洲、面积小于半个参考单元的离岸岛屿、具体某天的天气及极端事件未纳入主评分。

## 柯本分类与海洋气温复核

下列命令使用 256 档导出文件，从仓库根目录运行。柯本对照直接调用游戏的 C# 分类器。海洋和南极近地气温对照额外依赖 h5py，首次运行从 NOAA PSL 下载两份公开的 NCEP/NCAR Reanalysis 1 数据（合计约 550 KiB），验证 SHA-256 后缓存在 `ValidationOutput/EarthClimate/sources`。温度常年值选用 1981–2010。该补充数据与 CRU 校准期分别计分。[NOAA PSL 资料与引用说明](https://psl.noaa.gov/data/gridded/data.ncep.reanalysis.html)

```powershell
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen ValidationOutput/EarthClimate/baseline.bin
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen ValidationOutput/EarthClimate/current.bin
python Validation/EarthClimate/check_marine_air.py ValidationOutput/EarthClimate/baseline.bin
python Validation/EarthClimate/check_marine_air.py ValidationOutput/EarthClimate/current.bin
```
