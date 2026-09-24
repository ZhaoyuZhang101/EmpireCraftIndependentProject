# 本版测试入口

从工程根目录运行 `python Validation/Perf3_2/run_offline.py`。需要Python3、g++、NumPy和Pillow，图形探针另需EGL/GLES3软件或硬件实现；脚本不安装这些依赖，也不启动Unity。缺少依赖应报告失败/未执行而不是PASS。

该脚本重新执行通用词法/资源检查、Perf3.2源代码契约检查、连续场/河曲/植被C++转译回归、原排水/浅水/气候分类C++转译回归，并重新生成及执行GLSL片元探针。结果不自动修改交付时的RELEASE_VALIDATION.json；新生成的各报告带各自时间。

Windows原生入口为run_d3d11.ps1；菜单和适用范围见Documentation/TerrainPerf3_2/ACCEPTANCE.md。未执行前不存在native PASS报告。
