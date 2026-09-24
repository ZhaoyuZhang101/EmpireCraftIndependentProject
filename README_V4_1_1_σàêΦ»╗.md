# ORBIS Unity V4.1.1 — 云着色器循环展开修复

**交付状态：已修改真实 V4.1 源码并通过离线检查；未在 Unity Editor 中完成 C#／HLSL 编译或 GPU 画面验收。**

本次对应报错：`ORBIS/Clouds ... unable to unroll loop`。用户未提供完整 Console 日志，因此不能确认唯一报错行、具体 Pass 或图形后端。修复针对原包可确认的强制展开结构，不声称已经在用户电脑上复现和消除错误。

基线：`ORBIS_Unity_V4_1_AtmosphereCloudFix.zip`，5,664,624 字节。
SHA-256：`b2f6e034a0e156260384ff345c3609d64c91bfc3fbfba1153b7e588a0d3f220f`。
环境保持 **Unity 6000.0.47f1 / Built-in Render Pipeline**；保留 `#pragma target 3.5`，没有改成 URP、HDRP、SM5 或要求切换图形 API。

## 1. 使用哪个包

**完整工程包**适合另建目录对照。在 Unity Hub 添加包含 `Assets`、`Packages`、`ProjectSettings` 的工程根目录，打开：

`Assets/ORBIS/Scenes/OrbisDemo.unity`

**增量补丁**仅面向上述 V4.1 基线。先退出 Play，关闭 Unity Editor。解压补丁，在补丁目录打开终端，先检查：

```text
python apply_patch.py --project "你的V4.1工程根目录"
```

确认预检没有冲突后应用：

```text
python apply_patch.py --project "你的V4.1工程根目录" --apply
```

系统命令是 `python3` 时用它替换 `python`。工具仅依赖 Python 标准库。默认不写文件；应用前逐文件验证原版／补丁 SHA-256；遇本地修改则停止，**没有强制覆盖开关**。原文件保存在工程根目录 `.orbis-backups` 下。关闭 Editor 是为了避免导入期间部分文件版本不一致。

不要把整个完整工程覆盖到有本地修改的项目，也不要同时套用完整包和增量包。只修源码，不删除 Library、不清空存档、不重新生成世界。

## 2. 原包里实际找到的结构

以下行号以未修改 V4.1 为准：

| 文件及位置 | 观察 | 本次处理 |
|---|---|---|
| `OrbisAtmosphereCommon.cginc:111–112` | 六元素排序使用双层 `[unroll]`，内层从 `b=a+1` 起，索引依赖外层 | 已知 begin/end 是界限，只排序四个云交点；用五次 min/max 比较的排序网络，删除排序循环和 `cuts[6]` 数组 |
| 同文件 `120–128` | `[unroll]` 包裹最多五次 `IntegrateSegment`，内含自适应步进、日照和密度查询 | 分段循环改为 `[loop]`，让同一主积分循环体服务不同区间 |
| 同文件 `25–45` | 光照循环强制展开，同时使用 `_LightSteps`、运行时 `break` 和临时数组 | 两个光照循环均显式 `[loop]`，六份光学厚度使用固定向量分量存储，避免动态临时数组索引 |
| `OrbisCloudCommon.cginc:69–73` | 两段云影回退里再次展开六次复杂密度函数 | 两层均保留运行时循环，跳过空区间 |
| `OrbisOptics.cginc:26` | 主步进所调用的八次太阳透射积分又被展开 | 保留八步和同一积分公式，改为 `[loop]` |
| `CloudShadowCache.shader:23–27` | 步长除数与 32 次硬上限可在越界输入时不一致 | `_ShadowSteps` 统一限制为 1–32，同时用于步长与终止判断 |

**已确认的是这些源码结构，不是已确认某一行在所有编译器上必然失败。** `[unroll]` 本身并不错误；小型、固定、简单的循环可以适用。本次没有全工程机械替换，例如 2×2 合成核和十项水波核保持原状。

主积分原本已有 `[loop]`，所以“给最里面的循环加一个 loop”不够。另一方面，原包的天气／风／三维噪声查询已经使用 `tex2Dlod`、`tex3Dlod`，不能把本次错误直接归咎于隐式 LOD 采样。

## 3. 保留的功能和公式

保留 `ORBIS/Clouds` 名称和原有材质／资源定位；新增 Pass 名称便于定位，**索引未变**：0 = `MRT_VOLUME`；1 = `FULL_RES_VOLUME`；2 = `MAP_VOLUME`。

地表、河海水面、文明／区域、天气演化、有界云噪声运动、共享云影、空气源项云影、展开地图径向大气柱和最终曝光链仍存在。没有通过关云、取消多重散射近似、降低默认质量档、缩小云层或改换渲染管线来掩盖报错。

