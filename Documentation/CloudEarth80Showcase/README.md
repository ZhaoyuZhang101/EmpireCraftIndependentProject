# ORBIS 体积云 NASA DSCOVR EPIC L1 八轮闭环重构与 GPU 深度加速验证集

## 1. GPU 渲染性能（Unity 6000.0.55f1 · Direct3D11 · `1280×720` 无争用同步中位数）

实测硬件：`NVIDIA GeForce RTX 5070 Laptop GPU`（`CloudRenderBenchmark.Run`，含 CPU `ReadPixels` 同步回读）：

| 测试场景与画质档位 | 原始参考积分 (`original`) | **EPIC 8 轮终版 (`Round 8`)** | 纯体积云通道 (`volume - no-volume`) | 相对原始积分提速 |
| :--- | :---: | :---: | :---: | :---: |
| **星球全景 (`Globe-Pacific`, `q=2` 精细)** | `49.74 ms` | **`14.62 ms`** | **`11.82 ms`**（原 `46.94 ms`） | **纯云快 `3.97×`** |
| **星球全景 (`Globe-Pacific`, `q=1` 均衡默认)** | `25.97 ms` | **`9.33 ms`** | **`6.44 ms`** | **稳超 `107 FPS`** |
| **低轨密云 (`LowOrbit-DenseClouds`, `q=2` 精细)** | `49.92 ms` | **`14.10 ms`** | **`10.09 ms`**（原 `45.91 ms`） | **纯云快 `4.55×`** |
| **低轨密云 (`LowOrbit-DenseClouds`, `q=1` 均衡默认)** | `22.84 ms` | **`9.98 ms`** | **`5.81 ms`** | **稳超 `100 FPS`** |

---

## 2. 对标 NASA DSCOVR EPIC L1 太平洋实拍的 8 轮“去头屑片状”闭环迭代

针对此前云层偏“二值硬切头屑片状、中尺度碎块分散、缺少半透明云雾纱裙与 15–45 km 细粒海洋细胞云网”的真实性缺陷，新增与 `epic_pacific.jpg` **1:1 同机位**验证视角 `epic_pacific_match.png`（`lon=-126°, lat=-6°`），连续完成 8 轮 Unity D3D11 实机渲染闭环迭代：

1. **第 1 轮（打破 0.11 二值硬切悬崖）**：重构 `OrbisHPVolumeCloud.cginc` 与 `ScrollCloudSource.cginc` 为双阶水汽曲线（宽过渡半透明 `gauzeVeil` 纱裙 + 内嵌雪白 `billowy` 对流核），消除纸片剪切边缘；
2. **第 2 轮（根除 `FlatSkyBake` 二值压缩 + 流体涡旋平流）**：将 `FlatSkyBake.shader` 从陡峭 `9.8×` Logistic + `k=38` 大洞侵蚀重构为**两级无散球面旋度平流（奶油入咖啡式流体卷曲）+ 连续三阶水汽分布（薄雾纱 `0.06–0.44` → 连贯云体 `0.35–0.76` → 雪白厚核 `0.78–1.0`）**，二值头屑片状形态首次彻底消退；
3. **第 3 轮（零额外采样 3D 细粒海洋细胞云网）**：在 `ScrollCloudSource.cginc` 中将高频三维细节提升至 `k=112`（`20–45 km`），以零额外纹理读取为海洋层积云、信风积云与冷锋尾迹生成细密细胞泡沫与开细胞鱼网结构；
4. **第 4 轮（可见中纬多圈螺旋气旋带 + 破除极区纯白盖子）**：将南北斜压风暴带拉回 `40°–48°` 可见中纬，强化 `2.4` 圈逗点螺旋气旋、`5,500 km` 弧形锋面云幕与冷锋后鱼网尾迹，并用螺旋干侵入舌自然劈开高纬纯白盖子；
5. **第 5 轮（细长蜿蜒 ITCZ、斜向 SPCZ 与双倍频锋面拉丝）**：强化赤道窄长蜿蜒 ITCZ 珠链与 NW→SE 斜向 SPCZ 多股丝缕云带，并将锋面纹理升级为双倍频 3-tap LIC 拉丝纤维；
6. **第 6 轮（解除海洋细胞网连乘衰减 + 多股拉丝高空卷云帘）**：修复 `marineTapestry` 三项因子连乘衰减问题，让中太平洋、秘鲁与巴哈外海清晰呈现出 EPIC 标志性的超细 `15–40 km` 细粒海洋细胞泡沫网与顺风拉丝卷云帘；
7. **第 7 轮（高光厚核反差 `corePop` + 晴空裁剪与低轨高塔柔化）**：在 `OrbisAtmosphereCommon.cginc` 中为阳面高空厚核加入雪白提亮 `corePop`（拉大薄雾纱裙与雪白核心动态反差），收紧墨西哥湾与干区卷云裁剪，柔化低轨斜切积雨云塔；
8. **第 8 轮（终版微调与全量回归验收）**：微调极缘干舌裂隙与薄雾上限，以全量 12 张 `1600×900` 实机成图完成 8 成以上 EPIC 实拍神似度收官。

