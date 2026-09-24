# V4.1.2 — atmosphere.glsl 的实际移植与接线审计

## 来源与可追溯性

用户上传 `atmosphere.glsl`，22,624 bytes，SHA-256：
`708ea4a9141a7653ea0085d7f26ae09ca09bb14c88e339d63da74e16439300bd`。

原文 MIT License，Copyright (c) 2019 Dimas Leenman；原文保存在 `Reference`，活动 HLSL 和独立 HTML 均保留署名／许可。不是将其它网页的相似公式冒称用户上传文件。活动代码来自该文件公式的适配，但不是逐字、逐像素等价移植。

基线完整包 `ORBIS_Unity_V4_1_1_CloudLoopFix.zip`：5,696,440 bytes；SHA-256 `f3f18d485beeea2c8103734d0d2b24843d9e302ec11d2209d35c914bd0a515ee`。

## 函数级对应

| 上传源码 | 活动实现 | 实際调用 |
|---|---|---|
| `calculate_scattering` 中指数 Rayleigh/Mie 密度 | `OrbisGLSLDensity` | `AirDensities` → 云／空气主积分，太阳光路 |
| `density.z = density.x / (denom²+1)` | 同函数中的 rational ozone | 真实消光，不再使用此前 Gaussian ozone |
| `phase_ray`、`phase_mie` | `OrbisGLSLPhase` | `IntegrateSegment` → 空气源项 |
| 相机、外层球体与 `max_dist` | `OrbisGLSLViewInterval` | `OrbisMarchRay`，球体和地图 |
| `opt_l`、`attn` | `OrbisGLSLSunTransmittance` | 地表／水体太阳光，云／空气照明 |
| 相位×系数×散射总量 | `OrbisGLSLSource` | 主积分中的空气源项，继续乘云影 |
| 累加光学深度与背景透射 | `OrbisGLSLAccumulate`、原有 MRT 合成 | 以云＋空气联合消光实现每段稳定 Beer 积分 |
| `render_scene` 中的太阳圆盘 | `OrbisBackgroundWithSun` | 全分辨率和 MRT 最终合成，真实深度遮挡 |

完整接线：`OrbisAtmosphereProfile.Bind` → 材质光学参数 → `OrbisOptics.cginc` → 活动 `OrbisAtmosphereGLSL.cginc`；`Clouds.shader` 三个 Pass 调用共享积分。`Surface.shader`、`RiverWater.shader` 通过共享 Optics 接入新的太阳透射率，无需复制一份不同公式。地图继续是每个经纬度上方的径向大气柱，不是在平面上粘一颗太阳。

旧 `AirDensities`、`AirExtinction`、`AirSunTransmittance` 是兼容入口，其函数体明确委托新移植，旧密度公式已移除。这不是同时开启两套天空。

## 为现有项目做的改写

原文件地球半径 6371 km、100 km 厚大气。本游戏地形起伏及云壳远大于真实比例；直接把 100 km / 6371 km 的大气顶套入，云层会落在大气外。因此保持现有 1R／1.20R 和云壳，用 S=100000/0.20=500000 米／游戏 R 转换“光学长度”，不是重新定义世界真实半径。

设参考系数 β（每米），高度 H（米），高度伸展 q：游戏系数 β'=βS/q，尺度高度 H'=Hq/S。因此 β'H'=βH；这是未截断、平面垂直指数柱的积分量，不保证弯曲光路、有限球壳或最终 RGB 与参考完全一致。

默认 Rayleigh q=2，系数 (1.375,3.25,5.6)/R，高度 .032R；Mie q=3.3333，系数 3.15/R，高度 .008R。吸收系数 (10.2,24.85,.975)/R，峰值参数 .06R，宽度 .008R；它仍乘 Rayleigh 密度，因高度适配也会改变其分布。大气顶 .188–.20R 平滑截断，密度函数不随相机位于内外而切换。

Mie 使用上传文件的 Cornette–Shanks 表达式；rd 指相机到采样点、sun 指采样点到太阳，在这个约定下正 g=.7 为前向峰。不能直接照抄其它文章不同角度约定下的负 g。

太阳光路在最近掠点处拆成两段，用平方分布节点集中采样近地部分，单侧不超过 24 次，循环是 `[loop]`。四档光路预算 12/16/20/24；因分段取整和每侧至少 4 个节点，预算不是严格总次数上限。视线空气预算 16/24/32/48、每段至少 6；云主采样和云光照档位保留。增加光路采样会提高运算成本，未测硬件帧率。

200 个固定高度／太阳角度组合，相对同几何的 1024 预算双精度参考：16 预算最大 RGB 透射绝对误差约 .01737（不是百分比图像误差）；12 预算约 .03920，24 预算约 .00337。这个误差只是选定样本集的数值差，不是全角度最坏误差上界。

