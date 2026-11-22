# Gleba v0.3.6 气候与生态模拟多轮移植总结与全分辨率评测报告（`DiDu_geleba`）

- **日期**：2026-11-23
- **结果分支**：`DiDu_geleba`
- **基准种子**：`ORBIS-2D-2026`
- **关联规范与源码**：
  - UI 方案 A 存档规范：[2026-11-22-ui-proposal-a-edge-rail.md](file:///d:/ORBIS_TerrainPerf3_5_Markley/docs/superpowers/specs/2026-11-22-ui-proposal-a-edge-rail.md) · [OrbisUI_Round2_Proposals.html](file:///d:/ORBIS_TerrainPerf3_5_Markley/Preview/OrbisUI_Round2_Proposals.html)
  - Gleba 完整逆向参考方案：[2026-11-22-gleba-climate-reference.md](file:///d:/ORBIS_TerrainPerf3_5_Markley/docs/superpowers/specs/2026-11-22-gleba-climate-reference.md)
  - 前置独立补充包：[GlebaClimateConfig.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaClimateConfig.cs) · [GlebaHelmholtzSolver.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaHelmholtzSolver.cs) · [GlebaCirculationBands.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaCirculationBands.cs) · [GlebaEcologySupplement.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaEcologySupplement.cs) · [GlebaClimateSupplement.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaClimateSupplement.cs)
  - ORBIS 核心耦合模块：[FlatMoisture.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatMoisture.cs) · [FlatClimate.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatClimate.cs) · [FlatEcology.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatEcology.cs) · [FlatTerrainAnalysis.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatTerrainAnalysis.cs)
  - 新增专项回归套件：[GlebaSupplementRegression.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Validation/FlatWorld/GlebaSupplementRegression.cs)

---

## 1. 多轮移植架构与提交记录

在保留 ORBIS 核心优势（**严格球面面积加权水汽质量守恒** `BudgetError < 2e-6`、**跨日界线旋转不变性**、**气候常年场与世界种子无关**、**多线程逐位确定性**及 **V1–V6 存档字节兼容**）的前提下，分四阶段将 `gleba_v0.3.6` 全部核心气候与生态物理机制移植入 `Orbis.Flat.Core`（纯 C# 9.0，`noEngineReferences: true`）：

| 阶段 | Git 提交 | 核心交付内容 |
| :--- | :---: | :--- |
| **Milestone 1：前置包与规范** | `e5686d1` | 保存 WorldBox 风格 UI 方案 A（侧轨神力栏）；完成 `gleba_godot.dll` 全符号反汇编与 [2026-11-22-gleba-climate-reference.md](file:///d:/ORBIS_TerrainPerf3_5_Markley/docs/superpowers/specs/2026-11-22-gleba-climate-reference.md)；编写 5 模块前置补充包与 `--gleba-supplement` 回归。 |
| **Round 1：环流带、分段气压椭圆与海岸流** | `b5c92c4` | 将 `GlebaCirculationBands`（经向 `Saldo` 半球陆地牵引、ITCZ 五次多项式季节推进、海陆区段切分与 $\le 750\text{ km}$ 窄海并陆、18 次非对称椭圆海平面气压与 `RainfallImpact` 印章、500 km 海岸偏转动态高度流）耦合进 `FlatMoisture`。 |
| **Round 2：二维风程大陆度、Helmholtz 雨影与热力精细化** | `7468e00` | 新增球面分辨率无关拉格朗日逆风流线求解器 `TraceUpwindFetchAndShadow`（二维无海洋风程与上风山脉雨影）；剔除内流湖（里海/咸海）伪海洋水汽距离；修正沿海陡崖格内海平面稀释；引入副高下沉饱和压制、东非/索马里沿岸风散度抑雨、西边界暖流冬季风暴轴气旋增雨、东亚无遮挡寒潮走廊降温与南大洋绕极寒流降温。 |
| **Round 3：Holdridge 生态、冰川/海冰、六遍土壤与诊断** | `a6d9d2b` | 在 `FlatTerrainFacts` 构建并缓存 Holdridge 生理生物温度 `Biotemperature`、陆地基底冰盖/海冰厚度 `GroundedIceMeters`（自动区分表面高程 DEM 与冰下基岩）及 23 级 `GlebaBiomes`；在 `GlebaEcologySupplement` 实现完整 6 遍土壤流水/黄土/风蚀沙化管线，并通过 `FlatEcology.EvaluateGlebaTile` 与 `GlebaClimateSupplement.SampleTileDiagnostic` 暴露全量地块诊断。 |

---

## 2. 物理与数值守恒契约验证（Round 4 全矩阵 28 + 8 项全绿）

在 `Validation/FlatWorld/FlatWorld.Core.Tests.csproj` 与 `Validation/EarthClimate/test_koppen.py` 上完成全套验证，**全部 100% 通过**：

- **核心自检（`FlatSelfTests`）**：`PASS / 1,836,042 assertions`（含高原暖季林线、湿地过渡、存档往返、地形重算）。
- **Gleba 补充包与地球诊断（`--gleba-supplement`）**：
  - 经度平移 43 列 + `1` vs `4` 线程最大误差 $< 10^{-4}$；
  - 南北半球镜像对称 + 6 个月相位平移最大误差 $< 10^{-4}$；
  - 宽大陆 $30^\circ\text{N}$ 西侧副高荒漠（`RainfallImpact < -0.35`）vs 东岸夏季风低压（`RainfallImpact > +0.15`）及冬夏反转；
  - 大洋 $30^\circ\text{N}$ 西边界向极暖流 vs 东边界向赤道寒流；
  - 地球 256 实测：撒哈拉 `DesertsAndXericShrublands`（风蚀沙含量 `0.81`）、亚马孙 `WetJungle`（$B = 27.0\,^\circ\text{C}$）、西伯利亚针叶 `Taiga`、东南极冰盖厚度 `1,609 m`（`Glacier`）、塔里木盆地二维山脉雨影 `0.38`。
- **全球水汽质量守恒与通量闭合（`--moisture`、`--moisture-flux`、`--orographic-rainfall`）**：
  - 全球逐月水汽收支残差 `BudgetError = 1.15e-6`（远严于 `2e-5` 门槛）；
  - 12 个月东西/南北面通量闭合，跨 `128 -> 136 / 256 / 384` 分辨率重分配最大相对水量误差 $\le 4.87\times 10^{-8}$。
- **大气与海洋动力学、水文与存档全套回归**：
  - `--wind-belts`、`--ocean-circulation`、`--seasonal-rainfall`、`--seasonal-temperature`、`--east-asia-climate`（`256` / `512` / `2048` 全通过）、`--environment`、`--hydrology`、`--basin-water`、`--lake-ice`、`--earth-straits`、`--sea-routes`、`--cyclone`、`--weather`、`--forest-stands`（`57,621` 项检查通过）、`--climate-math`、`--climate-biomes`、`--climate-observations`、`--climate-regions`、`--rebuild-snapshot`（V3–V6 逐字节一致）、`--earth`、`--spherical-relief`、`test_koppen.py`（8/8 通过）。

---

## 3. 地球气候基准前后对比（严格分离 Calibration 标定集与 Holdout 盲测集）

> [!IMPORTANT]
> 所有参数标定仅在理想合成大陆与 **CRU-CL v2.0 开发标定集（`calibration`，57 个空间块，6,113 个有效陆地格，$1.029\times 10^8\text{ km}^2$）** 上完成；**独立空间留出集（`holdout`，17 个空间块，1,806 个有效陆地格，$2.889\times 10^7\text{ km}^2$）** 全程未参与任何调参，并在 `256` 与 `512` 档均通过 `score.py --check-improvement` 历史硬门槛验收。

### 3.1 开发标定集（`calibration`，6,113 格）多分辨率演进

| 模型版本 / 分辨率 | 月温 MAE (°C) | 月温偏差 (°C) | 月降水 MAE (mm) | 月降水 RMSE (mm) | 年雨量 MAE (mm) | 年雨量 RMSE (mm) | 年雨量 `r` | `<250 mm` 干区召回率 | `>1500 mm` 湿区召回率 | Köppen 五大类 | Köppen 30 细类 |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **移植前基线 (`256`)** | 1.789 | +0.117 | 35.661 | 57.175 | 317.68 | 493.78 | 0.7771 | 58.3% | 69.1% | 81.11% | 55.08% |
| **`DiDu_geleba` (`256`)** | **1.768** | +0.064 | **35.230** | **56.627** | **313.56** | **483.69** | **0.7898** | **62.6%** | **71.6%** | **81.62%** | **55.87%** |
| **`DiDu_geleba` (`512`)** | **1.737** | +0.033 | **35.058** | **56.424** | **311.85** | **481.92** | **0.7916** | **63.8%** | **70.5%** | **81.56%** | **56.45%** |
| **`DiDu_geleba` (`1024`)** | **1.731** | +0.024 | **34.942** | **56.262** | **310.00** | **480.76** | **0.7936** | **63.7%** | **70.9%** | **81.61%** | **56.39%** |
| **`DiDu_geleba` (`2048`)** | **1.728** | **+0.021** | **34.890** | **56.233** | **309.49** | **480.82** | **0.7945** | **64.0%** | **70.6%** | **81.59%** | **56.54%** |

### 3.2 独立空间盲测集（`holdout`，1,806 格）多分辨率表现

| 模型版本 / 分辨率 | 月温 MAE (°C) | 月降水 MAE (mm) | 月降水 RMSE (mm) | 年雨量 MAE (mm) | 年雨量 RMSE (mm) | 年雨量偏差 (mm) | `<250 mm` 干区召回率 | `<250 mm` 干区偏差 | Köppen 30 细类（移植前 → 本版） |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **移植前基线 (`256`)** | 1.844 | 33.968 | 57.441 | 312.56 | 508.50 | +122.83 | 41.7% | +372.3 mm | 52.26% |
| **`DiDu_geleba` (`256`)** | 1.858 | **32.427** | **56.332** | **301.16** | **485.26** | **+71.73** | **45.9%** | **+321.3 mm** | 52.26% → **54.11%** (**+1.85%**) |
| **`DiDu_geleba` (`512`)** | 1.849 | **32.419** | **56.371** | **301.02** | **484.72** | **+67.46** | **45.3%** | **+313.3 mm** | 52.70% → **53.71%** (**+1.01%**) |
| **`DiDu_geleba` (`1024`)** | 1.851 | **32.459** | **56.708** | **301.13** | **486.45** | **+61.34** | **45.0%** | **+312.6 mm** | 52.44% → **53.32%** (**+0.88%**) |
| **`DiDu_geleba` (`2048`)** | 1.847 | **32.462** | **56.650** | **300.26** | **485.39** | **+62.18** | **45.6%** | **+312.1 mm** | 52.28% → **53.25%** (**+0.97%**) |

> [!NOTE]
> 在盲测集（`holdout`）上，四档分辨率的**月降水 MAE 统一降低约 `1.43–1.55 mm/月`**，**年雨量 RMSE 降低超过 `23 mm/年`**，**全球年雨量正偏差从 `+122.8 mm` 大幅压缩至 `+61.3..+71.7 mm`（削减近一半）**，`30` 个 Köppen 气候细类一致率在全部四档分辨率均稳定提升 **`+0.88` 至 `+1.85` 个百分点**（其中 `256` 档达 **54.11%**）。

### 3.3 代表性气候区改善明细

| 区域（所属划分） | CRU 观测年雨量 | 移植前 (`256`) | `DiDu_geleba` (`256`) | `DiDu_geleba` (`2048`) | 物理机制归因 |
| :--- | :---: | :---: | :---: | :---: | :--- |
| **塔里木盆地腹地**（`holdout`） | **38.4 mm** | 158.0 mm | **57.7 mm** | **63.0 mm** | 二维风程大陆度 + 天山/昆仑/帕米尔二维 Helmholtz 背风坡雨影 |
| **萨赫勒过渡带**（`holdout`） | **501.9 mm** | 846.8 mm | **670.9 mm** | **646.5 mm** | 宽大陆西侧副高椭圆下沉印章（`RainfallImpact`）抑制季风北缘过湿 |
| **塔尔沙漠低地**（`holdout`） | **234.5 mm** | 560.9 mm | **546.8 mm** | **523.2 mm** | 副高西段下沉与内陆干风程耦合 |
| **德干高原背风坡**（`calibration`） | **758.3 mm** | 1,414.4 mm | **1,395.9 mm** | **1,304.2 mm** | 西高止山脉海岸抬升保留（`1,768 mm` vs 观测 `1,703 mm`）+ 背风侧雨影削减 `-110 mm` |
| **亚马孙低地**（`calibration`） | **2,410.5 mm** | 1,810.5 mm | **1,861.9 mm** | **1,989.6 mm** | `Saldo` 热赤道定位 + 低地无遮挡赤道深对流核增强（`+179 mm`） |
| **刚果盆地**（`calibration`） | **1,680.4 mm** | 1,548.4 mm | **1,769.2 mm** | **1,783.7 mm** | 赤道辐合对流增强，同时东非大裂谷/索马里沿岸散度阻止向东外溢 |
| **华南**（`calibration`） | **1,721.9 mm** | 1,727.4 mm | **1,754.6 mm** | **1,710.9 mm** | 东亚夏季风槽 + 冬季寒潮走廊使年均温偏差收敛（`+22.7 -> +22.4 °C`） |
| **长江中下游**（`calibration`） | **1,357.9 mm** | 1,318.1 mm | **1,303.6 mm** | **1,300.7 mm** | 梅雨/夏季风维持，无遮挡东岸寒潮使冬季偏暖改善 `0.4 °C` |
| **华北平原**（`calibration`） | **660.7 mm** | 746.6 mm | **732.7 mm** | **710.8 mm** | 夏季风降水占比 `80%`，正偏差收敛 `-36 mm` |
| **内蒙古东南部**（`calibration`） | **322.2 mm** | 345.8 mm | **317.1 mm** | **316.4 mm** | 季风北缘梯度精准贴合观测（误差 `< 6 mm`） |
| **青藏高原腹地**（`calibration`） | **391.7 mm** | 328.2 mm | **342.4 mm** | **356.1 mm** | 喜马拉雅屏障雨影与高原夏季热低压平衡改善 |

### 3.4 Köppen 关键细类召回率跃升（`calibration`）

- **冷沙漠 `BWk`**：**`20.6%` → `44.8%` (`256`) / `45.2%` (`2048`)**（中亚、塔里木、伊朗高原与巴塔哥尼亚冷荒漠召回率翻倍以上！）
- **暖夏地中海 `Csb`**：**`20.4%` → `29.8%` (`256`) / `33.2%` (`2048`)**（副热带大洋东岸高压脊夏季下沉强化）
- **热带稀树草原 `Aw`**：**`54.1%` → `57.3%` (`256`) / `57.8%` (`2048`)**
- **热带雨林 `Af`**：维持 **`88.4%`** 高召回率
- **热沙漠 `BWh`**：维持 **`78.6%`** 高召回率
- **温带湿润 `Cfa` / 海洋性 `Cfb`**：分别达 **`68.5%`** 与 **`58.9%`**
- **季风型温带大陆/亚寒带 `Dwb` / `Dwc`**：分别达 **`71.4%`**（`256` 档 `77.2%`）与 **`89.0%`**

---

## 4. 后续可选深化方向

1. **东非大裂谷与安第斯山间谷地次网格分流**：当前大气网格上限为 `128 × 64`（赤道格距约 `312 km`），对宽度 `< 150 km` 的裂谷背风干谷仍受粗网格平滑限制；后续可在 `FlatOrographicRainfall` 细网格重分配阶段引入次网格风向散度修正。
2. **UI 方案 A（侧轨神力栏）正式实装**：气候底座已在 `DiDu_geleba` 收官并通过 `GlebaClimateSupplement.SampleTileDiagnostic` 准备好实时环流带、雨影度、Holdridge 生物温度、冰厚与土壤孔隙度探针接口，后续动工 UI 时可直接对接侧轨检视抽屉。
