# ORBIS 体积云 7 轮 NASA 对标迭代与 GPU 深度加速验证集

## 1. GPU 渲染性能飞跃（Unity 6000.0.55f1 · Direct3D11 · `1280×720` 同步相机中位数）

实测硬件：`NVIDIA GeForce RTX 5070 Laptop GPU`（`CloudRenderBenchmark.Run`，含 CPU `ReadPixels` 同步回读）：

| 测试场景与画质档位 | 原始参考积分 (`original`) | 升级前基线 (`afd8e73`) | **7 轮终版 (`Round 7`)** | 纯体积云通道 (`volume - no-volume`) | 相对 `afd8e73` 提速 |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **星球全景 (`Globe-Pacific`, `q=2` 精细)** | `52.50 ms` | `34.48 ms` | **`12.09 ms`** | **`8.56 ms`**（原 `30.53 ms`） | **快 `3.57×`（降耗 `72.0%`）** |
| **星球全景 (`Globe-Pacific`, `q=1` 均衡默认)** | `23.83 ms` | `~21 ms` | **`8.62 ms`** | **`5.26 ms`** | **稳超 `115 FPS`** |
| **低轨密云 (`LowOrbit-DenseClouds`, `q=2` 精细)** | `79.77 ms` | `44.60 ms` | **`14.46 ms`** | **`10.02 ms`**（原 `39.38 ms`） | **快 `3.93×`（降耗 `74.6%`）** |
| **低轨密云 (`LowOrbit-DenseClouds`, `q=1` 均衡默认)** | `28.28 ms` | `~27 ms` | **`10.10 ms`** | **`5.81 ms`** | **稳超 `99 FPS`** |

### 核心性能优化项
- **晴空段零冗余体积采样**（`OrbisAtmosphereCommon.cginc`）：修复晴空包络探针未命中后仍调用 6 次 3D 噪声的缺陷；
- **单次采样 `simpleMode` 包络与光路探针**（`ScrollCloudSource.cginc`）：将占空探针与太阳阴影步进的 4 次 `128³` 体积读取压缩为 1 次，并跳过探针阶段的高云 3D 细丝读取；
- **昼半球月光/夜气辉全跳过 + 丁达尔空柱快筛**（`OrbisAtmosphereCommon.cginc`、`GeographicCoordinates.cginc`）：引入硬件 SFU `OrbisFastWeatherUV` 与单采样 `ScrollCloudGroundFast`，太阳直射半球跳过 `_AtmoOpticalDepth` 月光 LUT 与气辉计算，并提前将遮挡云体内部交由预积分柱缓存接管；
- **多档位步长与双边上采样解耦**（`OrbisAtmosphereRenderer.cs`）：运行时档位 `{0.56, 0.68, 0.80, 1.00}`、云步进 `{26, 36, 48, 64}`、光照步进 `{3, 4, 5, 8}` 与参考积分基线完全分离。

---

## 2. 对标 8 张 NASA 实拍原图的 7 轮体积云形态迭代总结

参考图库涵盖用户上传的 NASA Blue Marble 西半球、NASA DSCOVR EPIC L1 三大洋实时全盘照、Blue Marble 东半球、Apollo 17 以及 ISS 空间站婆罗洲积雨云砧与飓风伊莎贝尔眼墙特写：

1. **第 1 轮**：对齐 ISCCP 卫星光学厚度指数分布（薄纱云幕 `0.07–0.46` + 内嵌雪白对流白核 `0.9–1.8`）与流线拉伸（`3.5–6.5×`）；
2. **第 2 轮**：四大卫星云系形态专化（副热带东岸蜂窝层积云街、温带 4,500 km 弧形锋面与冷锋后开细胞鱼网碎积云、细长 ITCZ/SPCZ 对流热塔带、陆地地形积云）；
3. **第 3 轮**：引入**保方差双相位流场混合**（彻底消除 `phase≈0.5` 模糊发糊）、**顺风倾斜积雨云砧外悬（Anvil Overhang）**以及无赤道水平条纹的 3D 弯曲马尾卷云；
4. **第 4 轮**：全套体积云 GPU 渲染管线深度加速，实现纯云通道 `3.6×–3.9×` 提速且与前轮肉眼零画质差异；
5. **第 5 轮**：新增**高度与直射光耦合的云谷腔体自阴影遮蔽（Cavity AO）**与冷蓝天光补色，消除高光截断发平，拉出 3D 积雨云塔与台风眼墙深蓝立体阴影；同时在天气尺度叠加沿哈德莱-沃克-罗斯贝流线的各向异性拉丝形变；
6. **第 6 轮**：温带斜压气旋雕刻**卷入气旋核心的 1.5 圈对数螺旋干侵入晴空舌**，超强台风 12 km 深眼底加入 ISS 实拍级的**低空微涡旋眼底云纱（Hub Cloud）**；
7. **第 7 轮**：优化斜压锋面主干连续性（抑制高频点状打断），通过 `1,833,265` 项核心气候断言、全部原生 D3D11 着色器变体与 4 项 Unity GPU 回归，归档 11 张 `1600×900` 实机验证图。

---

## 3. 实机验证图清单（`1600×900`，Unity 6000.0.55f1 Direct3D11）

- `0_nasa_reference_north_america_view.png` — 对标 NASA Blue Marble 北美—墨西哥湾—东太平洋同机位
- `1_earth_pacific_double_itcz_july.png` — 对标 NASA DSCOVR EPIC 7 月太平洋双 ITCZ、冷舌晴空与北太平洋气旋
- `2_earth_equinox_pacific_americas_apr.png` — 4 月春分东太平洋冷舌、秘鲁层积云与亚马逊对流
- `3_earth_monsoon_sahara_contrast_july.png` — 对标 Blue Marble 东半球：撒哈拉/阿拉伯/索马里晴空 vs 亚洲季风
- `4_earth_amazon_atlantic_storm_jan.png` — 1 月北大西洋晨昏温带气旋与亚马逊对流
- `5_earth_autumn_indopacific_walker_oct.png` — 10 月澳洲晴空荒漠、西澳层积云街与南大洋咆哮西风带锋面
- `6_earth_low_orbit_volumetric_clouds.png` — 低轨海洋性大陆 3D 花椰菜积雨云与丝缕云砧
- `7_gleba_procedural_doublecell_globe.png` — 架空世界 Gleba 慢自转双环流（`SlowRotatorDoubleCell`，`0.45×` 自转）
- `8_earth_grazing_cumulus_horizon.png` — 对标 ISS 婆罗洲地平线：五档云高分层与顺风拉丝冰晶云砧
- `9_earth_super_typhoon_stadium_eye.png` — 对标 ISS 飓风伊莎贝尔：3D 体育场斜壁台风眼、阴影阶梯与眼底微涡旋云纱
- `10_earth_hurricane_and_comma_cyclone.png` — 实机连续推演第 256 天加勒比大型飓风（`45.2 m/s`）全景
