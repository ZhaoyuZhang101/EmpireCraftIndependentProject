# ORBIS V4.2 GreenRingFix · TerrainPerf1

本轮目标是减少地图与地形更新造成的卡顿，并补齐基础地形编辑功能。工程从 **ORBIS_Unity_V4_2_Repair_IPCFix1_NightLighting_GreenRingFix_Full.zip** 直接复制后修改，没有使用 V4.3 或其他分支替换。

**交付状态：源码修改、离线结构检查、独立数值验证已完成；本环境没有 Unity Editor，尚未执行 Unity C# 编译、原生 HLSL 编译、Play Mode、实机帧率和视觉验收。请不要把离线报告中的 PASS 解释为这些测试已经通过。**

## 1. 使用哪个文件

完整包适合新目录验证：解压 `ORBIS_Unity_V4_2_GreenRingFix_TerrainPerf1_Full.zip`，在 Unity Hub 中添加解压出的工程根目录。工程沿用 **Unity 6000.0.47f1、Built-in Render Pipeline**，打开 `Assets/ORBIS/Scenes/OrbisDemo.unity`。没有新增 URP、HDRP、Burst 或额外付费包，也没有自动修改渲染管线。

补丁包仅适用于上述 GreenRingFix 完整版，不适用于先前的 NightLighting、IPCFix1、V4.2.4 或自行改过相关源码的工程。关闭目标工程的 Unity Editor，先备份项目及存档。优先用补丁包中的 `apply_patch.py` 进行逐文件哈希检查：

```bash
# 在解压后的补丁目录中执行；第一条只检查，不写入。
python apply_patch.py "/你的/ORBIS工程目录" --dry-run
python apply_patch.py "/你的/ORBIS工程目录" --apply
```

脚本先检查**全部**文件，遇到不匹配立即停止，不会偷偷把其他版本改造成目标版本。通过后会把被替换的旧文件备份到工程目录内的独立时间戳目录，再原子替换本次修改文件；不会删除 Library、ProjectSettings、场景、素材或存档。中断时可重新执行；已匹配新哈希的文件会被识别。不要将补丁再套用到本轮完整包上，也不要整目录删除旧 Assets 后再复制。

仍使用原来的 `orbis-demo-v4.json` 存档格式。首次验证建议先复制存档，因为新旧工程默认会使用同一产品的 persistentDataPath。旧版程序不认识新增的工具 6/7，因此**不要用旧版直接读取工具选中为 6/7 的新存档**；回退版本应同时回退存档备份。

## 2. 本轮性能修改

| 路径 | 原行为 / 问题 | 本轮实现 | 不应误解为 |
|---|---|---|---|
| 球面地形 | 每个粗三角形独立细分，重复采样共享边与顶点 | 在采样前使用规范化的边/角点编号，共享拓扑；仍保留 detail=8 和相同三角形数 | 降低地形分辨率，或减少 28.9% 的整帧耗时 |
| 渲染分块 | 自然分区 Revision 改变会销毁、重新创建渲染块 | 固定 6 个立方体面 × 4 × 4，共 96 个空间渲染块；网格和缓冲复用 | 把文明区块改成 96 个，或完整流式四叉树 |
| 可见性 | 自然区块和渲染分块耦合 | 复用相机视锥平面，结合保守地平线范围禁用不可见的球面块 | 已做硬件遮挡剔除、网格着色器或新剔除平台 |
| 地形编辑 | 地质底图反复进行板块、噪声与热松弛计算 | 缓存未编辑的地质底图；种子噪声对象或地质时钟改变时失效 | 跳过海岸、河网或自然分区更新 |
| 表面采样 | 所有位置都计算高频细节 | 海洋/低地细节权重为零时直接返回完全相同的结果；编辑后按关联地块标记重新采样 | 降低山地噪声或删除山脉 |
| 近地地形 | 镜头移动时同步完成整块高细节网格 | 双缓冲，生成新网格期间保留旧网格；最多每帧 768 个顶点，采样约 1.5 ms 软预算，缩放分级有迟滞 | 整帧保证不超过 1.5 ms；法线和最终网格发布仍同步 |
| 展开地图 | 周期刷新时对每个顶点反复计算地块颜色/气候/水体 | 共享每地块调色数据，固定地图拓扑，按 4096 顶点分片更新外观，完整后统一发布 | 所有数据都在 9 帧内从零生成；几何重建另有成本 |
| 展开地图云雾 | 原 MAP_VOLUME 全屏全分辨率积分 | 新增低分辨率 MRT_MAP_VOLUME 和独立合成通道；地形底图保持全分辨率 | 地形/文字也变糊，或整体 GPU 成本下降 75% |
| 暂停 | 无变化时继续刷新调色和上传天气贴图 | 暂停且世界/天气时钟不变时跳过；手动降雨、对流实验、地形编辑可强制更新 | 停止昼夜/天气的正常运行 |
| 内存 | 多余全局颜色缓冲、旧世界被编辑历史/闲置近地块引用 | 删除未使用缓冲；切换世界清理历史，失效近地块释放旧世界引用 | 全工程零 GC 或已完成内存压力实测 |

