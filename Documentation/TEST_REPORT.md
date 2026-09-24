# ORBIS V4 验证记录

来源工程：本轮上传的 ORBIS Unity V3 SystemFonts ZIP。

## 已执行

`python Validation/validate_v4.py`：**11 组通过，0 组失败**。具体数据见同目录 `Validation-V4.json`。离线测试是源码引用／数据夹具检查和独立 Python 数值参考，不是通过模拟环境执行 Unity C# 或 HLSL。

当前示例数据：两张 1024×512 图像，高度 PNG 为 16 位灰度；河图参考模型 427 个有效节点，1 个连通分量，0 个明显抬水位警告。10,000 个球面／像素中心往返的最大坐标误差约 3.39e-15。热松弛参考的高度和误差约 3.55e-15。数值是参考模型结果，不是实际 C# 单精度误差。

资源检查：116 个唯一 UI 命名、20 处直接资源加载引用、66 个唯一资产 GUID、0 个字体二进制。源代码括号、include 和有限的 var 声明检查通过；这类检查不能排除所有 C# 类型或 API 错误。

## 未执行

- Unity Editor 导入与完整 C# 编译。
- ShaderLab/HLSL 编译、全部平台变体验证和 GPU 画面检查。
- Play Mode、真机帧率、内存峰值、桌面构建和长时间持续运行。
- 生产 C# 图像解码器、世界存读档和编辑器菜单的实际执行。

环境没有可用 Unity 编辑器或 C# 编译器，因此不能把这些项写成通过。

## 已提供的实际 C# 验收入口

Unity 菜单 `Tools > ORBIS > V4 > Validate Upgrade (Production Code)`：编解码精度往返、PNG 过滤器、BMP 索引色、源图嵌入、河网水位单调、只下切的河床、宽度变化、高地河流、入海水位、同源球面／地图网格、V4 存档、区块建国、人口不复制、非法占领拒绝、热松弛守恒和着色器当前错误状态。

该菜单在本环境没有运行。输出路径：`Application.persistentDataPath/orbis-v4-editor-validation.json`。实际导入后仍应按照 README 做近景、入海口、湖面、经度接缝、国家扩张和昼夜的视觉验收。

V3 的旧结论保存在 `History/TEST_REPORT_V3.md` 和 `Validation-V3.json`，仅作为历史记录。
