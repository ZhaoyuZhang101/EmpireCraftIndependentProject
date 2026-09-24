# ORBIS V4.2 Repair — 云层编译 IPC 修复补丁 1

## 先看结论与验证边界

本包直接以 **ORBIS_Unity_V4_2_Repair_GLtracy_HP.zip** 为基底修改，未切换到 V4.2.4、V4.3 或其它工程。原工程目录仍为 `ORBIS_Unity_V4_2_Repair`，`ProjectVersion.txt` 仍为 `6000.0.47f1`，保留 Built-in Render Pipeline。

已完成源码结构修复、离线回归与打包。**本次没有运行 Unity Editor、UnityShaderCompiler、独立 HLSL 编译器或 C# 编译器；不能声称已经在用户电脑复现并消除了 IPC 崩溃。** 没有实测 FPS，也没有进行画面对比验收。所有离线 PASS 仅对应报告列出的检查。

原包 SHA-256：

```text
081d7de7dbbc0f37e90eaa5be941c896f8fc418fa5bf72e83d8aba9afe651227
```

## 安装：已有 Repair 工程

1. 退出 Unity，备份当前工程。确认这是上述 V4.2 Repair，不是其它带相似名字的版本。
2. 解压补丁，将 `Assets` 中的内容按同名路径**合并覆盖**到工程的 `Assets`。不要把整个 Assets 目录删除后替换。将补丁的 `Documentation`、`Validation`、`Tools` 与根目录说明也合并过去。保留原来的 `.meta`；补丁只为新编辑器工具带有一个新 `.meta`。
3. 在 Unity 已退出的状态下，若存在 `Library/ShaderCache`，将它重命名备份，例如 `ShaderCache.before_IPCFix1`；同名备份已存在时使用其它名字。不要删除整个 Library，不要修改 Assets、Packages、ProjectSettings。重新打开后 Shader 会重新编译。完整包解压成新工程通常没有这个旧缓存，无须此步骤。
4. 打开工程，运行菜单 **Tools → ORBIS → V4.2 Repair → 编译云层并保存诊断**。然后打开原场景 `Assets/ORBIS/Scenes/OrbisDemo.unity`，检查球体、进出大气与展开地图。编译检查与画面检查是两回事。

也可以用下面的可选安装器，先检查版本与文件哈希，再写入。需要 Python 3.9+；不使用它也可按上述方法合并文件。

```text
python Tools/apply_v42_repair_ipc_patch.py "D:/Your_ORBIS_V4_2_Repair"
python Tools/apply_v42_repair_ipc_patch.py "D:/Your_ORBIS_V4_2_Repair" --apply
```

第一条只预检、不修改。第二条确认四个 Shader 源文件是此 Repair 基底后，先备份再覆盖；遇到不同版本或自行修改的 Shader 会拒绝安装。安装器不清缓存、不启动 Unity，也不代表编译验收完成。请从解压后的补丁目录执行，目标参数填写原工程根目录。

## 安装：直接使用完整工程

将完整 ZIP 解压到新目录，在 Unity Hub 中添加其中包含 `Assets / Packages / ProjectSettings` 的 `ORBIS_Unity_V4_2_Repair` 目录。工程声明的编辑器版本仍为 `6000.0.47f1`。完整包已经含有此补丁，不需要再覆盖一次。然后执行上面的诊断菜单与场景检查。

## 为什么没有只改 unroll 或清缓存

报错锁定 `ORBIS/Clouds` 的 `MRT_VOLUME` 片元阶段，表示与着色器编译子进程的通信失败。相同错误码可能有不同原因，不能仅凭它认定是网络问题、显卡损坏或特定的 Unity Bug。Unity 官方已有 FXC 编译器崩溃产生相同错误的案例，但并不证明本工程就是那个案例。

这个 Repair 基底原本已经有 `[loop]`。本次源码审查发现的高风险结构是：视线分段循环中包含完整步进循环；步进里有重复的阴影调用；阴影函数内部又有两个重型直接追踪回退调用点，每个回退再嵌套两层采样循环。它们会增加编译器处理的重复函数体与控制流。**这是本补丁针对的结构性风险，不是已通过崩溃转储确证的根因。**

## 实际代码修改

| 文件 | 修改 | 保留内容 |
|---|---|---|
| `OrbisAtmosphereCommon.cginc` | 将“5 个分段 × 分段内步进”改成一个前后顺序不变的状态式循环；采样函数不再接收循环中的 `inout OrbisVolumeResult`；同一位置的云阴影只计算一次 | 分段边界、中点采样、原质量预算、透射率提前退出、HP-inspired 近似多重散射、调试模式 |
| `OrbisCloudCommon.cginc` | 直接阴影的 `2 × 16` 两层循环改为单层 32 次有界循环；缺少缓存与远侧云壳共用一个回退调用点；4 个闪电槽改用常量索引 | 两侧云壳、原 16+16 个节点、真实密度采样、无缓存直接追踪、阴影图集 |
| `OrbisAtmosphereGLtracy.cginc` | 仅为备用 8 步光学积分增加 `[fastopt]` | 用户提供的 GLtracy 大气实现、光学 LUT、散射参数及数学公式 |
| `Clouds.shader` | 更新版本注释以标识补丁 | 原三个 Pass、Shader 名称、`target 3.5`、MRT 输出、顶点与片元接口完全保留 |

