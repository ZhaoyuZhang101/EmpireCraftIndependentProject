# 来源、适配范围与许可记录

访问核对日期：2026-09-12（Asia/Taipei）。本次没有获得两个仓库的不可变commit SHA，故不编造版本锁定；下列链接使用当时可访问的main分支。交付的适配源码由本包SHA-256清单独立固定。没有把整个上游仓库、演示场景或贴图改名后宣称为原创工程。

## AshenOneArt / Unity-SWE

- 原仓库：https://github.com/AshenOneArt/Unity-SWE
- 实际核对代码：https://raw.githubusercontent.com/AshenOneArt/Unity-SWE/main/ShallowWater/Shaders/ShallowWaterSim.compute
- 上游说明：HDRP演示；InitWater、Advect、SolveDivergence、SolvePressure、ApplyBoundary、InjectWater、模糊与CalculateFoam等阶段；README声明MIT。
- 本包：`FlatShallowWater.cs`、`FlatLocalWater.cs` 为面向Built-in的算法适配，**未整份复制该compute shader**；采用共享面通量和供体限制的托管CPU局部扰动。上游GPU流水线、MacCormack速度平流以及完整边界/注水功能不能视为已原样接入。
- 署名保留 AshenOneArt。附 `Licenses/Unity-SWE-MIT-NOTICE.txt` 为基于上游README许可标识的标准MIT说明，不冒充从仓库不存在/未读取的LICENSE文件逐字复制所得的原文件。

## AshenOneArt / HPWater

- 原仓库：https://github.com/AshenOneArt/HPWater
- 源文件：https://raw.githubusercontent.com/AshenOneArt/HPWater/main/Core_Source_Preview/HanPiWater/Deferred/HPWaterVolumetrics.hlsl
- 光学说明：https://github.com/AshenOneArt/HPWater/wiki/BSDF%E2%80%90Model
- 许可：https://raw.githubusercontent.com/AshenOneArt/HPWater/main/LICENSE （MPL-2.0）
- 本包 `Assets/ORBIS/Resources/ORBIS/FlatWorld/Water/HPWaterOptics.cginc` 改写 `CaculateScatteredLight` 与 Fresnel-Schlick 的单段散射/消光/反射；保留 MPL-2.0 文件头、来源和修改说明。该文件源码完整提供，并附完整MPL2.0文本 `Licenses/HPWater-MPL-2.0.txt`。
- 修改：去除HDRP上下文、使用逐分量安全消光/反射、按水深/淡水/悬沙调用，不进行原上游多段体积步进与RenderGraph/MRT/屏幕空间折射链的整体移植。
- `FlatWater.cginc`、`FlatMap.shader` 和旧 `OrbisWater.cginc` 实际调用上述共享光学代码。原包已有的其他第三方声明与资源均保留。

## 分类与排水依据

- Beck et al. 2018, Present and future Köppen-Geiger climate classification maps at 1-km resolution：https://www.nature.com/articles/sdata2018214 。本项目使用月温/月雨分类规则，不包含其数据集，也不声称自己达到其观测/资料质量。
- Priority-Flood 原作者实现与说明：https://github.com/r-barnes/richdem 、https://github.com/r-barnes/Barnes2013-Depressions 。参考算法思想；没有复制其整套第三方库进Unity。
- HLSL关键字：https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-appendix-keywords
- HLSL step：https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-step
- Unity同步Pass编译：https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderUtil.CompilePass.html

作者自定义的湿地阈值、土壤水桶、地貌色码、汇流阈值、河宽/深与输沙扇形都在实现文档披露为游戏尺度近似，不挂靠上述论文冒充科学标定结果。
