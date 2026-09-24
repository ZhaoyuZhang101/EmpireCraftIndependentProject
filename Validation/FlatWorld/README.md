# Validation / FlatWorld

本目录只将当前确实执行的检查写为PASS，不沿用旧版报告。

- `static_check.py` → `static-report.json`：Python标准库词法/资源/元数据检查，不是Unity编译。
- `verify_classifier_cpp.py` → `classifier-cpp-report.json`：从实际C#方法体机械转换到C++，g++执行65气候用例，不是C#执行。
- `reference_world_check.py` → `reference-world-report.json`：独立NumPy参考模型（需numpy/Pillow），不是Unity截图或实际C#运行。
- `FlatWorld.Core.Tests.csproj` / `Program.cs`：可在本机.NET8运行真实核心C#，交付环境未执行。
- `Assets/ORBIS/Runtime/FlatWorld/Core/FlatSelfTests.cs`：Unity编辑器菜单调用的同一真实核心用例，交付环境未执行。

自查命令从项目根目录运行：

```sh
python Validation/FlatWorld/static_check.py
python Validation/FlatWorld/verify_classifier_cpp.py
python Validation/FlatWorld/reference_world_check.py
```

最后两项不是启动Unity所需依赖，只供离线复核。C++临时可执行文件不会保存在项目中。最终原生验收应按 Documentation/TerrainPerf3/验收与已知限制.md 进行。