### 可复核的结构数字

保留 3,456 个逻辑地块、6,908 个粗三角形和 442,112 个细分三角形。独立 Python 拓扑重建得到源顶点由 **310,860** 降至 **221,058**，约减少 **28.89%**。96 个渲染块仍会在块边界复制顶点，因此实际分块顶点总和为 **232,624**，不能把 221,058 当作所有 GPU 网格的顶点总量。

固定拓扑检查中每条边恰好被两个三角形使用，欧拉示性数为 2，未发现开边或零面积/反向三角形；共享位置和随机插值场在双精度参考计算下误差小于 1e-12。这是数学与源码结构验证，不是 Unity 截图验收。

以 1920×1080、云雾中档为例，展开地图的积分缓冲为 960×540，即 **518,400** 个体积像素，原先为 **2,073,600**；只说明该积分通道像素数是原来的 25%。合成、地形、天气模拟、贴图上传仍有开销。最高云雾档或 `fullResolutionReference` 继续使用全分辨率参考路径；不支持 MRT/ARGBHalf 时也保留回退。

## 3. 地图与地形功能

左侧新增「地形工作台」。隆起、降低仍在原工具区；工作台新增平滑、整平、目标高度取样、可调半径与强度、独立近地网格档位、撤销/重做和性能记录。

**操作流程：**先选地块，按「取用所选高度」可以设定整平目标；选择隆起、降低、平滑或整平，再单击地图。工具按球面距离计算范围，因此不是以经纬图像素为半径，避免越靠近极点笔刷实际范围越小。拖拽仍用于转动/平移镜头；本轮是单击编辑，没有实现连续拖拽刷地或 3D 笔刷预览。

地形质量 48×48、64×64、96×96、128×128 是**近地局部网格的细分数**，独立于云雾质量；实际顶点数为 `(细分数+1)²`。默认使用 64×64，球面全局 detail=8 不随它改变。

平滑以本次笔刷开始前的高度快照求邻接均值，不依赖遍历先后。整平逐步趋近设定高程，而不是一次把范围内所有点强制截成绝对平面。目标 0 表示游戏海平面；正值为陆地高程，负值可降低到海面以下。地表仍包含程序细节与河床切割，**不是 CAD 平面工具，也不是实测米制 DEM 编辑器**。

球体和展开地图共用同一组地块、高程和河网。地图法线现在也使用与球体一致的河床切割结果，材质沿用球体的地形图集；几何与河网修订号一起检查，避免新世界或重新布置河网后沿用旧河床法线。球体地图转换和原有高度图/河网导入导出继续保留，没有附加任何真实地球地理数据。

### 撤销与数据保护

每次地形编辑自动暂停模拟，停止自动绕行/飞行。支持当前会话 **16 步**撤销/重做，记录编辑量、人口、归属、聚落标志和首府，因此撤销会一并还原本次地形淹水造成的人口迁移。撤销栈不写入存档；恢复模拟、换世界、载入/导入地图会清空。若其他操作已改变人口或归属，历史还原会拒绝覆盖这些新状态。

海岸、流向/流域和自然分区在编辑后重新计算，河流仍沿原来的统一河网渲染。对导入地图，编辑后会重新检查已有河线；若出现需要明显抬高水面的冲突，会回滚本次编辑并显示原因，避免用浮在山上的水面掩盖错误。

## 4. 绿色辉光修复保留范围

以下 9 个文件与原 GreenRingFix 包逐字节相同，哈希列于 `Documentation/TerrainPerf1/offline-validation.json`：

- `Runtime/OrbisNightLighting.cs`
- `Resources/ORBIS/OrbisNightLighting.cginc`
- `Resources/ORBIS/OrbisAtmosphereGLtracy.cginc`
- `Resources/ORBIS/OrbisAtmosphereCommon.cginc`
- `Resources/ORBIS/OrbisCloudCommon.cginc`
- `Resources/ORBIS/OrbisOptics.cginc`
- `Resources/ORBIS/Surface.shader`
- `Resources/ORBIS/OrbisWater.cginc`
- `Resources/ORBIS/RiverWater.shader`

没有关闭月光、微弱天光或 airglow，也没有通过把夜侧压成全黑来提升性能。Clouds 原来的 Pass 0/1/2 和 AtmosphereComposite 原来的 Pass 0 原文保留；只追加地图 MRT 和合成通道。新低分辨率地图路径仍调用同一个夜侧与大气积分实现，但上采样边缘和不同 GPU 的效果仍需实机目测。

原本的 IPC 修复中单一视线遍历、32 步上限的直接阴影回退和云采样预算保持不变。追加一个 Shader Pass 仍需要本机 UnityShaderCompiler 编译，不能据此保证任何原生编译器问题都已经解决。

## 5. 在 Unity 中验收

新菜单集中在 `Tools → ORBIS → TerrainPerf1`：

