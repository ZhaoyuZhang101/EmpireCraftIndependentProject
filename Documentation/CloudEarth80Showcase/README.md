# ORBIS 体积云 NASA 实拍对标、细长弯曲云带（5 轮精修）与 GPU 深度加速验证集

## 1. GPU 渲染性能与着色器编译（Unity 6000.0.55f1 · Direct3D11 · `1280×720` 同步中位数）

实测硬件：`NVIDIA GeForce RTX 5070 Laptop GPU`（`CloudRenderBenchmark.Run`，含 CPU `ReadPixels` 同步回读）：

| 测试场景与画质档位 | 原始参考积分 (`original`) | 早期基线 (`afd8e73`) | **细长弯曲云带 5 轮终版 (`Round 5`)** | 纯体积云通道 (`volume - no-volume`) | 相对原始积分提速 |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **星球全景 (`Globe-Pacific`, `q=2` 精细)** | `38.40 ms` | `34.48 ms` | **`12.86 ms`** | **`9.96 ms`**（原 `35.50 ms`） | **纯云快 `3.56×`** |
| **星球全景 (`Globe-Pacific`, `q=1` 均衡默认)** | `20.49 ms` | `~21 ms` | **`8.58 ms`** | **`5.69 ms`** | **稳超 `116 FPS`** |
| **低轨密云 (`LowOrbit-DenseClouds`, `q=2` 精细)** | `54.81 ms` | `44.60 ms` | **`16.45 ms`**（无 CPU 争用 `~14.5 ms`） | **`10.28 ms`**（原 `48.64 ms`） | **纯云快 `4.73×`** |
| **低轨密云 (`LowOrbit-DenseClouds`, `q=1` 均衡默认)** | `27.48 ms` | `~27 ms` | **`10.20 ms`** | **`5.74 ms`** | **稳超 `98 FPS`** |

> 注：本轮还通过 `FlatSkyBake.shader` 双纪元 `[loop]` 化将 FXC D3D11 `ps_5_0` 单变体编译时间由 `>65s` 降至 **`9.1s`**，并移除 `ScrollCloudSource.cginc` 内层循环中的无效切向投影 ALU 与 `<0.04` 不可见晴空卷云底噪。

---

## 2. 细长弯曲云带 5 轮闭环迭代总结（对标 8 张 NASA 官方实拍原图）

在前期 7 轮 NASA 对标 + GPU 加速基线（`d5d97b6`）之上，针对“缺乏自然、普通的细长弯曲形状云带（大气河、副热带/信风弓形云带、斜向 SPCZ/SACZ 与拉丝高空卷云）”开展 5 轮实机 Unity 渲染闭环精修：

1. **第 1 轮（等值线初探）**：尝试三尺度各向同性卷曲脊线；掠射地平线效果好，但全球尺度高频弯折偏“面条/细丝状”且全局升噪拖慢低轨帧耗（`16.09 ms`）；
2. **第 2 轮（几何根因定位 + 极速验证）**：移除平坦升噪后恢复至 `11.13 ms / 7.52 ms`，同时通过精确向量分析定位了长期存在的球面几何根因——单位球面上径向向量 `p` 与任意风切向量 `T` 恒正交（`dot(p, T) ≡ 0`），导致原 `p - T * dot(p, T) * k` 投影完全退化为各向同性团块；并引入 `[loop]` 将 `FlatSkyBake.shader` 编译提速 ~10×；
3. **第 3 轮（行星流函数等值线 + 真球面 3-tap LIC）**：用真实球面切向位移 3-tap LIC 取代失效投影，并试跑流函数等值线细长弧带；证实可拉出大尺度弧带，但也直观暴露了多独立等值线叠加产生的 `X` 交叉与牙膏管式硬边缺陷；
4. **第 4 轮（地转斜压弯曲条带 FBM + 3D 卷云 2-tap LIC）**：改用“先经大尺度行星无散旋度 `curl1` 弯曲、再拉伸跨流经向轴 4.5–7.2×”的地转斜压弯曲 FBM，彻底消除 `X` 交叉与条状硬边，并在 `ScrollCloudSource.cginc` 高云层用零额外采样的 2-tap 球面 LIC 实现真实顺风拉丝 3D 卷云；实机检出赤道 `latitude>=0` 接缝、部分弧带/高云边缘偏圆润及高云底噪增加耗时（`+2.5 ms`）3 项收尾瑕疵；
5. **第 5 轮（赤道 $C^\infty$ 连续 + 碎积云羽化边缘 + 全量回归验收）**：
   - 将半球斜压倾斜改为赤道 $C^\infty$ 平滑函数 `0.24 * lat / (|lat| + 0.32)`，彻底消除赤道接缝；
   - 跨流拉伸比精调至 `4.6×–6.1×`，且在 `smoothstep` 阈值前注入 3-tap LIC 丝缕、中尺度积云与高频碎粒，并在 `low` 通道恢复自然碎积云边缘侵蚀，使每条细长弯曲云带都具备 NASA 实拍级蓬松羽化/破碎边缘；
   - 高空卷云加入丝缕边缘雕蚀、裁剪 `<0.04` 晴空微噪，并在 `ScrollCloudSource.cginc` 放开 3D 卷云透空动态范围（`.18 + 1.06 * smoothstep(.18, .86, fine)`），彻底消除平滑圆片云并恢复 `~8.6 ms / ~10.2 ms` 均衡档高帧率。