---

## 3. 全量自动回归验证结果（EPIC 第 8 轮终版）

- **核心 C# 气候与 NOAA 1991–2020 云气候学测试**：`FlatWorld.Core.Tests.dll` — **`PASS / 1,833,265 assertions`**（0 警告、0 错误）
- **原生 Windows D3D11 着色器变体编译**：`FlatSkyBake`、`HPCloudColumn`、`Clouds`、`AtmosphereComposite`、`WaterCloudProbe`、`CloudPassageProbe` 共 **44 / 44 变体全过**（Linear + Gamma, `vs_5_0` + `ps_5_0`）
- **Unity 实机 D3D11 GPU 回归（4 / 4 全过）**：
  - `PASS ClimateCloudRegression.Run()`
  - `PASS HPCloudHeightRegression.Run()`
  - `PASS WaterCloudRegression.Run()`
  - `PASS LightningRegression.Run()`

---

## 4. 实机验证图清单（12 张 `1600×900`，Unity 6000.0.55f1 Direct3D11）

- `epic_pacific_match.png` — **1:1 同机位对标 NASA DSCOVR EPIC L1 太平洋实拍**（`lon=-126°, lat=-6°`）
- `0_nasa_reference_north_america_view.png` — 对标 NASA Blue Marble 北美—墨西哥湾—东太平洋同机位
- `1_earth_pacific_double_itcz_july.png` — 7 月太平洋双 ITCZ、斜向弯曲 SPCZ、冷舌晴空与北太平洋螺旋气旋
- `2_earth_equinox_pacific_americas_apr.png` — 4 月春分东太平洋细粒海洋细胞云网、弓形云带与亚马逊对流
- `3_earth_monsoon_sahara_contrast_july.png` — 对标 Blue Marble 东半球：撒哈拉/阿拉伯/索马里晴空、亚洲季风与南印度洋平行弧带
- `4_earth_amazon_atlantic_storm_jan.png` — 1 月北大西洋晨昏温带锋面、信风云系与撒哈拉晴空
- `5_earth_autumn_indopacific_walker_oct.png` — 10 月澳洲内陆晴空、西澳蜂窝云街、印尼沃克对流与南大洋螺旋西风带
- `6_earth_low_orbit_volumetric_clouds.png` — 西澳印度洋低轨俯视：弯曲细粒海洋积云链、澳洲晴空荒漠与印尼立体积雨云
- `7_gleba_procedural_doublecell_globe.png` — 架空世界 Gleba 慢自转双环流（`SlowRotatorDoubleCell`，`0.45×` 自转）
- `8_earth_grazing_cumulus_horizon.png` — 东北亚地平线掠射： sweeping 丝缕锋面云盾、五档云高分层与渤海/黄海/朝鲜半岛晴空
- `9_earth_super_typhoon_stadium_eye.png` — 对标 ISS 飓风伊莎贝尔：东海超强台风 3D 体育场斜壁眼墙、眼底云纱与放射丝缕卷云肋
- `10_earth_hurricane_and_comma_cyclone.png` — 实机自然演进第 256 天加勒比大型飓风（`45.2 m/s`）与大西洋云系全景
