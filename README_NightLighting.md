# ORBIS V4.2 Repair IPCFix1 — 夜侧光照扩展

本包只在 `ORBIS_Unity_V4_2_Repair_IPCFix1_Full.zip` 上修改，不是 V4.2.4、V4.3，也没有回退到未修复 IPC 的 Repair。保留 Unity **6000.0.47f1**、**Built-in Render Pipeline** 与工程目录 `ORBIS_Unity_V4_2_Repair`。

**本次新增：气辉 airglow、带月相的定向月光、微弱夜间天光；接入地表、海洋／河流、云层和大气。默认开启。** 没有把最终画面整体加灰，也没有提高曝光来掩盖黑色。

## 交付与验证边界

已完成源码修改、原 IPC 数值回归、新增夜光数值检查，以及新增函数机械转译后的 GLES 软件执行检查。**当前环境未运行 Unity Editor、UnityShaderCompiler、HLSL 编译器或 C# 编译器，未实测 FPS，也没有实际 Unity 画面验收。不能据此保证用户机器上的 IPC 崩溃不再发生。**

源码与测试的完整说明见 `Documentation/V4_2_Repair_IPCFix_NightLighting/夜侧光照说明.md`。新的文件指纹清单是根目录 `NIGHT_LIGHTING_MANIFEST.json`。保留的旧 README、旧 Validation 报告及 Preview 页面都是历史资料，不是本次画面或编译通过的证明。

## 已有 IPCFix1 工程：用增量补丁

关闭 Unity 并备份工程。把补丁根目录的 `Assets`、`Documentation`、`Validation`、`Tools` 按同名路径**合并**到工程；同时复制 `README_NightLighting.md` 与 `NIGHT_LIGHTING_MANIFEST.json`。不要删除整个 Assets 后再替换；不要覆盖到其它版本上。已有 `.meta` 不改动，补丁只为三个新增源码文件提供新 `.meta`。

更稳妥的方式是使用自带安装器，先核对原文件指纹，再自动备份与覆盖。需要 Python 3.9+，在补丁根目录执行：

```text
python Tools/apply_v42_ipc_night_patch.py "D:/Your_ORBIS_V4_2_Repair"
python Tools/apply_v42_ipc_night_patch.py "D:/Your_ORBIS_V4_2_Repair" --apply
```

第一条仅预检；第二条实际应用。修改过的同名文件或其它版本会被拒绝覆盖。安装器不清缓存、不更改 Packages / ProjectSettings、不启动 Unity。

## 直接打开完整工程

把完整 ZIP 解压到新目录，在 Unity Hub 添加其中包含 `Assets / Packages / ProjectSettings` 的 `ORBIS_Unity_V4_2_Repair` 文件夹。完整包已包含这次扩展，不需要再套补丁。

打开后，先运行：

**Tools → ORBIS → V4.2 Repair → 编译夜侧光照并保存诊断**

然后打开原场景 `Assets/ORBIS/Scenes/OrbisDemo.unity`，进入 Play 检查夜侧。新诊断工具会同步请求当前图形 API 的 10 个默认 Shader Pass，并在每步之前写检查点；还会在本机运行月相与存档序列化测试。报告路径：

`Documentation/V4_2_Repair_IPCFix_NightLighting/Unity-compile-report.json`

若怀疑旧 Shader 缓存，在 Unity 完全退出时将存在的 `Library/ShaderCache` **重命名备份**；不要删除整个 Library。新解压的完整工程通常无需此操作。

## 调整亮度

Play 模式选中挂载 `OrbisPlanetView` 的星球对象，展开 **V4.2 Repair IPC — 夜侧光照（随世界存档）**。天光、气辉、满月亮度均可分别调节。修改后使用游戏原有保存功能，设置跟随世界存档；仅退出 Play 而未保存，不代表已写入游戏存档。

保留原有「昼夜」开关开启；关闭它是旧版的展示／检查照明模式，不是夜晚效果。原始 Debug、地图专题图也不适合当自然夜景验收图。


## NightLighting Fix 2 — 夜间绿色大气圈

将气辉由偏绿色高强度源改为极弱、低饱和蓝灰色；同时降低向下气辉和大气切线自发光。月光与微弱夜间天光不受影响，因此夜侧仍不会全黑。