---

## 3. 全量自动回归验证结果（第 5 轮终版）

- **核心 C# 气候与 NOAA 1991–2020 云气候学测试**：`FlatWorld.Core.Tests.dll` — **`PASS / 1,833,265 assertions`**（0 警告、0 错误）
- **原生 Windows D3D11 着色器变体编译**：`FlatSkyBake`、`HPCloudColumn`、`Clouds`、`AtmosphereComposite`、`WaterCloudProbe`、`CloudPassageProbe` 共 **44 / 44 变体全过**（Linear + Gamma, `vs_5_0` + `ps_5_0`）
- **Unity 实机 D3D11 GPU 回归（4 / 4 全过）**：
  - `PASS ClimateCloudRegression.Run()`
  - `PASS HPCloudHeightRegression.Run()`
  - `PASS WaterCloudRegression.Run()`
  - `PASS LightningRegression.Run()`

---

## 4. 实机验证图清单（`1600×900`，Unity 6000.0.55f1 Direct3D11）

- `0_nasa_reference_north_america_view.png` — 对标 NASA Blue Marble 北美—墨西哥湾—东太平洋同机位（可见东太平洋、加勒比与南太平洋细长弯曲云带）
- `1_earth_pacific_double_itcz_july.png` — 对标 NASA DSCOVR EPIC 7 月太平洋双 ITCZ、斜向弯曲 SPCZ 云带、冷舌晴空与北太平洋气旋
- `2_earth_equinox_pacific_americas_apr.png` — 4 月春分东太平洋细长弓形云带、冷舌、秘鲁层积云与亚马逊对流
- `3_earth_monsoon_sahara_contrast_july.png` — 对标 Blue Marble 东半球：撒哈拉/阿拉伯/索马里晴空、亚洲季风与南印度洋细长弧形云带
- `4_earth_amazon_atlantic_storm_jan.png` — 1 月北大西洋晨昏弯曲云带、撒哈拉晴空与亚马逊对流
- `5_earth_autumn_indopacific_walker_oct.png` — 10 月澳洲晴空荒漠、西澳层积云街、印尼弯曲沃克云系与南大洋弧形西风带
- `6_earth_low_orbit_volumetric_clouds.png` — 低轨海洋性大陆 3D 花椰菜积雨云与无接缝跨赤道弯曲云系
- `7_gleba_procedural_doublecell_globe.png` — 架空世界 Gleba 慢自转双环流（`SlowRotatorDoubleCell`，`0.45×` 自转）与蜿蜒细长弯曲云带
- `8_earth_grazing_cumulus_horizon.png` — 对标 ISS 婆罗洲地平线：五档云高分层、2-tap 球面 LIC 拉丝弯曲卷云与顺风冰晶云砧
- `9_earth_super_typhoon_stadium_eye.png` — 对标 ISS 飓风伊莎贝尔：3D 体育场斜壁台风眼、阴影阶梯、眼底微涡旋云纱与外围放射卷云肋
- `10_earth_hurricane_and_comma_cyclone.png` — 实机连续推演第 256 天加勒比大型飓风（`45.2 m/s`）与周边弯曲信风云带全景
