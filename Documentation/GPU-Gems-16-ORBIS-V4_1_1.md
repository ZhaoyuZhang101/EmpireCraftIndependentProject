# GPU Gems 2 第 16 章与 ORBIS 大气链核对

核对日期：2026-09-11。这里只报告已阅读参考、实际本地源码及本次热修复的边界；不宣称画面已与参考一致。

## 参考来源

1. Sean O’Neil，NVIDIA，GPU Gems 2，Chapter 16: Accurate Atmospheric Scattering。正文已读取：
   https://developer.nvidia.com/gpugems/gpugems2/part-ii-shading-lighting-and-shadows/chapter-16-accurate-atmospheric-scattering
2. 用户上传 `atmosphere.glsl`，文件头标注 Dimas Leenman，MIT。原文保存在 `Reference/atmosphere.glsl`，许可头保留。
3. Microsoft HLSL for 语句属性：
   https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-for
4. Microsoft HLSL 错误说明，ERR_CANT_UNROLL / 3511：
   https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/hlsl-errors-and-warnings
5. Microsoft tex3Dlod：
   https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-tex3dlod
6. Unity 6 ShaderUtil.CompilePass / IsPassCompiled：
   https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderUtil.CompilePass.html
   https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderUtil.IsPassCompiled.html

## 本地源码接线审计

| 要检查的关系 | ORBIS 实际位置 | 本次状态 |
|---|---|---|
| 密度随高度衰减 | `OrbisOptics.cginc / AirDensities` | 保留游戏尺度的 Rayleigh、Mie 和额外臭氧密度；未改为地球实测参数 |
| 太阳光先衰减再照亮地表／云／空气 | `AirSunTransmittance`；地表、水体、`IntegrateSegment` 调用 | 保留共用 RGB 太阳透射率；八步循环不再强制展开 |
| 外部相机从大气入口开始，内部相机从眼点开始 | `OrbisMarchRay` 的 `max(0, outer.x)` | 保留；新增独立 CPU 几何边界回归 |
| 射线遇可见地表／场景就截断 | `OrbisMarch` 的深度与球体交点，地图的 `_GroundField` | 保留，不让整个球背面加入前景积分 |
| 场景背景受到透射衰减再加散射 | `Clouds.shader` 和 `AtmosphereComposite.shader` | 保留 `scene * transmission + light`，不改为单纯加一层蓝光 |
| 显示变换只在最后执行 | `OrbisDisplay` | 保留 HDR 辐亮度合成后的统一显示路径 |
| 晨昏／夜面直射遮挡 | `OrbisSunVisible` 与 `AirSunTransmittance` | 保留径向起点偏移与行星遮挡；夜间极弱天空填充仍是游戏可读性近似 |
| 展开地图的大气语义 | `OrbisMarchMap` | 继续每个经纬度的径向大气柱，不把二维平面伪装成球体天空透视 |

这些关系多数在 V4.1 已存在。V4.1.1 的新增实现是循环／数据存储重构和检查工具，不把既有功能重新宣称为本次首次接入。

## 为什么不直接照搬示例的快速拟合

NVIDIA 第 16.4.2 节指出拟合与大气厚度比例、尺度高度有关；示例厚度为行星半径的 2.5%。本地代码地表半径 1.0、大气半径 1.20，厚度是 20%，两者不是同一标定。因此本次保留数值积分，没有复制示例的拟合常数。[1]

从本地代码可直接算出，Rayleigh 尺度高度 0.025 约占本地大气厚度的 12.5%；Mie 另用 0.006。这些参数、云底 1.085 和云顶 1.132 都是本工程的艺术化游戏尺度，而不是米制地球。

相函数也不能只复制 `g` 的正负号。当前代码采用 `mu=dot(camera_to_sample, sample_to_sun)`，分母含 `1+g*g-2*g*mu`。在这一定义下，正 g 使朝太阳看的方向更亮。若把方向向量反过来，符号约定必须同步变化；应核对公式，不以代码注释中的“前向”文字代替检查。

## 上传 GLSL 的对应关系

上传文件已含相机／大气球相交、场景深度上限、Rayleigh／Mie、臭氧吸收、RGB 透射和末端曝光。它的 `mainImage`、演示相机、绿色球体以及固定米制常量不是可直接挂到 Unity 内置管线的 Shader。原文件内还有针对示例场景的 `allow_mie` 开关；不宜未经判断就用它把所有前景空气的 Mie 散射关掉。

本地实现保留独立 HLSL 接口和自己的云／空气有序积分，未将上传源码改名为 `.shader` 冒充移植。参考副本放在 Assets 外，仅供追溯。

## 热修复不包含的内容

没有新增空气多重散射 LUT、完整 O’Neil 快速拟合、自动曝光、生产级时域重投影、地球参数预设或预先认可的夜景／地平线画面。以后进一步改变大气外观，应单独比较相同天气、太阳、相机、曝光下的结果，避免把“编译修复”与“画风重调”混为一谈。

本轮保留单散射和已有简化天空补光，也保留已注明范围的 HanPi-inspired 云漫射近似。没有声明把云近似变成了 NVIDIA 大气的完整多重散射模型。
