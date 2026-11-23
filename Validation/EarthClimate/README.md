# 地球月气候观测验证

本目录用地球陆地的逐月气候常年值，评估游戏通过 `FlatGenerator.Generate` 生成的气温和降水。开发集与空间留出集分开存放。游戏加载资源、生成世界和读档均无需访问观测数据。

[本次模型与历史版本的对照结果](RESULTS.md)

[希腊、地中海与欧洲的逐月降水、30 年站点模拟与爱琴海风向](EUROPE.md)

## 季风与全球留出验收

以下命令从玩家生成入口导出当前 2048 地球，并从 Git 版本 `8d311ab` 导出当时的地形、水系及模拟作为对照。将宽度依次换成 256、512、1024、1536、2048、4096 可复现六档成绩。Python 评分依赖 NumPy。[验证结果](RESULTS.md#印度夏季风群岛冬雨与全球留出验证)

```powershell
$out = "ValidationOutput/EarthMonsoonCheck"
$width = 2048
$export = "Validation/EarthClimate/Export.csproj"
python Validation/EarthClimate/export_baseline.py --before-subcloud --width $width --output "$out/before-$width.bin"
dotnet run --project $export -c Release -- "$out/after-$width.bin" $width ORBIS-2D-2026
foreach ($stage in "before","after") {
    $map = "$out/$stage-$width.bin"
    dotnet run --project $export -c Release -- --compare-koppen $map --split holdout --json "$out/$stage-$width-holdout.json"
    if ($LASTEXITCODE -gt 1) { throw "Invalid comparison for $map" }
    python Validation/EarthClimate/score.py $map --split calibration --json "$out/$stage-$width-regions.json"
    python Validation/EarthClimate/score.py $map --split holdout --json "$out/$stage-$width-fields.json"
}
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --moisture-flux "$out/transport.omf" 256
python Validation/EarthClimate/check_monsoon_flux.py "$out/transport.omf" --cache "$out/noaa" --json "$out/transport.json"
```

水汽通量取每六小时步进的网格面通量，含季风低压与环境输送，单位为 kg/(m·s)。离岸对照用 NCEP/NCAR R1 的 1991–2020 年逐月湿度和风场，积分范围为 1000–300 hPa。逐月平均量的乘积没有包含瞬变涡动的协方差，因此此项属于输送方向和量级的诊断。[NOAA 再分析](https://psl.noaa.gov/data/gridded/data.ncep.reanalysis.derived.html)

## 全球水汽输送、地形降水与陆面云辐射

对照版本为 `4aa93fc340c6028f934ae78d43ec38176a379d57`。用相同的地球高程、水系、种子和玩家生成入口导出两版逐月场。柯本评分采用原有 17 个留出块的 1,806 个陆地参考格，按观测面积加权。六档分别评分。开发区有 57 个块，适合检查具体地区和调试机制。[结果](RESULTS.md#全球水汽输送地形降水与陆面云辐射)

```powershell
$out = "ValidationOutput/GlobalClimatePhysics"
$export = "Validation/EarthClimate/Export.csproj"
$tests = "Validation/FlatWorld/FlatWorld.Core.Tests.csproj"
foreach ($width in 256,512,1024,1536,2048,4096) {
    python Validation/EarthClimate/export_baseline.py --before-surface-climate --width $width --output "$out/control-$width.bin"
    dotnet run --project $export -c Release -- "$out/final-$width.bin" $width ORBIS-2D-2026
    foreach ($stage in "control","final") {
        $map = "$out/$stage-$width.bin"
        dotnet run --project $export -c Release -- --compare-koppen $map --split holdout --json "$out/$stage-$width-holdout.json"
        if ($LASTEXITCODE -gt 1) { throw "Invalid comparison for $map" }
        python Validation/EarthClimate/score.py $map --split holdout --json "$out/$stage-$width-field-holdout.json"
    }
}
dotnet run --project $tests -c Release -- --orographic-rainfall
dotnet run --project $tests -c Release -- --seasonal-rainfall
dotnet run --project $tests -c Release -- --seasonal-temperature
dotnet run --project $tests -c Release -- --environment
python -m unittest discover -s Validation/EarthClimate -p test_koppen.py
```

柯本命令将两项一致率和各自的通过标记写入 JSON。退出码 1 表示至少一项未严格超过 90%，2 表示输入无效。`largest_confusions` 按错分面积列出前十个「观测 → 模拟」组合，分母是整个评分区域。`rainfall_attribution` 在相同月温下分别替换全年雨量或逐月雨量占比。只有双方全年雨量均为正的格子才有可比较的月占比，报告给出配对格数及面积。纯模拟门槛始终使用原始评分范围。

陆面云辐射系数在 256 档的开发区拟合。`--surface-fit-cloud` 导出拟合时冻结的 64×32 预测场。`--surface-cloud` 导出玩家使用的 128×64 预测场，与最终水汽网格对齐。空间交叉验证在每个训练折重新拟合背景温度，并从该折自己的温度重新计算两种云场。独立重建需要 NumPy 和 h5py。

```powershell
python Validation/EarthClimate/export_baseline.py --before-seasons --width 256 --output "$out/preseason.bin"
python Validation/EarthClimate/export_baseline.py --before-bohai --width 256 --output "$out/preplateau.bin"
dotnet run --project $tests -c Release -- --wind-belts "$out/winds.bin"
dotnet run --project $tests -c Release -- --surface-fit-cloud "$out/fit.orc" 256
dotnet run --project $tests -c Release -- --surface-cloud "$out/player.orc" 256
python Validation/EarthClimate/fit_temperature.py "$out/preseason.bin" --plateau-baseline "$out/preplateau.bin" --surface-control "$out/control-256.bin" --ocean-air "$out/winds.bin" --surface-cloud "$out/fit.orc" --check-cloud "$out/player.orc" --check-runtime "$out/final-256.bin" --cloud-folds "$out/spatial-folds"
```

Unity 端核对使用同一版本工程的独立副本。把本目录的 `EarthClimateMenuRegression.cs` 复制到副本的 `Assets/ORBIS/Editor`。用 Unity 6000.0.55f1 执行下面的命令，方法会从玩家菜单生成指定尺寸的地球，核对 8,192 个地表格的逐月气候和实际分类，写入日志后退出。以下示例中的 `$qa` 为工程副本的绝对路径。

```powershell
$reference = (Resolve-Path "$out/final-2048.bin").Path
$log = Join-Path (Resolve-Path $out).Path "menu-final.log"
$argsForUnity = @("-batchmode","-force-d3d11","-projectPath",('"{0}"' -f $qa),"-executeMethod","EarthClimateMenuRegression.Run","-earthClimateReference",('"{0}"' -f $reference),"-logFile",('"{0}"' -f $log))
$run = Start-Process -FilePath "D:/Program Files/Unity 6000.0.55f1/Editor/Unity.exe" -ArgumentList $argsForUnity -WindowStyle Hidden -PassThru -Wait
if ($run.ExitCode -ne 0) { throw "See $log" }
```

## 河北至辽宁的沿海夏季风

以 `e560df5` 为对照。先对同一张玩家地球的逐月气温、降水做面积匹配，再把这两组月值分别送入游戏的柯本分类器。渤海样区取 116–125°E、38–43°N，西、南边界包含在内。21 个合格的 CRU 陆地格均属开发块 93。相邻的内蒙古东南部和辽东也列在逐月报告的 `regions` 中。[结果与玩家画面对照](RESULTS.md#河北至辽宁的沿海夏季风)

```powershell
$out = "ValidationOutput/BohaiClimate"
$width = 2048
python Validation/EarthClimate/export_baseline.py --before-bohai --width $width --output "$out/before-$width.bin"
python Validation/EarthClimate/export_baseline.py --before-surface-climate --width $width --output "$out/after-$width.bin"
foreach ($stage in "before", "after") {
    $file = "$out/$stage-$width.bin"
    python Validation/EarthClimate/score.py $file --split calibration --json "$out/$stage-$width-field-calibration.json"
    dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen $file --split calibration --window 116 125 38 43 --json "$out/$stage-$width-bohai.json"
    dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen $file --json "$out/$stage-$width-holdout.json"
}
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --east-asia-climate $width
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --seasonal-rainfall
```

`--window west east south north` 按参考格中心选取递增的经纬度范围，只能用于开发区诊断。全球验收仍使用已登记的完整留出区。六档地图采用同一套评分命令。两项全球一致率中任一项未超过 90%，默认留出评分会写出报告并返回退出码 1。

温度校准分两步。原来的热力参数使用季节模型引入前的地形拟合。新增的暖季高地下风向增温系数使用本次修改前的月温残差拟合。下面的命令在 256 档重建 C# 运行时月温，需安装 NumPy 和 h5py。

```powershell
python Validation/EarthClimate/export_baseline.py --before-seasons --width 256 --output "$out/before-seasons-256.bin"
python Validation/EarthClimate/export_baseline.py --before-bohai --width 256 --output "$out/before-256.bin"
python Validation/EarthClimate/export_baseline.py --before-surface-climate --width 256 --output "$out/after-256.bin"
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --wind-belts "$out/after-winds.bin"
python Validation/EarthClimate/fit_temperature.py "$out/before-seasons-256.bin" --plateau-baseline "$out/before-256.bin" --check-runtime "$out/after-256.bin" --ocean-air "$out/after-winds.bin"
```

## 风带、洋流与陆地柯本验收

以 `2d05a93` 为本轮的完整历史对照。常规导出直接执行和玩家相同的 `FlatGenerator.Generate`。下面用同一地形、海陆、河湖和种子复现 256 档留出分数。把宽度改为 512、1024、1536、2048 或 4096 可验证其余地图。默认留出命令在任一指标未超过 90% 时写出 JSON 并返回 1。

```powershell
$out = "ValidationOutput/CirculationClimate"
python Validation/EarthClimate/export_baseline.py --before-circulation --width 256 --output "$out/baseline-256.bin"
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- "$out/spherical-256.bin" 256
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen "$out/baseline-256.bin" --json "$out/baseline-256-holdout.json"
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen "$out/spherical-256.bin" --json "$out/spherical-256-holdout.json"
python Validation/EarthClimate/score.py "$out/spherical-256.bin" --split holdout --json "$out/spherical-256-field.json"
```

评分器的 `with_observed_temperature` 和 `with_observed_precipitation` 在离线诊断中分别替换一个月值序列，衡量剩余偏差来自哪一项。正式两项一致率始终使用生成器自己的月温和月雨。

以下四组对照固定地形和其他降水物理。`control` 使用原大气输送风并关闭风生海温异常，`still-ocean` 启用季节风带及其球面输送，`fixed-winds` 仅启用风生洋流的热力作用，`coupled` 同时启用两者。两组开启洋流的试验使用相同的年平均海洋风应力。风带试验改变大气输送，海洋试验改变沿岸气温和蒸发。完整旧版由上面的历史导出命令复现。

```powershell
foreach ($forcing in "control","still-ocean","fixed-winds","coupled") {
    dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --circulation $forcing "$out/$forcing.bin" 256
    dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen "$out/$forcing.bin" --json "$out/$forcing-holdout.json"
}
```

下面的诊断导出包含 128×64 的全海格掩膜、两套逐月地面风，以及年均海温异常、海洋输送的近地气温异常、东西和南北向海流。海温异常单位为 °C，海流单位为 m/s。模拟风是归一化分量。验证风向时，以 NOAA NCEP/NCAR R1 的 1991–2020 年 10 米风为参照，在高斯格点上按面积求平均。参考风速达到 1 m/s 才计风向，计算分量正负时要求相应观测分量绝对值达到 1 m/s。[NOAA NCEP/NCAR R1](https://psl.noaa.gov/data/gridded/data.ncep.reanalysis.html)

海温对照使用 NOAA OISST v2 的 1991–2020 年逐月常年值，12 个月等权。开阔海域评分要求周围四个模拟格均为全海格，每一纬线上从观测与模拟各自减去同一组有效经度的平均值。东西岸对照用海盆两侧匹配的纬线计算温差。风、海温与浮标检验共用地理留出规则，`ocean-holdout` 由原坐标哈希选出全部 32 个预留块。开发区检查使用含合格 CRU 陆地样本的 57 个开发块内的海域。下载文件按脚本登记的 SHA-256 验证，依赖 NumPy、h5py 和 rasterio，绘图另用 Matplotlib。核心测试以 .NET 8 为目标，本次 .NET 9 环境用 `$env:DOTNET_ROLL_FORWARD="Major"` 执行。[NOAA OISST v2](https://psl.noaa.gov/data/gridded/data.noaa.oisst.v2.html)

浮标检验使用 NOAA/AOML 年均近表层流速。在原始 0.25° 网格上每四点取一点，先将有采样记录的东西和南北分量按面积平均到模拟格，再计算流向和矢量误差。每个全海格至少需要四个有效样点，有效样点覆盖至少一半的取样面积，且整行处于观测纬度范围内。观测平均速度达到 0.05 m/s 才计入流向，模拟静止按 90° 计。静海矢量误差和模拟矢量误差都使用全部合格海格。[NOAA 漂流浮标年均流速](https://erddap.aoml.noaa.gov/gdp/erddap/info/drifter_annualmeans/index.html) · [Laurindo 等，2017](https://doi.org/10.1016/j.dsr.2017.04.009)

```powershell
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --wind-belts "$out/spherical-winds.bin"
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --ocean-circulation
python Validation/EarthClimate/check_wind_belts.py "$out/spherical-winds.bin" --split ocean-holdout --json "$out/spherical-wind-holdout.json"
python Validation/EarthClimate/check_ocean_currents.py "$out/spherical-winds.bin" --split ocean-holdout --json "$out/spherical-ocean-holdout.json"
python Validation/EarthClimate/check_drifter_currents.py "$out/spherical-winds.bin" --split ocean-holdout --json "$out/spherical-drifter-ocean-holdout.json"
python Validation/EarthClimate/plot_circulation.py "$out/spherical-winds.bin" "$out/spherical-wind-holdout.json" "$out/spherical-ocean-holdout.json" "$out/seasonal-circulation.png"
```

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

默认地图为 256×128，种子为 `ORBIS-2D-2026`。当前导出器与玩家入口使用相同的地球高程、海湖和河道资源，在输出文件后另接受地图宽度和种子。历史基线脚本从固定提交 `fcb360fd872b6a7621868006ac72e1cd00b0ee37` 提取当时的运行时代码和高程，放入临时目录构建。该提交原有的地球宽度为 256、512、1024。本机默认基线二进制的 SHA-256 为 `0e8b2e4bcdbf42cd8b62f9b7be6f1542e5a66e8e7d6f8614e769165435d50113`。

512 宽地图使用同一批留出区独立复核。`--check-improvement` 要求 1,806 个留出单元覆盖同一组 17 个地理块，逐月气温和降水的 MAE、RMSE 四项均有限，且均优于相同宽度的历史版本。这四项属于整体误差门槛，各月和各地区的表现可在 JSON 中查阅。

```powershell
python Validation/EarthClimate/export_baseline.py --width 512 --output ValidationOutput/EarthClimate/baseline-512.bin
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- ValidationOutput/EarthClimate/current-512.bin 512
python Validation/EarthClimate/score.py ValidationOutput/EarthClimate/baseline-512.bin --split holdout
python Validation/EarthClimate/score.py ValidationOutput/EarthClimate/current-512.bin --split holdout --check-improvement
```

本轮季节模拟的直接对照版本是 `06bfd50`。下列命令用该提交的地形、海湖、河网及运行时代码生成对照，再给当前地球评分。种子为 `ORBIS-2D-2026`。把宽度改为 512、1024、1536 或 2048 可复核其他玩家地图尺寸。

```powershell
python Validation/EarthClimate/export_baseline.py --before-seasons --width 256 --output ValidationOutput/EarthClimate/before-seasons.bin
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- ValidationOutput/EarthClimate/current.bin 256
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen ValidationOutput/EarthClimate/before-seasons.bin
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen ValidationOutput/EarthClimate/current.bin --json ValidationOutput/EarthClimate/koppen-current.json
```

印度、中国季风修正的直接对照为 `1bee7ae`。下列命令均在仓库根目录运行。地球宽度支持 256、512、1024、1536、2048 和 4096。将最后两条评分命令中的输入路径换为对照文件，可复现同一批地理单元的修正前指标。区域报表的 `--split all` 同时列出两组数据中的所有样区，柯本验收则始终使用固定留出集。

```powershell
python Validation/EarthClimate/export_baseline.py --before-monsoon --width 256 --output ValidationOutput/EarthClimate/before-monsoon.bin
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- ValidationOutput/EarthClimate/monsoon.bin 256
python Validation/EarthClimate/score.py ValidationOutput/EarthClimate/monsoon.bin --split all --json ValidationOutput/EarthClimate/monsoon-regions.json
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen ValidationOutput/EarthClimate/monsoon.bin --json ValidationOutput/EarthClimate/monsoon-koppen.json
```

温度系数复算使用 256 档地形、海陆几何和 57 个开发区地理块。五折交叉验证在每个训练折分别拟合背景项及 16 个热力系数。脚本还可根据模拟地形独立重建逐月温度，并与 C# 导出逐格核对。ECL1 没有保存湖面高程，这项数值核对会明确列出有足够输入的非湖泊单元数。

```powershell
python Validation/EarthClimate/fit_temperature.py ValidationOutput/EarthClimate/before-seasons.bin --check-runtime ValidationOutput/CirculationClimate/spherical-256.bin --ocean-air ValidationOutput/CirculationClimate/spherical-winds.bin
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

区域表按 `score.py` 中的地理矩形统计，包括印度季风带、西高止山脉与德干、塔尔、恒河下游、阿萨姆，以及中国东部平原、四川、青藏高原、东北和塔里木盆地。主指标覆盖整个对应分区。区域表给出逐月气温与雨量、年雨量、6–9 月降水占比，以及 6–8 月平均气温减去 12–2 月平均气温。一个分区中没有观测单元的区域不会出现在该分区的表中。

在仓库根目录运行下列命令，可用玩家菜单相同的高程、河湖资料和种子复现东亚的地表分类。

```powershell
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --east-asia-climate 2048
```

这个回归同时检查各地雨量、雨季集中度、柯本沙漠气候和实际沙漠地表。将宽度换为 256、512、1024 或 1536 可复核其他地图尺寸。`--environment` 另外用一个跨赤道的架空大陆检验两半球东岸的夏季向岸风与降水。

这些比较衡量陆地气候的多年平均空间分布与季节循环。开阔海洋、南极洲、面积小于半个参考单元的离岸岛屿、具体某天的天气及极端事件未纳入主评分。

## 柯本分类与季节循环验证

在仓库根目录运行。评分器将模拟和 CRU 的 12 个月常年值分别送入游戏的 C# 柯本分类器，按观测陆地面积计算五大类一致率和 30 个精确亚型一致率。256×128 导出可直接评分。其他嵌套网格通过 Python 和 NumPy 使用本目录相同的陆地覆盖面积映射。

```powershell
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen ValidationOutput/EarthClimate/current.bin --split calibration --json ValidationOutput/EarthClimate/current-koppen-calibration.json
```

开发期间用 `--split calibration` 只读取开发集，可附加 `--check-target` 检查同一门槛。模型参数冻结后运行默认的空间留出评分。它校验 1,806 个单元、17 个地理块和所有单元编号的 SHA-256，要求**两项未舍入的一致率均严格大于 90%**。任一项小于或等于 90% 时，仍输出报告并返回退出码 `1`。通过时返回 `0`，输入、月值或评分范围无效时返回 `2`。

```powershell
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- --compare-koppen ValidationOutput/EarthClimate/current.bin --json ValidationOutput/EarthClimate/current-koppen-holdout.json
```

JSON 中的 `observed_subtypes` 列出 30 个观测亚型。每类的 `area_km2` 是该类的观测陆地面积，`recall` 是其中被模拟精确判对的面积比例。该分区观测面积为零时，召回率为 `null`。报告同时记录导出文件和评分单元编号的 SHA-256。

`hemispheres` 分南北半球列出四季的平均气温 MAE（°C）和累计降水 MAE（mm/季）。北半球的夏、冬为 6–8 月和 12–2 月，南半球的夏、冬为 12–2 月和 6–8 月。每格再根据观测选出最湿和最干的连续三个月，在相同月份计算模拟雨量误差，可跨年，有并列窗口时取平均。月循环 RMSE 先减去每格各自的全年均值，再逐月比较，能检出分类相同但雨季或暖季错位的情形。

可用下列命令离线回归评分器。脚本在临时目录构建导出器，生成两半球的合成气候，并用已登记的陆地覆盖几何复核留出单元。回归覆盖独立门槛、恰好 90%、亚型召回率、季节错位和无效月值。

```powershell
python Validation/EarthClimate/test_koppen.py
```

## 海洋与南极近地气温复核

以下对照使用 256 档导出文件和 h5py。首次运行从 NOAA PSL 下载两份公开的 NCEP/NCAR Reanalysis 1 数据（合计约 550 KiB），验证 SHA-256 后缓存在 `ValidationOutput/EarthClimate/sources`。温度常年值选用 1981–2010。该补充数据与 CRU 校准期分别计分。[NOAA PSL 资料与引用说明](https://psl.noaa.gov/data/gridded/data.ncep.reanalysis.html)

```powershell
python Validation/EarthClimate/check_marine_air.py ValidationOutput/EarthClimate/baseline.bin
python Validation/EarthClimate/check_marine_air.py ValidationOutput/EarthClimate/current.bin
```