1. `1 Run isolated terrain data checks`：新建隔离的测试世界，不修改当前场景/世界；检查地块邻接、地形缓存、笔刷范围与异常参数、流向、存档新参数/旧默认值，以及示例导入河网。
2. `2 Compile current-API shaders and save diagnostics`：运行原生着色器验收，已更新为 4 个 Clouds Pass、2 个 Composite Pass；在每次导入/编译前保存检查点。仅针对当前 Editor 图形 API，不能代替其他平台测试。
3. `3 Inspect running terrain (read only)`：Play Mode 中读取顶点、分块、局部生成和编辑计数，不改场景。

数据测试报告写入 `Application.persistentDataPath/TerrainPerf1/production-data-validation.json`。着色器报告沿用原 IPC 诊断工具的输出路径，路径会在 Console 显示。

### 实机对比方法

先在旧工程与新工程分别使用相同种子或同一存档，固定窗口分辨率、云雾档位、视角、模拟速度、暂停状态和 VSync。原工程近地网格档位由云质量决定，比较近地细节时请让新工程的独立地形档位与它对应。不要把不同天气时刻、不同地形和不同摄像机距离的数字直接比较。

本轮 UI「记录帧时间」保存最多 1,800 帧的 `Time.unscaledDeltaTime`，跳过按下按钮前已经开始的间隔；达到上限自动停止。「导出 CSV/JSON」可以提前停止并把已有数据写到 persistentDataPath 下的 TerrainPerf1 文件夹，不会自动上传。JSON 包含均值、P50/P95/P99/最大帧间隔、设备/API/画质等元数据；CSV 保留每帧间隔和模式、海拔、局部生成状态。更换世界、修订地形、切图层/视图/质量/分辨率会标记 `contextChangedDuringCapture`，混合场景的摘要不能当作严格 A/B 结果。

记录的是**帧间隔**，包括等待、帧率限制及 Editor 开销，不是分离的 CPU/GPU 时间。要确认瓶颈请在 Unity Profiler 中看新增标记：

```text
ORBIS.Terrain.Geometry
ORBIS.Terrain.Palette
ORBIS.Terrain.LocalBuildSlice
ORBIS.Terrain.LocalPublish
ORBIS.Terrain.BrushCommit
ORBIS.Map.Geometry
ORBIS.Map.AppearanceSlice
```

旧工程没有本轮记录按钮，可用 Unity Profiler 对旧/新两版抓同等场景；不要将全部 TerrainPerf1 补丁套入旧版后再把它称为原始基线。

建议至少验收：轨道旋转、连续进出云层、近地平移和缩放、展开地图拖动、球/图切换、海岸附近编辑、极区/经度接缝附近编辑、撤销/重做、保存重载、示例地图河口，以及夜侧/月光/airglow。地图路径可切 `OrbisAtmosphereRenderer.fullResolutionReference` 对照是否出现上下翻转、地图边框漏光、细云模糊或夜侧环带差异。

## 6. 本次仍然同步执行的工作

首次世界生成、导入地图、全局排水/自然分区重建、河网重算、全局网格法线和上传仍可能造成尖峰。局部网格分片目前主要覆盖采样；最终法线计算、颜色填充和上传仍集中在发布帧。地图高程变化时也会重算其几何法线，不是完整局部瓦片流式系统。

本轮没有重写真实气候求解器、没有新增异步水文、没有采用 Jobs/Burst/GPU 地形生成，也没有完整块级多层 LOD/地形碰撞/导航系统。实际收益取决于机器上的 CPU、GPU 与运行场景，**没有提供捏造的帧率或“保证 60 FPS”的结论**。

## 7. 交付与复核

`Documentation/TerrainPerf1/offline-validation.json` 是本轮实际离线检查；`TERRAIN_PERF1_MANIFEST.json` 是逐文件变更与原文件哈希；`Validation/validate_terrain_perf1.py` 可以重新执行结构和独立数学检查。手动执行的 Unity 验收工具只表示已提供测试代码，不表示这些测试在交付环境中已经运行。

原包中的 `Documentation/V4*`、`Validation/validate_v4*`、Preview HTML 和旧 README 均作为历史资料保留，**不代表本轮源码的测试结果或实时演示**。一些历史检查写死旧 Pass 数量，直接运行可能对新增通道报预期不符；应使用本轮 TerrainPerf1 菜单与脚本。旧 Preview 不会执行本次 Unity C# 代码。

基线 ZIP SHA-256：

```text
34f6fb467ae0843ddcfe1d8e4b2fc8c70a86eb69f88e8942618e6fed5634fc23
```

API 核对来源为 Unity 6.0 官方文档：`GeometryUtility.CalculateFrustumPlanes` 的数组重用重载、`Mesh.GetNormals(List<Vector3>)`、`Texture2D.Apply`、`Time.unscaledDeltaTime` / `Time.realtimeSinceStartupAsDouble`。对应文档路径：

```text
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GeometryUtility.CalculateFrustumPlanes.html
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetNormals.html
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.Apply.html
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-unscaledDeltaTime.html
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-realtimeSinceStartupAsDouble.html
```
