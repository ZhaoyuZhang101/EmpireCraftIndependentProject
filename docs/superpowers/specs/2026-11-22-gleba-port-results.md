# Gleba v0.3.6 地理、环流、海洋与生态模拟八轮完整移植总结报告（`DiDu_geleba`）

- **日期**：2026-11-23
- **结果分支**：`DiDu_geleba`
- **基准种子**：`ORBIS-2D-2026`
- **关联规范与源码**：
  - UI 方案 A 存档规范：[2026-11-22-ui-proposal-a-edge-rail.md](file:///d:/ORBIS_TerrainPerf3_5_Markley/docs/superpowers/specs/2026-11-22-ui-proposal-a-edge-rail.md) · [OrbisUI_Round2_Proposals.html](file:///d:/ORBIS_TerrainPerf3_5_Markley/Preview/OrbisUI_Round2_Proposals.html)
  - Gleba 完整逆向参考方案：[2026-11-22-gleba-climate-reference.md](file:///d:/ORBIS_TerrainPerf3_5_Markley/docs/superpowers/specs/2026-11-22-gleba-climate-reference.md)
  - Gleba 独立补充包（6 模块）：[GlebaClimateConfig.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaClimateConfig.cs) · [GlebaHelmholtzSolver.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaHelmholtzSolver.cs) · [GlebaCirculationBands.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaCirculationBands.cs) · [GlebaEcologySupplement.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaEcologySupplement.cs) · [GlebaGeographySupplement.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaGeographySupplement.cs) · [GlebaClimateSupplement.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaClimateSupplement.cs)
  - ORBIS 核心耦合模块：[FlatWindBelts.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatWindBelts.cs) · [FlatOceanCirculation.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatOceanCirculation.cs) · [FlatMoisture.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatMoisture.cs) · [FlatClimate.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatClimate.cs) · [FlatWeatherSimulation.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatWeatherSimulation.cs) · [FlatClimateObservation.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatClimateObservation.cs) · [FlatEcology.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatEcology.cs) · [FlatTerrainAnalysis.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/FlatTerrainAnalysis.cs)
  - 专项回归套件：[GlebaSupplementRegression.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Validation/FlatWorld/GlebaSupplementRegression.cs)

---

## 1. 八轮完整移植演进与 Git 提交记录

在完整保留 ORBIS 严格球面水汽质量守恒（`BudgetError < 2e-6`）、单海盆混合层热量守恒（残差 `< 3e-9 °C`）、跨日界线旋转等变、南北半球对称与 V1–V6 存档字节兼容的前提下，将 `gleba_v0.3.6` 全部大气环流、风带、高空急流、表层洋流、涌升流、生态、土壤及地理水文地质系统完整移植进 `Orbis.Flat.Core`（纯 C# 9.0，`noEngineReferences: true`）：

| 轮次 | Git 提交 | 核心交付内容 |
| :--- | :---: | :--- |
| **Milestone 1：前置包与规范** | `e5686d1` | 归档 WorldBox 风格 UI 方案 A；反汇编 `gleba_godot.dll` 完成参考规范与 5 模块前置补充包。 |
| **Round 1：三环流带与分段气压椭圆** | `b5c92c4` | 将 `Saldo` 半球陆地牵引、ITCZ 五次多项式推进、$\le 750\text{ km}$ 窄海并陆、18 次非对称椭圆海平面气压与 `RainfallImpact` 印章耦合入 `FlatMoisture`。 |
| **Round 2：二维风程大陆度与雨影** | `7468e00` | 实现球面逆风流线追踪（二维无海洋风程与山脉雨影）、副高下沉抑雨、西边界暖流冬季风暴轴增雨、东亚无遮挡寒潮走廊与绕极寒流降温。 |
| **Round 3：Holdridge 生态与六遍土壤** | `a6d9d2b` | 构建并缓存 `Biotemperature`、冰盖/海冰厚度 `GroundedIceMeters`、23 级 `GlebaBiomes`、6 遍土壤流水/冰期黄土/风蚀沙化管线与地块诊断接口。 |
| **Round 4：多分辨率回归基线** | `5547a04` | 完成 `256–2048` 地球与核心自检首阶段多分辨率基准验证。 |
| **Round 5：高空 Rossby 急流与表面风带** | `2356eae` | 移植 4 支高空 Rossby 波急流（`GlebaRossbyWaveField`），将分段副热带高压脊/副极地低压槽地转+跨等压线风与 `Saldo` 热赤道耦合入 `FlatWindBelts.SurfaceWithGleba`、`FlatWeatherSimulation` 与 `FlatClimateObservation`。 |
| **Round 6：Ekman 表层洋流、东边界涌升流与极地冰穹** | `b80020e` | 升级 `FlatOceanCirculation`（有界海盆海岸衰减风驱 Ekman 漂流、加那利/加利福尼亚/秘鲁/本格拉东边界温跃层涌升流、分海盆精确热量闭合），并在 `FlatClimate` 引入对称极地高海拔内陆冰穹辐射降温。 |
| **Round 7：珊瑚礁、峡湾、含水层、咸淡水湖、四级区划与流场图层** | `9d80004` | 新增 [GlebaGeographySupplement.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaGeographySupplement.cs)：20 类基岩与孔隙度表、深层含水层与自流盆地、热带珊瑚礁与沙洲岛、LGM 冰川峡湾、内流咸水湖（里海等）vs 外流淡水湖（苏必利尔湖等）拓扑修正、海洋不可达度、8 地形域四级区划（`Area/Region/SuperRegion/Subcontinent`）、Wallace 生物地理界、12 个月季风反转表层洋流、11 阶 Cividis 色带/流场采样器及热力/水力侵蚀算子。 |

---

## 2. 大气风带、表层洋流、海温异常与极地气温盲测对比

> [!IMPORTANT]
> 所有参数仅在理想地形与开发标定集（`calibration`）上确定；**独立海洋留出集（`ocean-holdout`）** 与 **南极陆地再分析集（`< 60°S`）** 全程未参与任何调参。

### 2.1 NOAA NCEP/NCAR R1 10m 逐月表面风向检验（`check_wind_belts.py`）

| 数据集与纬度带 | 移植前角误差 MAE | `DiDu_geleba` 角误差 MAE | 变化 | 纬向同号率 `E_sign` | 经向同号率 `N_sign`（移植前 → 本版） |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **`calibration` 全海域 (`0–90°`, 32,185 格月)** | 41.59° | **38.52°** | **-3.07°** | **87.23%** | 80.44% → **83.79%** (**+3.35 pp**) |
| `calibration` 低纬信风/季风 (`0–30°`) | 47.18° | **42.40°** | **-4.78°** | 83.49% | 81.36% → **87.08%** (**+5.72 pp**) |
| `calibration` 中纬西风带 (`30–60°`) | 31.24° | **30.10°** | **-1.14°** | **93.12%** | **80.21%** |
| `calibration` 极地/副极地 (`60–90°`) | 59.23° | **58.72°** | **-0.51°** | 77.89% | 71.54% |
| **`ocean-holdout` 盲测全海域 (`0–90°`, 28,465 格月)** | 32.74° | **32.22°** | **-0.52°** | **92.51%** | 82.57% → **84.26%** (**+1.69 pp**) |
| `ocean-holdout` 盲测低纬 (`0–30°`) | 36.30° | **33.85°** | **-2.45°** | **92.84%** | **83.71%** |
| `ocean-holdout` 盲测中纬 (`30–60°`) | 24.35° | **26.59°** | +2.24° | **93.37%** | **86.71%** |

### 2.2 NOAA 全球漂流浮标（GDP）表层洋流矢量检验（`check_drifter_currents.py`）

| 数据集与纬度带 | 移植前流向 MAE | `DiDu_geleba` 流向 MAE | 变化 | 模拟均速 vs 浮标观测 (m/s) | 矢量 RMSE vs 静海对照 (m/s) |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **`calibration` 全海域 (`0–90°`)** | 56.89° | **49.29°** | **-7.60°** | **0.071** vs 0.104 (原 0.041) | **0.1376** < 0.1476 |
| `calibration` 低纬赤道流系 (`0–30°`) | 65.70° | **55.66°** | **-10.04°** | **0.066** vs 0.112 | **0.1571** < 0.1591 |
| `calibration` 中纬副热带环流 (`30–60°`) | 44.51° | **40.53°** | **-3.98°** | **0.081** vs 0.094 | **0.1098** < 0.1334 |
| `calibration` 高纬环流 (`60–90°`) | 42.49° | **37.36°** | **-5.13°** | **0.042** vs 0.089 | **0.0780** < 0.1104 |
| **`ocean-holdout` 盲测全海域 (`0–90°`, 1,043 格)** | 58.55° | **50.54°** | **-8.01°** | **0.097** vs **0.101** (原 0.062) | 0.1543 (静海 0.1393) |
| `ocean-holdout` 盲测低纬 (`0–30°`) | 73.60° | **64.21°** | **-9.39°** | **0.079** vs 0.106 | 0.1661 |
| `ocean-holdout` 盲测中纬 (`30–60°`) | — | **28.55°** | — | **0.128** vs 0.094 | 0.1366 |

### 2.3 NOAA OISST v2 卫星海表温度与 NCEP 海上/南极 2m 气温检验

| 检验指标 | 观测真值 | 移植前基线 | `DiDu_geleba` 结果 | 核心提升说明 |
| :--- | :---: | :---: | :---: | :--- |
| **北大西洋西减东海温差（湾流 − 加那利）** | +4.71 °C | +2.23 °C | **+3.96 °C** | 西边界暖流 + 加那利东边界涌升寒流耦合（**+78%**） |
| **北太平洋西减东海温差（黑潮 − 加州）** | +6.44 °C | +2.01 °C | **+3.87 °C** | 黑潮暖池 + 加利福尼亚涌升寒流耦合（**+93%**） |
| **南大西洋西减东海温差（巴西 − 本格拉）** | +5.63 °C | +1.78 °C | **+2.77 °C** | 本格拉涌升寒流强化（**+56%**） |
| **OISST 同纬海温异常相关系数 `r` (`calibration`)** | 1.000 | 0.310 | **0.415** | RMSE `1.888 -> 1.803 °C`，MAE `1.366 °C`（优于静海 `1.982 °C`） |
| **OISST 同纬海温异常相关系数 `r` (`ocean-holdout`)** | 1.000 | 0.066 | **0.167** | 盲测海域空间相关性提升 **2.5 倍** |
| **NCEP 全球海洋 2m 月温 (`12,134` 点)** | — | RMSE 3.91 °C | **RMSE 3.88 °C** | MAE **2.80 °C**（`256` 与 `2048` 一致） |
| **NCEP 南极洲 `< 60°S` 2m 月温 (`1,657` 点)** | — | RMSE 14.86 °C | **RMSE 11.89 °C** | 对称极地高海拔冰穹降温使南极 RMSE 大幅下降 **`-2.97 °C`**（MAE **9.67 °C**） |

---

## 3. CRU-CL v2.0 陆地气候与 Köppen 分类多分辨率评测

### 3.1 开发标定集（`calibration`，6,113 格，$1.029\times 10^8\text{ km}^2$）

| 模型版本 / 分辨率 | 月温 MAE (°C) | 月温偏差 (°C) | 年均温 MAE (°C) | 月雨 MAE (mm) | 月雨 RMSE (mm) | 年雨 MAE (mm) | 年雨 RMSE (mm) | 年雨 `r` | Köppen 五大类 | Köppen 30 细类 |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **移植前基线 (`256`)** | 1.789 | +0.117 | 1.309 | 35.661 | 57.175 | 317.68 | 493.78 | 0.7771 | 81.11% | 55.08% |
| **`DiDu_geleba` (`256`)** | **1.767** | +0.054 | **1.282** | **35.255** | **56.655** | **314.03** | **484.20** | **0.7891** | **81.55%** | **56.00%** |
| **`DiDu_geleba` (`512`)** | **1.736** | +0.023 | **1.252** | **35.084** | **56.452** | **312.31** | **482.42** | **0.7910** | **81.63%** | **56.53%** |
| **`DiDu_geleba` (`1024`)** | **1.730** | +0.015 | **1.247** | **34.970** | **56.293** | **310.43** | **481.28** | **0.7930** | **81.57%** | **56.35%** |
| **`DiDu_geleba` (`2048`)** | **1.727** | **+0.012** | **1.244** | **34.916** | **56.259** | **309.87** | **481.26** | **0.7940** | **81.57%** | **56.52%** |

### 3.2 独立空间盲测集（`holdout`，1,806 格，$2.889\times 10^7\text{ km}^2$）

| 模型版本 / 分辨率 | 月温 MAE (°C) | 月雨 MAE (mm) | 月雨 RMSE (mm) | 年雨 MAE (mm) | 年雨 RMSE (mm) | 年雨偏差 (mm) | Köppen 30 细类（移植前 → 本版） |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **移植前基线 (`256`)** | 1.844 | 33.968 | 57.441 | 312.56 | 508.50 | +122.83 | 52.26% |
| **`DiDu_geleba` (`256`)** | 1.860 | **32.496** | **56.458** | **302.32** | **486.54** | **+71.91** | 52.26% → **54.16%** (**+1.90 pp**) |
| **`DiDu_geleba` (`512`)** | 1.852 | **32.477** | **56.482** | **302.13** | **485.81** | **+67.60** | 52.70% → **53.83%** (**+1.13 pp**) |
| **`DiDu_geleba` (`1024`)** | 1.853 | **32.520** | **56.823** | **302.23** | **487.61** | **+61.58** | 52.44% → **53.07%** (**+0.63 pp**) |
| **`DiDu_geleba` (`2048`)** | 1.850 | **32.520** | **56.758** | **301.25** | **486.39** | **+62.46** | 52.28% → **53.05%** (**+0.77 pp**) |

---

## 4. 地理水文地质与可视化特征实测摘要（Earth `256 × 128`）

- **内流咸水湖 vs 外流淡水湖拓扑判别**：里海精确识别为 `SaltwaterLake`（`Endorheic`），北美苏必利尔湖精确识别为 `SweetwaterLake`（`Glacial`），彻底修复此前内流湖误判为淡水湖的问题。
- **热带珊瑚礁与沙洲**：共识别 `131` 个暖浅海灰岩珊瑚礁格（大堡礁、珊瑚大三角、马尔代夫、巴哈马等），`> 40°` 中高纬海域误报数为 **`0`**。
- **LGM 冰川峡湾海岸**：共识别 `1,114` 个高纬山脉峡湾岸段（挪威西岸、智利巴塔哥尼亚、阿拉斯加东南/BC、新西兰峡湾地、格陵兰），`< 32°` 热带/副热带深海海沟陡岸误报数为 **`0`**（已剔除水下海沟伪高差干扰）。
- **深层孔隙含水层**：在无冰盖储水沉积盆地上生成 `632` 个区域含水层（含潜水面埋深、饱水厚度与自流盆地标记），南极冰盖下含水层数为 **`0`**。
- **全球海洋不可达度与四级地理区划**：太平洋主大洋不可达度 `0 km`，准噶尔盆地（欧亚大陆难抵极）不可达度达 **`2,971 km`**，里海不重置海洋距离；全球自动聚合为 **`4,475` Areas → `643` Regions → `196` SuperRegions → `108` Subcontinents**，并输出 8 大 Wallace 生物地理界。
- **12 个月季节洋流与 Cividis 图层**：索马里/北印度洋表层流在 1 月东北季风与 7 月西南季风间呈现显著反转；完整提供 Gleba 11 阶 Cividis 色带（`#00224e -> #fee837`）、向岸蓝/离岸红海岸风着色及 `(FlowU, -FlowV)` UV 矢量流场接口。
