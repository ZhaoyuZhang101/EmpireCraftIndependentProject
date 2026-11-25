# ORBIS 台风螺旋雨带与非对称云形专项升级报告（基线 `aaa9625` → 候选提交）

> **渲染环境**：Unity `6000.0.55f1` · Direct3D11 · `NVIDIA GeForce RTX 5070 Laptop GPU`  
> **严格对照条件**：改前（`aaa9625`）与改后（候选版本）锁定**完全相同的相机位置/姿态、日历日期、太阳经度偏置、曝光与 `q=2` 画质**；台风性能测试在无并发编译负载下以相同 `1280×720` 分辨率、相同视角采集 `q=1`/`q=2` 同步中位数帧时。

---

## 1. 专项改动要点（沿用现有 `FlatCycloneWeather` 物理驱动，仅改 2 个着色器文件）

1. **`FlatSkyBake.shader`（`Cyclone()` 多尺度非对称螺旋雨带 + 逗号状 CDO + 顺旋向卷云流出）**
   - **引导流与风切变非对称坐标系**：基于现有引导流 `steering` 与半球旋向 `spin` 构建 `downwind` / `rightFront` 正交基，形成背风/入流象限宽阔外延雨带与卷云羽扇、迎风/干切变侧紧凑清晰晴空弧的层次非对称。
   - **四条弧长、宽度与厚度不同的螺旋雨带**：采用自然对数螺距（`~0.9–1.05` 圈，破除过圆厚云盖且避免人为同心卷），以周期角距 `atan2(sin, cos)` 塑造四条不等长、不等宽、不等厚的非对称雨带（主雨带 `r=0.06..1.48`、次雨带 `+148° r=0.07..1.24`、外围辐合入流尾带 `+256° r=0.24..1.52`、内区逗号旋臂 `+66° r=0.06..0.64`）及外缘羽状次丝缕。
   - **带间低云云隙与三维高度反差**：移除铺满整个风暴盘面的 `spiralApron` 与 `shield` 圆形雾底，在雨带间保留深蓝海面云隙与 `1.2–2.6 km` 卷曲低云云街，与 `11–12 km` 亮白主雨带墙形成鲜明亮度与立体阴影反差；拉远全风暴视角亦可一眼辨识多条雨带向眼墙卷入。
   - **紧致非圆逗号状 CDO 与顺旋向卷云流出**：收紧并按主旋臂与引导流塑形内区 CDO，与 `rWall` 平滑衔接（无圆环台阶、保住 `79e6fa2` 零灰斑）；上层卷云 `outflowTurn` 沿旋向展开为背风羽流，同时在带间云隙处适度透空，不糊没螺旋槽沟。
2. **`ScrollCloudSource.cginc`（`ApplyTyphoon3DStadium()` 移除圆形 `cdoCap` 覆盖 + 修正南半球三维旋向）**
   - 移除原 `r < 0.52` 轴对称圆形 `cdoCap * 0.76` 覆盖层，仅保留紧致 `r < 0.21` 3D 体育场斜壁眼墙领口与眼底低云，让螺旋槽沟可一路卷入眼墙外缘；
   - 将防灰斑所需的 `cdoSmooth` 改为按实际 `coverage` 门控（`smoothstep(.44, .84, coverage)`），在保住 CDO 与亮雨带零灰斑的同时彻底释放带间云隙清晰度；
   - 修正 `rotPhase` 符号使北半球（逆时针）与南半球（顺时针）三维云体随 `FlatCycloneWeather.Phase` 同向旋转，并以柔和旋扭梯度（`1.25`）保持三维蓬松立体感。

---

## 2. 台风专项三栏对照与演进复查图清单

