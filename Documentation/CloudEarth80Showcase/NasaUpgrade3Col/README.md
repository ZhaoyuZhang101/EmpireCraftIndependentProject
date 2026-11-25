# ORBIS 全球体积云 NASA 实拍对标升级验收报告（基线 `79e6fa2` → 候选提交）

> **渲染环境**：Unity `6000.0.55f1` · Direct3D11 · `NVIDIA GeForce RTX 5070 Laptop GPU`  
> **对照控制**：改前（`79e6fa2`）与改后（候选版本）锁定**完全相同的相机位置与姿态、日历日期、太阳经度偏置、曝光设置与 `q=2` 画质**；性能测试均在无并发编译负载下以相同 `1280×720` 分辨率、相同视角采集 `q=1` / `q=2` 同步中位数帧时。

---

## 1. 核心诊断与改动摘要

对照多日期、多半球的 **NASA DSCOVR EPIC** 全盘自然色图、**NASA Worldview (Suomi NPP VIIRS)** 真彩局地影像与 **ISS** 轨道实拍，从 `79e6fa2` 改前实机成图中识别出三处最大视觉差异，并完成 3 轮闭环着色器升级（零新增 3D 纹理采样）：

1. **全球尺度云团聚散、极区/热带自然晴空与中纬弯曲锋面（[FlatSkyBake.shader](../../../Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatSkyBake.shader)）**
   - 引入行星尺度无散平流聚散场 `synCluster` 与非纬向 Rossby 波动摆幅（`±10°` 纬向游走），破除固定纬向云带；
   - 锐化中纬冷锋后缘梯度与气旋干侵入舌，同时在 `46°–84°` 高纬引入极涡槽脊通道 `polarCarve`，避免极区被整块罩白或出现缺云空洞，并在赤道雨林（刚果、亚马逊）保留自然晴空裂隙；
   - 精确保护东边界冷海流海洋层积云区（`scProtect = smoothstep(.38, .74, marineSc)`），防止 Walker 沉降抑制项误抹平秘鲁/智利、加利福尼亚/巴哈与纳米比亚层积云，同时避免在 `13°–33°` 暖海区形成人工纬向云带；
   - 柔化台风外围 `radialRibs`（`cos(14θ)` → 扰动 `cos(6θ)`），消除低轨掠视下的硬放射条纹，同时 100% 保留 `79e6fa2` 的 CDO 灰斑修复。
2. **零额外采样 3D Voronoi 海洋开闭细胞层积云与步进连续性（[ScrollCloudSource.cginc](../../../Assets/ORBIS/Resources/ORBIS/FlatWorld/ScrollCloudSource.cginc)、[OrbisHPVolumeCloud.cginc](../../../Assets/ORBIS/Resources/ORBIS/OrbisHPVolumeCloud.cginc)）**
   - 直接复用已读取的 3D Voronoi 通道（`body.b` ~26 km 主胞、`erosion.r` ~14 km 次胞、`erosion.g` ~7 km 积云壁），在低轨实时雕刻**闭细胞鹅卵石云片**（饱满亮白多边形胞体 + 清晰深蓝胞间缝）与**开细胞花环 / POCs**（深蓝海面中心 + 蕾丝状积云壁），并以平滑权重在海洋层积云、锋后冷空气开细胞与信风积云间 C¹ 过渡；
   - 将海洋层积云垂直厚度由易产生低轨步进断层的 `420–1380 m` 调校至 `560–2200 m`，并恢复胞顶 3D 穹顶起伏（`1 - 0.25*sc`），彻底根除低轨平灰台阶与拉丝伪影（收敛 `anvilShear` 与细节风切倍率）；
   - 将 `OrbisHPVolumeCloud` 薄纱与厚核的硬 `max()` 改为连续概率并（`veilTerm + coreTerm * (1 - 0.52 * veilTerm)`），消除云缘灰白等值线台阶。
3. **薄云去灰、厚云立体阴影、云底透光与防剪白高光肩部（[OrbisAtmosphereCommon.cginc](../../../Assets/ORBIS/Resources/ORBIS/OrbisAtmosphereCommon.cginc)）**
   - 解耦 `cavityAO` 对低空阳面云顶的纯高度压暗：受阳薄云纱与 `1–2 km` 海洋层积云顶保持干净银白（消除 `薄云发灰`），而厚云背光侧与云底保留冷蓝大气阴影与多重散射**云底透光**；
   - 按光学厚度门控 `powder` 与 `denseGate`，并在云辐射率 `> 1.08` 处加入柔和膝部压缩（`knee`），在保留亮核反差的同时避免 `亮部剪白`。

---

## 2. 相同场景 `1280×720` `q=1` / `q=2` 性能对比（无并发负载）