视线积分用同一采样点的空气与云消光总和，在每个小段内作常系数精确 Beer 积分；小 tau 用多项式避免消减相消。这有意替换原文“先加完整步长光学深度再计源项”的离散方式，避免对当前采样点多衰减半步。

保留现有 `OrbisSunVisible` 实体星球遮挡。没有照搬上传示例中遇物体就把 Mie 设零的 `allow_mie`：前景仍应有路径内气溶胶散射，遮挡由实际深度和球体交点处理。太阳圆盘作为背景乘 T，空气散射加 L；显示仍只做一次旧项目的曝光／ACES 近似。

原示例空气光源强度 40、`1-exp(-color)` 显示；当前空气 `solarRadiance` 默认 12，保留原项目 ACES 显示以适配旧地表／水体的亮度尺度。没有承诺与原 Shadertoy 的曝光和颜色逐像素一致。

## 没有移植或没有改变的部分

未移植原文 `mainImage` 的相机、绿色球体、`skylight` 二次天空采样与测试时间轴；没有把示例场景替换游戏世界。现有可读性天空补光、近似云间接散射和云影继续存在，不能称为严格的全阶多重散射求解。

保留 V4.1.1 四交点比较排序、五段 `[loop]` 主积分以及无动态数组的云光学厚度存储。没有为实现进出重新启用复杂嵌套 `[unroll]`。最终合成中的固定 2×2 采样展开仍存在，它不包含体积积分。

地形、云层、文明、河道不会因观察者进出而重置。地图不是相机穿透地表后强制弹出的另一场景；它仍由原有地图按钮切换。旧存档新增的仰俯角／朝向有缺字段默认值；原生加载尚待实机确认。

## 测试的含义与边界

`Validation-V4_1_2.json`：11 组源码／独立 CPU 数值检查，通过。含 4000 个随机射线区间，15 对界面两侧的晴空参考积分（±10⁻⁶R），相位归一化，零密度／夜面，稳定 Beer 和 UI／保存连接。晴空连续性检查不是 Unity 云图像帧间连续性验证。

`GLES-Core-Execution-V4_1_2.json`：机械转译 GLSL ES 在 Mesa llvmpipe 执行 401 个 RGBA32F 读回样本，和相同节点的 Python float64 比较，最大绝对误差 4.41×10⁻⁶。它验证转译核心的实际着色器执行，不验证 Unity HLSL 后端、材质绑定、深度纹理翻转、C# 或场景。

`GLES-Preview-Render-V4_1_2.json`：GLSL ES 预览编译、链接并渲染。`Preview-Browser-V4_1_2.json`：Chromium 桌面／手机尺寸 DOM 无横向溢出或未捕获 JS 错误，三张静态回退可切；当前 WebGL context 不可用，未验证浏览器实时路径。预览不读原 Unity 网格／天气，是光学观察工具。

重跑旧 V4.1.1：9/9。重跑旧 V4.1：16/17，唯一失败是旧内联 `*sunT*airShadow*` 文本断言；新写法为 `OrbisGLSLSource(rho,phaseAir,sunT)*airShadow`。原测试和失败报告保留；新 `shared_render_wiring` 显式检查空气源项云影。不要把旧测试的数学结果当作新物理参数的验证。

当前无 Unity Editor，CLI 安装尝试因域名解析失败；未进行 Unity 导入、原生 C#／HLSL 编译、Play、构建、硬件性能测试。菜单 `Tools > ORBIS > V4.1.2 > 強制編譯雲與大氣並輸出報告` 供本机请求编译；报告必须实际执行后才产生。

复现独立检查：

```bash
python -m pip install numpy pillow
python Validation/validate_v412.py
# 以下两项需系统可用 EGL / OpenGL ES3 / Mesa；不安装 Unity，也不验证 Unity。
python Validation/execute_gles_core.py
python Validation/gles_preview_renderer.py
```

执行报告和本地生成图片可能改写同名检查输出；记录运行环境，勿与交付时报告混淆。

## 外部原理参考

[1] Sean O’Neil, GPU Gems 2, Chapter 16 — 相机内外球体裁剪、沿途散射与背景透射。本次没有移植其拟合优化、LUT 或整份 Cg 示例。
https://developer.nvidia.com/gpugems/gpugems2/part-ii-shading-lighting-and-shadows/chapter-16-accurate-atmospheric-scattering

[2] Microsoft HLSL `for` — `[loop]` 与 `[unroll]` 的控制语义；保持复杂采样体使用 rolled flow control。
https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-for

[3] Unity 6000.0 `OnRenderImage` — 本工程仍使用 Built-in 的图像效果入口，不是 URP/HDRP Renderer Feature。
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html

访问核对：2026-09-11。用户上传源码是本次移植的第一来源；NVIDIA 为辅助关系核对，未以网页参考代替上传文件。