| 归档文件 | 内容与镜头参数 | 参考来源与拍摄日期 / 路径 |
| :--- | :--- | :--- |
| `typhoon_compare_01_wide_fullstorm.png` | **拉远全风暴与外雨带三栏对照**（改前 `aaa9625` \| 改后 \| NASA）：Day `228.08`, `(127.2°E, 19.2°N)`, `R=1.72`, `tilt=0°`, `sunOff=+24°`, `q=2` | **NASA Worldview / Suomi NPP VIIRS True Color（超强台风海贝思）** · **`2019-10-08`** (`worldview_viirs_typhoon_hagibis.jpg`) |
| `typhoon_compare_02_eyewall_closeup.png` | **近看眼墙与内旋臂三栏对照**（改前 `aaa9625` \| 改后 \| NASA）：Day `228.07`, `(127.4°E, 20.1°N)`, `R=1.34`, `tilt=14°`, `sunOff=+28°`, `q=2` | **NASA ISS Expedition 7 (`ISS007-E-14858`, 飓风伊莎贝尔眼墙)** · **`2003-09-13`** (`iss07_hurricane_isabel_20030913.jpg`) |
| `typhoon_compare_03_evolution_and_chirality.png` | **真实 `FlatCycloneWeather` 演进与南北半球旋向复查**：同一加勒比自然飓风 `T0`（Day `256.25`, `45.2 m/s`, `75.3°W, 14.4°N`, `phase=15.15 rad`）→ `T0+24h`（Day `257.25`, `44.1 m/s`, `81.3°W, 14.7°N`, `phase=21.87 rad`，西移 + 逆时针旋转）+ 南半球自然气旋峰值（Day `74.75`, `47.4 m/s`, `21.7°W, 14.3°S`, `phase=-13.82 rad`，镜像顺时针旋卷） | 真实模拟步进截图：`03_natural_cyclone_t0.png`、`04_natural_cyclone_t1_later.png`、`05_natural_sh_cyclone_cw.png` |

---

## 3. 台风同条件帧时对比（`1280×720` · 无负载同步中位数 · RTX 5070 Laptop）

| 台风场景 | 画质 | 改前 `aaa9625`（总帧时 / 纯云） | 改后候选（总帧时 / 纯云） | 纯云变化 |
| :--- | :---: | :---: | :---: | :---: |
| **Typhoon-Wide-FullStorm**（拉远全风暴） | `q=1` | `13.95 ms` / `8.15 ms` | **`13.94 ms` / `8.16 ms`** | **`+0.01 ms`（持平）** |
| **Typhoon-Wide-FullStorm**（拉远全风暴） | `q=2` | `20.15 ms` / `14.67 ms` | `21.09 ms` / `15.39 ms` | `+0.72 ms` |
| **Typhoon-CloseUp-Eyewall**（近看眼墙） | `q=1` | `12.03 ms` / `7.02 ms` | `13.25 ms` / `8.57 ms` | `+1.55 ms`（带间边缘细化） |
| **Typhoon-CloseUp-Eyewall**（近看眼墙） | `q=2` | `19.03 ms` / `14.26 ms` | `19.61 ms` / **`14.45 ms`** | **`+0.19 ms`（+1.3%）** |

---

## 4. 全量回归验证

- **C# 气候、云量与气旋核心回归（`FlatWorld.Core.Tests.dll`）**：**`PASS / 1,833,265 assertions`**（0 警告、0 错误）
- **原生 Windows D3D11 云着色器变体编译**：**`44 / 44 PASS`**（`FlatSkyBake`、`HPCloudColumn`、`Clouds` 5 Pass、`AtmosphereComposite` 2 Pass、`WaterCloudProbe`、`CloudPassageProbe`，Linear + Gamma，`vs_5_0` + `ps_5_0`）
- **Unity 6000.0.55f1 D3D11 GPU 回归（`4 / 4 PASS`）**：
  - `PASS ClimateCloudRegression.Run()`（云影、雨雪门控、穿云连续性探针）
  - `PASS HPCloudHeightRegression.Run()`
  - `PASS WaterCloudRegression.Run()`
  - `PASS LightningRegression.Run()`
