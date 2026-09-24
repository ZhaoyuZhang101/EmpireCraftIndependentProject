# ORBIS V4-2 原版专用修复包

**本包从 ORBIS_Unity_V4_2_DynamicWeather.zip 修改；没有合并 V4.2.3/V4.2.4/V4.3。**

Unity 6000.0.47f1 · Built-in · 场景 `Assets/ORBIS/Scenes/OrbisDemo.unity`。

请先读 `Documentation/V4_2_Repair.md`。本次修改清单与哈希在 `REPAIR_MANIFEST.json`，实际离线测试在 `Documentation/V4_2_Repair/`。

已完成源码和数据修复；Unity 导入、C# / HLSL 编译、画面与帧率尚未运行验证。3,456 格长测超时，不标记通过。`Preview/`、旧 README 和旧验证报告是原包保留历史材料，**不是本次修复画面或验收证据**。

新世界会初始化新的局部低压扰动；加载原存档不会强制重置风场、压力或云水。不要删除存档来“修复”问题，先备份再分别测试。