原有四档云步数 20/32/48/64、空气步数 12/20/28/40、云光照步数 2/3/4/6，以及阴影步数 10/16/24/32 均未修改。新增上限只防止异常输入：云光照 1–6、阴影 1–32、单段主积分 1–64。总成本还依赖分段数量和内层采样，不应将“64”理解为整个 Shader 的采样总数。

有效输入下的密度、散射、透射、颜色和各向同性近似公式没有重调。循环／排序与 GPU 优化顺序改变后，不能保证不同设备输出逐像素位级一致。运行性能亦未实测。

## 4. NVIDIA 参考与上传 GLSL

详细核对见 `Documentation/GPU-Gems-16-ORBIS-V4_1_1.md`。

这次将 GPU Gems 2 第 16 章用作几何、光学与合成关系的核对依据，**没有声称移植其完整 Cg 代码、O'Neil 快速拟合、LUT 或完整多重散射**。上传的 `atmosphere.glsl` 原文及 MIT 头保存在 `Reference/atmosphere.glsl`，不放进 Assets 参与 Unity 编译。文件 SHA-256：`708ea4a9141a7653ea0085d7f26ae09ca09bb14c88e339d63da74e16439300bd`。

v4.1.1 优先修编译链，不把尚未验证的新视觉算法与热修复混在一起。现有大气仍是游戏尺度，不能称为真实地球参数标定。

## 5. Unity 内真正触发编译

打开工程、等待导入后执行：

`Tools > ORBIS > V4.1.1 > 強制編譯雲與大氣並輸出報告`

新工具用 `ShaderUtil.CompilePass(material, pass, true)` 请求同步编译，再检查 `IsPassCompiled`、`ShaderHasError`、`isSupported` 和完整 Shader 错误消息。覆盖 Clouds 的三个 Pass，以及 CloudShadowCache、AtmosphereComposite、Display、Surface、RiverWater 的默认材质 Pass。检查结束恢复异步编译设置并销毁临时材质，不修改场景或渲染配置。

报告路径：`Documentation/Unity-V4_1_1-shader-report.json`。

**交付包不包含伪造的 Unity 成功报告。** 该报告只有你实际执行工具后才产生。若没有图形设备（如 `-nographics`），结果标记为未通过／跳过，而不是成功。工具仅检查当前 Editor 图形 API、色彩空间和默认材质变体，不等同于 Metal／DX11／Vulkan 全平台构建，不覆盖所有 ShadowCaster 关键字组合。

原 V4.1 菜单仅检查资源和已有 Shader 消息，可能漏掉尚未触发的延迟编译。本次因此新增“强制编译”，而非继续只做资源存在性判断。

原生编译之后的人工验收：同一世界依次检查球体云、全分辨率对照、展开地图；再从太空缩放到云层内和地表，检查晨昏、遮挡、夜面直射光和 HDR 曝光。不要仅凭 Console 无报错判断画面正确。

## 6. 本次实际执行的检查

新增 `Validation/validate_v411.py`：**9 组通过、0 组失败**。内容包括：四元素排序的 24 种排列、81 组重复值和 10,000 组随机值；10,000 条射线的新旧分段参考一致性；6,000 组光学厚度存储和漫射求和参考；步数边界、地内地外几何、三 Pass 接线、循环源代码约束和资源 meta／无字体检查。

在新代码上重跑原 V4.1 检查：**17 组通过、0 组失败**。报告分别位于 `Documentation/Validation-V4_1_1.json` 与 `Documentation/V4_1-regression-on-V4_1_1.json`。历史报告文件保持原样。

这些是源码约束检查与独立 CPU 数值参考，**不是执行 HLSL，不是 Unity 导入／C# 编译，不是 GPU 渲染、性能数据或截图证据**。本环境未发现 Unity Editor；CLI 下载因域名解析失败未完成，独立 DXC 下载也失败。因此没有声称原生编译或视觉验收完成。

## 7. 文件与追溯

主要修改：`Clouds.shader`、`OrbisAtmosphereCommon.cginc`、`OrbisCloudCommon.cginc`、`OrbisOptics.cginc`、`CloudShadowCache.shader`。
新增：`OrbisV411ShaderValidation.cs` 及 meta、回归脚本与报告、NVIDIA 对照说明及 GLSL 参考副本。

`Documentation/Diffs/V4_1-to-V4_1_1.diff` 给出五个 Shader／include 的逐行差异。
`Documentation/ChangeManifest-V4_1_1.json` 给出相对真实 V4.1 原包的变化清单与哈希。
所有旧 meta GUID、场景、运行时 C#、材质图集、示例地图和保存逻辑保持原字节。继续使用系统字体；不附字体二进制。
