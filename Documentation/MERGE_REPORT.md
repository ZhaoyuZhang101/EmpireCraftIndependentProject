# V3 合并报告

## 基线与合并方法

A：用户本次上传 `ORBIS_Unity_Demo(1).zip`，根目录 `ORBIS_Unity`。
B：原云水升级 `ORBIS_Unity_Demo_v2_CloudWater.zip`，根目录 `ORBIS_Unity_Demo_v2`。
输出：独立 `ORBIS_Unity_V3_Merged`。未修改两份输入归档。

以 A 的场景、运行时游戏系统、地理/远征/中文地图界面和地形资源为骨架，选择性接入 B 的球面插值、天气库、云密度与水体；新写视角、近景与联合体积集成模块。两套同名主文件按职责合并，不采用后者整体覆盖前者。

## 关键冲突与处理

| 冲突 | 处理 |
|---|---|
| A 的云漂移逐格累加；B 的细节采样有界 | 禁用 cloudDrift 累积及采样，旧字段保留为兼容并清零 |
| A 的风暴/气压风与 B 的另一套风场互相覆盖 | A 提供天气强迫，B 输运模块读取同一份切向风；气压与风暴升力进入对流项 |
| Climate 每次更新都可能重写云量 | 仅初始化阶段由 Climate 提供起始云量，运行中云水库负责覆盖与降水 |
| A 河流网格缺少 B 水体需要的属性 | 给河流补入水深、河岸位置和下游方向；全球/近地网格补水体代理 |
| 旧透明云球与新屏幕空间云重复 | 保留对象兼容，但停用旧 MeshRenderer；只使用新联合体积路径 |
| 相机进入旧拾取外球后无法选择 | 支持从外包围球内部开始射线，与同一采样地形做有界步进和二分 |
| 从全景地图返回时关注点随自转跑掉 | 缓存地理方向而非屏幕角度；选区改变则恢复新选区 |
| 新输运子物体与父级同时自转 | 子级采用单位旋转，只继承观测层父级的星球旋转 |
| 两个分支都写 version=2 | 通过天气时钟和云水字段判别；新档采用 version=3 与 producer 标记 |
| 近地粗细网格在颜色和深度中不一致 | 两个 pass 引用同一互补遮罩；聚落/河流/选择覆盖层排除该裁剪 |
| 字体资源分发 | 移除字体二进制和硬编码字体资源路径，运行时尝试系统 CJK 字体 |

## 新/合并代码入口

`Assets/ORBIS/Runtime/`：

- `OrbisDemo.cs`：连续离地缩放、径向观察、近地跟随、地图关注点与 V3 保存加载。
- `OrbisWorld.cs`、`OrbisAtmosphereModel.cs`、`OrbisWeather.cs`：统一天气强迫与有状态云水输运。
- `OrbisSaveMigration.cs`：两种旧 V2 格式识别、新字段校验。
- `OrbisPlanetView.cs`、`OrbisMapView.cs`：水体属性、共享地形采样、渲染绑定和拾取。
- `OrbisSurfaceLOD.cs`：单个局部细分显示网格；不是新模拟网格。
- `OrbisAtmosphereRenderer.cs`：Built-in 全分辨率参考与低分辨率体积双缓冲。
- `OrbisTransportView.cs`：有限寿命风/水汽/洋流轨迹。
- `OrbisSphericalSampler.cs`、`OrbisCloudNoise.cs`：从 B 保留的基础模块。
- `OrbisSystemFont.cs`、`OrbisHud.cs`：系统字体和新增控制。

`Assets/ORBIS/Resources/ORBIS/`：`Surface.shader`、`Clouds.shader`、`AtmosphereComposite.shader`、`Transport.shader`、`OrbisAtmosphereCommon.cginc`、`OrbisCloudCommon.cginc`、`OrbisWater.cginc`、`OrbisPatch.cginc`。

`Assets/ORBIS/Editor/OrbisV3Validation.cs`：新增本机测试菜单。测试方法存在不代表已被执行。

## 明确保留与不迁入

保留 A 的地理、命名、远征、逻辑地块、文明规则、原场景 GUID、地形 atlas 和浏览器参考。精确未改文件可从 SourceManifest 中 `identical_to` 字段核对。

不并行启用 B 的旧独立云合成组件，不恢复 A 的无限云漂移，不复制输入包中的字体二进制，不更换渲染管线，不重编号逻辑地块。A 的 64³ 噪声二进制与生成工具保留作输入参考；当前活动云密度使用 B 的 32³ 程序化噪声。

`SourceManifest.json` 同时记录两个原始 ZIP 的 SHA-256、每个输出文件的 SHA-256、是否与 A/B 同路径文件完全相同，以及 A 中未原路径保留的文件。`Diffs/Incoming-to-V3.diff` 和 `Diffs/CloudWaterV2-to-V3.diff` 是文本对照，**不是可无冲突自动应用的补丁**。

不要以本报告中的功能合并记录替代引擎验收。当前工程是未通过 Unity 编译与画面验收的源码集成版。
