# 本机验收清单

本文件列的是待执行步骤，不是通过记录。交付时 `unity-native-report.json` 不存在；只有真正运行Unity菜单才生成它。不要将C++数值报告改名成Unity报告。

## 原生编译

完整解压，Unity6000.0.47f1/Built-in；先查看Console的C#导入错误。Windows项目使用D3D11，执行 `Tools/ORBIS/Perf3.1/Run water climate and current-API validation`。

该菜单实际执行扩展后的C#核心测试、导入并克隆UXML、验证六组tab/panel存在、同步编译FlatMap/Surface/Route的当前API默认Pass、检测ShaderHasError和isSupported。报告包含Unity版本、真实图形API和失败堆栈。

PowerShell（把Unity路径替换成真实安装位置）：

```powershell
.\Validation\WaterClimate\run_d3d11.ps1 -UnityEditor "C:\Program Files\Unity\Hub\Editor\6000.0.47f1\Editor\Unity.exe"
```

不要加`-nographics`。无图形核心验证与有图形shader验证是不同任务。不是所有关键字组合都被此菜单覆盖，旧体积云/大气全部变体仍可另运行原第3号全项目着色器诊断。

## Play Mode视觉和交互

| 操作 | 应检查的结果 |
|---|---|
| 默认新世界 | 无粉色地图；二维模拟、4×4选择和已有文明运行 |
| 六个标签逐一点击 | 只展开当前分类；按钮可找到；组内折叠正常；图层/工具入口固定可见 |
| 水文→下一个河口 | 聚焦真实River→Ocean出口；有连贯窄河面，不是整格矩形蓝块 |
| 放大河口 | 渐变扇形沿下游向海展开，不跨陆地；大小受汇流而非纯随机圆斑决定 |
| 河岸、海岸、湖岸 | 沙/淤泥/砾石材料过渡；不出现河道黑白外描边；淡水与海水有不同水色和水深变化 |
| SWE开关与波动按钮 | 8 px/Tile以上近景状态变化，平衡扰动产生局部波动；水量误差有限；远景不持续跑局部求解 |
| 暂停/切换星球 | 文明模拟暂停；星球使用同一世界；水文开关仍影响水色，不运行第二套SWE |
| 气候/地貌/材料图层 | 雨林/沙漠按Köppen，湿地有水文约束；丘陵/高原不被当成Köppen气候 |
| 等高线四档 | 高差处清楚，无整片平地全黑，水面不被地形等高线描边；缩远关闭细线 |
| 修改高程并重算 | 排水和河湖依据新高程重新计算；处理成功后再替换当前世界 |
| 保存再加载 | 文明与世界数据保留；旧v1群系不在加载时悄悄重标，手动重算才应用新规则 |
| 屏幕窄/文字多 | 滚动正常、标签不互相覆盖、系统中文字体可见；这是实机布局检查，XML解析不能代替 |

## 性能与长期运行

Profiler分别测远景/近景、SWE开关、星球摄影、默认512×256与大世界。记录主线程、GC、SWE后台时间、GPU用时和帧率；本包不预填任何FPS提升数值。连续运行、反复生成/加载/切换模式、关闭场景，检查迟到任务不操作销毁的Unity对象。

## 已执行离线测试的复现

```text
python Validation/FlatWorld/static_check.py
python Validation/WaterClimate/source_contracts.py
python Validation/FlatWorld/verify_classifier_cpp.py
python Validation/WaterClimate/verify_drainage_cpp.py
python Validation/WaterClimate/verify_swe_cpp.py
```

后三项需要g++；生成临时C++可执行文件，不把它打包进Assets。转译通过不是原生C#通过。安装.NET8 SDK后可独立运行 `dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj`；它执行真正C#核心测试，但仍不验证Unity图形、UXML、输入与画面。
