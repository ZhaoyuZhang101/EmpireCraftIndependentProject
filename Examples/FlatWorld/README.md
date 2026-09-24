# 离线参考图与高度图

这些图来自 Validation/FlatWorld/reference_world_check.py 的独立 NumPy 参考模型，**不是 Unity 实机截图，也不承诺与 C# float32 在边界逐格一致**。

reference-continents-height16.png 为 256×128、北向在上、16-bit 灰度的完整地床图，可把绝对路径填入新场景“高度图路径”，点击“导入为新世界”。黑色 −4000m，白色 +4000m。

reference-water-terrain.png 仅用于查看高度/排水流程是否产生海洋、大陆与淡水分布；不包含最终 UI、文明、细节材质或星球渲染。

导入重新生成气候、排水与文明，不导入该图的颜色或水体语义。原生完整状态应使用 .orbis 存档。