重型循环使用 `[loop] [fastopt]`。微软文档将 `fastopt` 定义为减少循环优化分析开销、阻止循环展开的属性；它不是关掉整个着色器的优化。其实际编译收益和运行时性能仍需实测。

主循环的 `325` 是保守的终止上限，不是每条光线新增 325 个采样；仍依照原预算执行并提前退出。预算没有降低：云层 `32 / 48 / 64 / 64`，空气 `16 / 24 / 32 / 48`，云光照 `2 / 3 / 4 / 6`。

Pass 顺序仍为：`0 = MRT_VOLUME`、`1 = FULL_RES_VOLUME`、`2 = MAP_VOLUME`。`SV_Target0`、`SV_Target1` 的 light、transmission 与 sceneDepth 布局未改。

没有更改天气模拟、云的存在与运动逻辑、自转、闪电频率、贴图、场景、存档、画质档位、渲染管线或 ProjectSettings。没有使用 `#pragma skip_optimizations`、强制 DXC、强制更换图形 API，或关云规避错误。

## 已执行的检查

新增报告在 `Documentation/V4_2_Repair_IPCFix/static-and-numerical-report.json`。

- 69 个代码文件的词法结构、include 路径与 90 个 GUID 一致性检查；检查原三个云层 Pass 和 MRT 布局。
- 11,904 组视线案例，分别带/不带合成介质提前退出，共 23,808 次对照、435,617 条采样记录；旧/新调度的中点、步长、顺序和云/空气分类一致。覆盖边界、切线、内部/外部、四档质量和异常预算夹取。
- 12,000 条直接阴影光线、218,016 条采样记录，其中 3,372 条穿过两段云壳；旧/新采样节点及顺序一致。
- 500 组阈值组合检查阴影复用，包括 `_OrbisShadowReady == .5` 和云密度边界。

以上是 **CPU 双精度的独立调度回归模型**，不是执行实际 GPU Shader，不能推导为像素级完全一致或 IPC 已消失。

还复跑原 Repair 验证脚本，其 8 项离线检查全部通过，结果另存为 `rerun-repair-regressions.json`，没有覆盖旧版验收文档。其中天气核心相关项目是对已有测试报告与当前源码哈希的核对，不是本次重跑全部天气模拟。

本地复跑新增检查：

```text
python Validation/validate_v42_repair_ipc.py
```

## Unity 编译诊断：如何判定真的通过

新工具只在手动点击菜单时运行，不添加自动导入或自动 Play 钩子。它同步请求编译云层、云阴影、地表、水体与合成相关的 7 个 Shader、10 个默认材质 Pass，检查 `IsPassCompiled`、Shader 错误、可用性、Pass 数与名称，以及编译期错误日志。

报告路径：

```text
Documentation/V4_2_Repair_IPCFix/Unity-compile-report.json
```

工具会在每次导入与 Pass 编译**之前**写检查点。若 Unity 本身或编译器原生层崩溃，文件可保留最后的 `currentOperation`；不会把未完成操作默认当成功。无真实图形后端会标为 `NOT_RUN`，关闭全局 Shader 优化也不会标为生产编译验收通过。

只有报告 `state = COMPLETED` 且 `success = true`，才能说明**这台电脑当前图形 API 与色彩空间下这些默认 Pass** 的检查通过。它不是跨平台构建验收，也不覆盖尚未请求的其它变体。工具本身的 C# 编译同样有待目标 Unity 验证。

若仍失败，保留该 JSON 与 `Editor.log`，重点看 `currentOperation`、`graphicsAPI`、`unityVersion`、`compilerLog`，以及 Editor.log 中错误前后内容。不要只重复发 `0x80000004` 三行；完整上下文才能区分源码触发、FXC/HLSLcc 后端缺陷、编译器启动失败或系统进程异常。

## 回滚

手动安装前保留原四个 Shader 源文件，回滚时恢复它们并移除本次新增诊断工具 `.cs` 和 `.meta`。可选安装器会把被覆盖文件与新增文件清单存入工程根目录 `ORBIS_IPCFix_Backup_时间戳`，不放在 Assets 中。不要把旧版四个 Shader 和新版四个 Shader 混用后判断效果。

## 修改审计与参考

本补丁的文件前后哈希、完整差异分别见 `patch-manifest.json`、`source-changes.diff`。本次环境没有 Unity CLI 或 Editor，CLI 获取失败；也没有可用独立 HLSL 编译器。所有报告明确保留这一限制。

参考官方资料（用于解释编译行为，不作为本工程已复现某个 Unity Bug 的证据）：

```text
Microsoft Learn — for Statement / loop / fastopt
https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-for

Unity Issue Tracker — UUM-127804，FXC 崩溃可表现为编译 IPC 断开
https://issuetracker-mig.prd.it.unity3d.com/issues/compiling-csmain-internal-error-communicating-with-the-shader-compiler-process-dot-error-when-compiling-a-shader

Unity 6 — ShaderUtil.CompilePass
https://docs.unity3d.com/cn/6000.0/ScriptReference/ShaderUtil.CompilePass.html

Unity 6 — ShaderUtil.IsPassCompiled
https://docs.unity3d.com/cn/6000.0/ScriptReference/ShaderUtil.IsPassCompiled.html

Unity Manual — Shader compilation / Library/ShaderCache
https://docs.unity.cn/2021.2/Documentation/Manual/shader-compilation.html
```