| 场景 | 画质 | 改前 `79e6fa2` 总帧时 / 纯云通道 | 改后候选 总帧时 / 纯云通道 | 纯云通道变化 |
| :--- | :---: | :---: | :---: | :---: |
| **Pacific-Globe-Apr**（太平洋全盘） | `q=1` | `8.84 ms` / `5.59 ms` | **`7.85 ms` / `4.99 ms`** | **`-10.7%`（更快）** |
| **Pacific-Globe-Apr**（太平洋全盘） | `q=2` | `12.85 ms` / `9.45 ms` | **`11.44 ms` / `8.48 ms`** | **`-10.3%`（更快）** |
| **LowOrbit-Grazing-Borneo**（低轨掠视） | `q=1` | `9.90 ms` / `6.63 ms` | `10.21 ms` / **`6.53 ms`** | **`-1.5%`（更快）** |
| **LowOrbit-Grazing-Borneo**（低轨掠视） | `q=2` | `14.31 ms` / `10.95 ms` | **`14.14 ms` / `10.80 ms`** | **`-1.4%`（更快）** |
| **LowOrbit-Typhoon**（超强台风特写） | `q=1` | `11.75 ms` / `7.52 ms` | `12.98 ms` / `8.05 ms` | `+0.53 ms`（外海层积云多步采样） |
| **LowOrbit-Typhoon**（超强台风特写） | `q=2` | `17.71 ms` / `13.33 ms` | `19.64 ms` / `14.82 ms` | `+1.49 ms`（外海层积云多步采样） |

---

## 3. 全量回归验证结果

- **C# 核心气候、云量与气旋回归**：`FlatWorld.Core.Tests.dll` — **`PASS / 1,833,265 assertions`**（0 警告、0 错误，`17.3 s`）
- **原生 Windows D3D11 着色器变体编译**：`FlatSkyBake`、`HPCloudColumn`、`Clouds`（5 Pass）、`AtmosphereComposite`（2 Pass）、`WaterCloudProbe`、`CloudPassageProbe` 共 **`44 / 44 PASS`**（Linear + Gamma，`vs_5_0` + `ps_5_0`）
- **Unity 6000.0.55f1 D3D11 现有 GPU 回归（`4 / 4 PASS`）**：
  - `PASS ClimateCloudRegression.Run()`（含双向区域云响应、晴/薄/厚云雨影门控、干湿响应、穿云连续性探针）
  - `PASS HPCloudHeightRegression.Run()`
  - `PASS WaterCloudRegression.Run()`
  - `PASS LightningRegression.Run()`

---

## 4. 三栏对照图清单（改前 `79e6fa2` | 改后候选 | NASA 实拍）

| 文件 | 覆盖场景 | NASA 实拍来源与拍摄日期 |
| :--- | :--- | :--- |
| `compare_01_pacific_epic_apr.png` | 太平洋全盘（春分，东/中太平洋与北美） | NASA DSCOVR EPIC Natural Color · **`2024-04-15 20:38:51 UTC`** |
| `compare_02_pacific_epic_jul.png` | 太平洋全盘（夏至后，中/西太平洋与澳洲） | NASA DSCOVR EPIC Natural Color · **`2024-07-15 00:50:27 UTC`** |
| `compare_03_africa_southernocean_oct.png` | 非洲—印度洋—南大洋锋面与本格拉层积云 | NASA DSCOVR EPIC Natural Color · **`2024-10-14 09:50:40 UTC`** |
| `compare_04_south_atlantic_fronts_jan.png` | 南/北大西洋弯曲锋面、SACZ 与螺旋气旋 | NASA DSCOVR EPIC Natural Color · **`2025-01-15 14:37:22 UTC`** |
| `compare_05_peru_marine_sc_local.png` | 秘鲁/智利外海海洋开闭细胞层积云局部 | NASA Worldview / Suomi NPP VIIRS True Color · **`2024-10-12`** |
| `compare_06_low_orbit_grazing.png` | 低轨斜掠地平线、薄云纱到厚云立体过渡 | NASA ISS Expedition 40 (`ISS040-E-026890`) · **`2014-06-27`** |
| `compare_07_typhoon_closeup.png` | 五级超强台风近景（3D 体育场眼墙与零灰斑 CDO） | NASA Worldview / Suomi NPP VIIRS (Typhoon Hagibis) · **`2019-10-08`** |
| `compare_08_atlantic_hurricane_globe.png` | 自然步进加勒比大型飓风（Day 256.25, 45.2 m/s） | NASA ISS Expedition 7 (`ISS007-E-14858`, Hurricane Isabel) · **`2003-09-13`** |
