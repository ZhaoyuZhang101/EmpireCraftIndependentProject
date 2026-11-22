# Gleba v0.3.6 气候与生态模拟完整逆向参考方案（ORBIS `DiDu_geleba` 移植规范）

- **日期**：2026-11-22
- **目标分支**：`DiDu_geleba`
- **逆向对象**：`d:\gleba_v0.3.6-windows\windows\gleba_godot.dll`（Rust `x86_64-pc-windows-gnu` PE32+，`ImageBase = 0x180000000`，含 176,352 个未剥离 COFF 符号与 Serde 结构重定位表）及 `common/model.ron`、`common/static.ron`、`common/mods/base/data/biomes.lua`。
- **关联补充包**：
  - [GlebaClimateConfig.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaClimateConfig.cs)
  - [GlebaHelmholtzSolver.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaHelmholtzSolver.cs)
  - [GlebaCirculationBands.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaCirculationBands.cs)
  - [GlebaEcologySupplement.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaEcologySupplement.cs)
  - [GlebaClimateSupplement.cs](file:///d:/ORBIS_TerrainPerf3_5_Markley/Assets/ORBIS/Runtime/FlatWorld/Core/GlebaClimateSupplement.cs)

---

## 1. 逆向核心发现总览（机器码级真值 vs 直觉假设修正）

通过对 `gleba_godot.dll` 全部气候、环流、水文、冰川、土壤与生态函数的逐指令反汇编及 `.rdata` 常量重定位，确认了 Gleba v0.3.6 气候引擎的 **10 项核心机制**：

1. **双层网格架构**：
   - 底层行星地块为 Goldberg 多面体网格（地球半径 $R = 6371\text{ km}$ 时为 $1,015,682$ 个六边形/五边形地块），气候求解运行在**行主序等距圆柱网格** `idx = y * W + x`（地球默认 $N = 201 \times 201 = 40,401$ 格，$y=0$ 为南极 $-\pi/2$，$y=N-1$ 为北极 $+\pi/2$，$x=0..N-1$ 对应 $-\pi..+\pi$ 周期包裹）。
   - `ClimateCell` 大小精确为 **`0x148`（328 字节）**，其中的 `[f32; 13]` 数组（`rainfall`、`temperature`、`humidity`、`continentality`、`rain_shadowiness`）代表 $t = s / 12.0 \;(s \in 0..=12)$ 的 **13 个年内时间切片**（`s=0` 与 `s=12` 同为 1 月冬至端，`s=6` 为 7 月夏至端，而非“12 个月 + 年均值”）。
2. **半球陆地面积平衡（`calculate_saldo`，`0x180a28600`）**：
   - 沿每一经度列 $x$，分别对北半球（$\phi > 0$）和南半球（$\phi < 0$）积分 $|\sin\phi| \cdot \text{land\_frac} / N$，得到 `saldo_north >= 0` 与 `saldo_south <= 0`。在大洋经度上，`saldo_north + saldo_south` 将热带辐合带（ITCZ）向陆地面积更大的一侧半球牵引（最高达 $7.0^\circ$）。
3. **三带动态推进与纵向圆周平滑（`Itcz` / `Sthz` / `PolarFront`）**：
   - 共用 `itcz::march_zone_for_season`（`0x180928530`）与 `march_zone`（`0x180927860`）：从海洋基准纬度（ITCZ $\pm 4^\circ$，STHZ 夏 $\pm 30^\circ$/冬 $\pm 28^\circ$，极锋由 STHZ 均值动态导出 $\frac{\bar{\phi}_{\text{STHZ}} \pm 90^\circ}{2} \approx \pm 60^\circ$）向大陆内部目标纬度（ITCZ $\pm 32.5^\circ$，STHZ 夏 $\pm 33^\circ$/冬 $\pm 30^\circ$，极锋 $\frac{\bar{\phi}_{\text{STHZ}} \pm 135^\circ}{2.5} \approx \pm 66^\circ$）扫描纯陆地跨度；窄岛屿/半岛自动取陆地弦中点，大陆内部受最高海拔 $(e_{\max}/40\text{ m})^2$ 调制且不超过大陆宽度的 $1/3$；随后执行 $2W$ 次 `[0.05, 0.90, 0.05]` 经向周期 Jacobi 平滑。
   - 季节插值：**ITCZ** 使用 Ken Perlin 五次多项式缓动 $s(u) = u^3(6u^2 - 15u + 10)$（使季风槽在夏/冬至附近驻留更久、春秋季快速越赤道），而 **STHZ 与 PolarFront** 使用线性三角波 $u = 1 - |1 - 2t|$。
4. **海平面气压椭圆印章与降水影响场（`SeaLevelPressureField::tick`，`0x180924710`）**：
   - `split_into_subsections`（`0x180926b50`）将每条环流带切分为海/陆区段，**自动把宽度 $\le 750\text{ km}$ 的狭窄海峡/内海（如红海、波斯湾、地中海、日本海）并入两侧大陆区段**，并在 $\pm 180^\circ$ 日界线处无缝首尾合并。
   - `atmosphere_oval`（`0x180923480`）采用 **向西 2:1 拉伸的非对称椭圆距离**（中心东侧半轴为 $r$，中心西侧半轴为 $2r$），配合 `center_frac = 0.75`（大洋盆地偏东 $3/4$ 处）精确复现亚速尔高压、北太平洋高压等副热带东岸强化高压脊。
   - **18 次 `do_sections` 印章（9 次气压 + 9 次 `rainfall_impact_field`）**揭示了 Gleba 区分**大陆西岸/腹地荒漠**与**大陆东岸季风**的关键机制：
     - 在宽广大陆（宽度 $\ge 2500\text{ km}$ 且西侧相邻海洋 $\ge 1000\text{ km}$）的 **`center_frac = 0.00`（西海岸）与 `0.25`（西部腹地）**，全年印上 **`1100 hPa` 高压**与 **`rainfall_impact = -1.0` 强下沉抑雨**（精确锁定撒哈拉、阿拉伯、塔尔、纳米布、澳大利亚中西部、北美西南荒漠）！
     - 在同一大陆的 **`center_frac = 0.75`（东岸与季风区）**，夏季印上 **`900 hPa` 热低压**与 **`+0.5` 季风增雨**，冬季切换为 **`1100 hPa` 大陆冷高压**与 **`-1.0` 干冷季风**（精确锁定东亚、南亚、北美东南与南美东岸）！
5. **三风合成与海岸风向分类（`get_wind_direction` + `ShoreType`）**：
   - 地转风 $\mathbf{V}_{\text{geo}}$（北半球顺时针绕高压 $+90^\circ$，南半球逆时针绕高压 $-90^\circ$，在 $|\phi| \in [2.5^\circ, 7.5^\circ]$ 线性衰减至 0）+ 气压梯度穿等压线风 $\mathbf{V}_{\text{pg}} = -\nabla P$ + 行星盛行纬向风 $u_{\text{prev}}(\phi)$，按权重 `(0.5, 0.5, 2.0)` 合成后取 `0.5 * raw + 0.5 * normalize(raw)` 并乘纬度风速因子 $(0.5 + |\phi_{\text{deg}}| / 60)$。
6. **解析大洋环流高度场（`OceanCurrentsField::tick`，`0x1809208e0`）**：
   - 由环流带重心坐标与 `-50` 陡峭 Sigmoid 构造副热带高压穹隆（`1100`）与副极地/赤道低压槽（`900`），并在距陆地 $\le 500\text{ km}$ 处施加反向海岸边界势（副热带海岸压低至 `250`，副极地海岸抬高至 `1750`），经 20 次 Jacobi 平滑后取 **99% 地转流 + 1% 梯度流**，天然涌现出**西边界暖流**（湾流、黑潮、巴西暖流、东澳暖流）与**东边界寒流**（加利福尼亚、加那利、秘鲁、本格拉寒流）以及北大西洋暖流/千岛寒流！
7. **二维半拉格朗日 Helmholtz 平流大陆度与地形雨影（Pass 1）**：
   - 弃用早期一维东西扫描线，改用二维季节风场对陆地掩码（`land_frac > 0.5`）平流 **128 步**（`dt = 0.0055`）并用同步平流的全 `1` 场归一化，得到**随风向动态变化的季节风程大陆度 `continentality[s]`**（经陡峭折线 `[(0,0), (0.75,0.05), (1.0,1.0)]` 映射：只要上风向有 $\ge 25\%$ 海洋水汽源即保持海洋性湿润；一旦 $>75\%$ 为陆地风程，迅速切换为大陆性极旱曲线！）。
   - 同时对高山屏障场 `elev > 1500 ? min(elev / 3500, 1) : 0` 沿二维季节风场平流 **20 步**（`dt = 0.011`），生成背风坡雨影抑制场 `rain_shadowiness[s]`。
8. **六区动态有效纬度映射（Pass 2）**：
   - 将真实纬度 $\phi$ 按当地经度列的 `[-\pi/2, PF_S, STHZ_S, ITCZ, STHZ_N, PF_N, +\pi/2]` 分段线性映射到标准环流纬度 `[-90°, -60°, -30°, 0°, +30°, +60°, +90°]`，再按 `0.33`（温度）和 `0.75`（降水）与真实纬度混合后查询 `model.ron` 的 19–22 点海/陆夏冬基准曲线。
9. **水汽/热量平流分离与微气候直减率**：
   - 降水场仅沿大气风场平流 128 步（`dt = 0.0015`），乘 `(1 - rain_shadowiness[s])` 与大陆腹地不可达度干旱因子；温度场转开尔文后沿洋流场平流 128 步（`dt = 0.005`），并在地块采样阶段施加 `+0.43 °C`（`0..100 m` 低地增温）与 `-4.3 °C/km`（`>100 m` 海拔直减率）。
10. **Holdridge 17 曲线植物功能型 + 23 级生物群系 + 四遍冰川 + 六遍土壤流水沉积**：
    - `initialize_flora_from_holdridge_and_koppen`（`0x1808efa90`）通过 17 张生物温度/降水/纬度/林线折线表计算乔木、灌木、草本、针阔比与裸地率（`dead_land`），包含高山林线退树转草与热带干季稀树草原退树转灌草机制；随后由 `biomes.lua` 按优先级 `0..301` 判定群系。

---

## 2. Gleba 与 ORBIS 现有气候系统架构对比与融合策略

| 维度 | Gleba v0.3.6 原生实现 | ORBIS 现有实现（`FlatClimate` + `FlatMoisture`） | `DiDu_geleba` 融合移植方案（取长补短） |
| :--- | :--- | :--- | :--- |
| **水汽质量守恒** | 非守恒经验曲线 + 半拉格朗日平流 + 乘性裁剪 | **严格球面面积加权水汽质量守恒**（`BudgetError < 2e-5`，面通量 `ReportFlux` 精确闭合） | **保留 ORBIS 严格质量守恒框架**，将 Gleba 物理机制注入蒸发、抬升（`Lift`）、凝结效率（`Efficiency`）、风场与云下蒸发（`DryLayer`）！ |
| **经度旋转与种子不变性** | `atmosphere_oval` 日界线 `dx` 存在符号翻转小瑕疵；叠加世界种子 Perlin 噪声 | **严格要求跨日界线旋转不变、与种子无关、多线程逐位确定**（`RotatedContinent` / `MoistureRegression`） | 修正 `atmosphere_oval` 为精确周期有符号距离 `WrapSigned`，主气候法线使用零噪声确定性模式，100% 通过旋转与并发一致性契约！ |
| **副热带高压与东西岸分异** | **18 次分段气压/抑雨椭圆印章**：宽大陆西侧 `0%/25%` 常年高压极旱，东侧 `75%` 夏低压多雨/冬高压少雨 | 仅用固定高斯纬度带 $\exp(-((|\phi|-24)/13)^2)$ 与局部海陆差，导致撒哈拉/萨赫勒/塔尔/澳洲内陆偏湿 1.5x–6x | 引入 `GlebaSeaLevelPressureField` 的**分段海陆副高下沉印章（`RainfallImpact`）**与气压梯度风场耦合，精准压制宽大陆西侧及内陆副高荒漠！ |
| **风程大陆度与背风坡雨影** | **2D Helmholtz 半拉格朗日风向追踪**：`continentality[m]`（128 步）与 `rain_shadowiness[m]`（20 步） | 各向同性 `CoastDistance` + 仅在 `summerJet > 0` 时对背风坡做 3 步衰减（导致德干高原背风坡模拟 1414 mm vs 观测 758 mm） | 引入 `GlebaHelmholtzSolver` **全风系二维风程大陆度与二维山脉雨影场**，显著改善德干背风坡、塔里木、青藏高原与安第斯背风坡精度！ |
| **ITCZ 与环流带跃迁** | `calculate_saldo` 半球陆地面积平衡 + 五次多项式季节推进 + 六区有效纬度映射 | 局部海陆比经验公式 `ConvergenceLatitude` | 融合 `GlebaCirculationBands` 的**经向 Saldo 牵引与 ITCZ 对流核增雨**，强化亚马逊、印尼/菲律宾与孟加拉湾深对流核降水！ |
| **大洋环流与海气耦合** | 解析海面动态高度场 + 500 km 海岸边界偏转（副热带西暖东寒 + 副极地西寒东暖） | Stommel 西边界流函数（仅单调副热带型，缺少副极地拉布拉多/亲潮寒流与挪威暖流细节） | 在保留 `FlatOceanCirculation` Stommel 守恒混合层的同时，融合 Gleba 副极地/东边界海气对比与沿海温度调制！ |
| **生态、林线、稀树草原与诊断** | Holdridge 生物温度 `B` + 17 张植物功能型曲线 + 干季稀树草原转移 + 海冰/冰川厚度 | 经验需水量 `AnnualWaterDemand` + 简单群系分支 | 将 `GlebaEcologySupplement` 接入 `FlatEcology` / `FlatTerrainAnalysis` 并在 `FlatClimateStatistics` 暴露 Gleba 诊断指标！ |

---

## 3. 核心数据结构与数学公式速查表

### 3.1 `ClimateCell` 内存布局（`0x148 = 328` 字节）

| 字节偏移 | 字段名 | Rust 类型 | 物理含义 |
| :--- | :--- | :--- | :--- |
| `+0x00..+0x08` | `elevation_extrema` | `(f32, f32)` | 格内最低/最高海拔 `(min, max)`（m） |
| `+0x08..+0x10` | `water_tiles`, `land_tiles` | `(u32, u32)` | 格内海洋/陆地 Goldberg 地块计数 |
| `+0x10` | `average_tile_inaccessibility` | `f32` | 地形不可达度（`u16 * 25.231325`） |
| `+0x14` | `true_continentality` | `f32` | 13 个季节切片 `continentality` 的算术均值 |
| `+0x18..+0x24` | `distance_to_{land,sea,deep_sea}` | `[f32; 3]` | 八邻域倒角 BFS 距离（km，`deep_sea` 定义为 `water_frac > 0.75`） |
| `+0x2c` | `elevation` | `f32` | 格内平均海拔（m） |
| `+0x30..+0x38` | `saldo_north`, `saldo_south` | `(f32, f32)` | 同经度列南北半球 $|\sin\phi|\cdot\text{land\_frac}/N$ 积分 |
| `+0x40..+0x74` | `rainfall` | `[f32; 13]` | 13 个时间切片月降水量（mm/month） |
| `+0x74..+0xa8` | `temperature` | `[f32; 13]` | 13 个时间切片海平面等效气温（°C） |
| `+0xa8..+0xdc` | `humidity` | `[f32; 13]` | 13 个时间切片湿度指数 $\in [0, 1]$ |
| `+0xdc..+0x110` | `continentality` | `[f32; 13]` | 128 步风场平流大陆度 $\in [0, 1]$ |
| `+0x110..+0x144` | `rain_shadowiness` | `[f32; 13]` | 20 步风场平流山脉雨影度 $\in [0, 1]$ |

### 3.2 关键数学方程

1. **半球陆地平衡（Saldo）**：
   $$\text{saldo}_N(x) = \frac{1}{H}\sum_{\phi_y > 0} |\sin\phi_y|\cdot f_{\text{land}}(x,y), \qquad \text{saldo}_S(x) = -\frac{1}{H}\sum_{\phi_y < 0} |\sin\phi_y|\cdot f_{\text{land}}(x,y)$$
2. **海平面气压非对称椭圆距离（`atmosphere_oval`，经周期修正）**：
   $$\Delta x = \text{WrapSigned}(x - c_x, W), \qquad s_x = \begin{cases} 1.0, & \Delta x \ge 0 \\ 0.25, & \Delta x < 0 \end{cases}, \qquad d = \sqrt{s_x \Delta x^2 + (y - c_y)^2}$$
   $$w(d) = \left(1 - \sqrt{\text{clamp}(d / r, 0, 1)}\right)\cdot \text{strength}$$
3. **二维半拉格朗日归一化平流（`helmholtz::advect`）**：
   $$\mathbf{p} = \mathbf{x} - \Delta t_0 \mathbf{v}(\mathbf{x}), \qquad \tilde{F} = \frac{\mathcal{A}^K(F_0)}{\max(\mathcal{A}^K(1), 10^{-5})}$$
4. **六区动态有效纬度映射**：
   $$\phi \in [-\tfrac{\pi}{2}, \phi_{\text{PF},S}, \phi_{\text{STHZ},S}, \phi_{\text{ITCZ}}, \phi_{\text{STHZ},N}, \phi_{\text{PF},N}, +\tfrac{\pi}{2}] \;\longmapsto\; \phi_{\text{eff,deg}} \in [-90^\circ, -60^\circ, -30^\circ, 0^\circ, +30^\circ, +60^\circ, +90^\circ]$$

---

## 4. `DiDu_geleba` 多轮移植实施路线图

1. **前置阶段（已完成/本轮提交）**：
   - 保存 WorldBox 风格 UI 方案 A 规范（[2026-11-22-ui-proposal-a-edge-rail.md](file:///d:/ORBIS_TerrainPerf3_5_Markley/docs/superpowers/specs/2026-11-22-ui-proposal-a-edge-rail.md)）。
   - 完成 Gleba 完整逆向参考方案（本文档）与 **Gleba 独立补充包**（`GlebaClimateConfig`、`GlebaHelmholtzSolver`、`GlebaCirculationBands`、`GlebaEcologySupplement`、`GlebaClimateSupplement` + `--gleba-supplement` 回归测试集）。
2. **第一轮移植（Round 1 — 大气环流带、分段海平面气压椭圆与表面风/海气环流耦合）**：
   - 将 `GlebaCirculationBands` 的 Saldo 热赤道牵引、分段海陆副高/季风低压椭圆印章（`SeaLevelPressureField`）及海气环流特征耦合进 `FlatMoisture` 与 `FlatClimateObservation` 的气压/风场求解。
   - 通过 `--wind-belts`、`--ocean-circulation`、`--moisture-flux` 与地球风带检验。
3. **第二轮移植（Round 2 — 二维风程大陆度、全风系 Helmholtz 地形雨影、副高下沉抑雨与温度/降水精度标定）**：
   - 将二维风程大陆度（`WindContinentality`）、全风系二维山脉背风坡雨影（`RainShadowiness`）、宽大陆西侧副高常年抑雨（`RainfallImpact`）及赤道深对流核增强融合进 `FlatMoisture` 与 `FlatClimate`（仅在 `calibration` 集与理想大陆上标定，严禁触碰 `holdout` 集调参）。
   - 目标：在 `holdout` 独立检验集上显著降低月降水 MAE/RMSE 与年降水误差，大幅提升 `<250 mm` 干旱区召回率（撒哈拉、塔尔、塔里木、萨赫勒、德干背风坡、澳洲内陆）及 Köppen 5 大类 / 30 亚类一致率！
4. **第三轮移植（Round 3 — Holdridge 生物温度、林线/稀树草原生态群落、海冰/冰川诊断与统计面板集成）**：
   - 将 `GlebaEcologySupplement` 的生物温度、高山林线退树转草、热带干季稀树草原分异融合进 `FlatEcology` / `FlatTerrainAnalysis`，并在气候诊断系统中暴露 Gleba 环流/雨影/生物温度指标。
5. **第四轮收官验证（Round 4 — 多分辨率全量回归、确定性与存档兼容检验、最终对比报告）**：
   - 运行 `FlatWorld.Core.Tests` 全套回归、`test_koppen.py`、多分辨率地球（`256`、`512`、`1024`、`2048`）`calibration` + `holdout` 评测，输出完整前后对比报告并整洁提交至 `DiDu_geleba` 分支。
