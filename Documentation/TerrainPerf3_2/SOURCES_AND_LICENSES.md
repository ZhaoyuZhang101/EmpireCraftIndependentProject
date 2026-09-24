# Perf3.2 参考项目、实际接入和许可范围

核对日期：2026-09-12。下列是原作者仓库/教程/官方 API。新版本没有获取不可变上游 commit SHA；不虚构版本锁定。本包自己的源码版本由 SHA-256 清单固定。基础包的其他第三方声明全部保留。

## 实际保留并继续调用的适配

**AshenOneArt / HPWater** — https://github.com/AshenOneArt/HPWater 。上游说明是 HDRP 核心源码预览，包含水体光学和渲染阶段，不能把源码预览称为本包已导入的完整工程。Perf3.1 中的 `HPWaterOptics.cginc` 按其单段散射/消光和 Schlick 函数适配，Perf3.2 不修改该文件，新的 FlatWater/Surface 继续实际调用。没有移植完整 GBuffer/MRT、SSR、屏幕空间折射、RenderGraph、焦散和水下体积流水线。来源/修改头保留；MPL-2.0 全文在 `../TerrainPerf3_1/Licenses/HPWater-MPL-2.0.txt`，适配源文件完整随包提供。

**AshenOneArt / Unity-SWE** — https://github.com/AshenOneArt/Unity-SWE 。上游为 HDRP+Compute 的高度场浅水示例，README 列出通量守恒、边界条件与外力，并提醒 CFL/步长仍影响稳定性。本包保留 Perf3.1 改为托管 CPU 单可见区扰动的路线；Perf3.2 修正可见湿区与曲线河岸、法线编码和着色衔接。没有把上游 GPU MacCormack、注水捕获和全部 HDRP 阶段原样搬来。README 许可标识为 MIT；已有署名及说明在 `../TerrainPerf3_1/Licenses/Unity-SWE-MIT-NOTICE.txt`，其性质为基于 README 许可标识的说明而非虚构原仓库 LICENSE 文件。

## 设计/数学参考，未复制项目代码或资产

**Catlike Coding: Curves and Splines** — https://catlikecoding.com/unity/tutorials/curves-and-splines/ 。参考二次贝塞尔、切线和曲线采样思路。本版自行写 CPU/HLSL 双实现，固定在已有排水连接的格内，不采用教程旧 Unity 编辑器脚本。单调场重建与河边共享截面不是声称来自这个教程的现成模块。

**Azgaar Fantasy Map Generator** — https://github.com/azgaar/fantasy-map-generator ，地形生成流程文档 https://github.com/azgaar/fantasy-map-generator/blob/master/docs/domain/generation_pipeline.md 。参考把地形、水文、气候、生物群系、聚落分层处理的工程组织；本包不导入其 JS/Delaunay 地图，不替换 WorldData，也不把地图绘制模块冒充 Unity 原生实现。

**Terrain3D** — https://github.com/TokisanGames/Terrain3D 。Godot 地形项目，可参考可编辑地形、材质/区域组织与细节分层，但不是 Unity 插件。本版没有集成其 GDExtension、碰撞系统、植被实例或资产，不把本次色彩混合称为“已移植 Terrain3D”。

**Red Blob Games: Terrain from Noise** — https://www.redblobgames.com/maps/terrain-from-noise/ 。参考连续地形场、地形与气候/群系分工。本版沿用原地形生成器而没有声称实现新的复杂侵蚀或真实气候。

## 官方编译与采样依据

Unity 6.0 采样器文档：https://docs.unity3d.com/6000.0/Documentation/Manual/SL-SamplerStates.html 。声明/共享采样器和目标后端约束需要结合实际编译验证；本包只据源码计数，不把该计数当成 IPC 唯一根因。

同步请求编译 Pass：https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderUtil.CompilePass.html 。检查 Pass 状态：https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderUtil.IsPassCompiled.html 。本机测试入口使用这些 API 编译实际当前图形 API 的默认材质 Pass；不能以无图形批处理或 GLSL 翻译结果代替 D3D11。

这些资料的作用与未接入范围都已逐项区分：本次实际代码整合的是本项目的连续场、曲线水域、渲染缓存及原适配光学/浅水求解；参考项目的名称不代表其所有功能已存在于 Perf3.2。
