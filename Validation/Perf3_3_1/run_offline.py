"""Current integration suite. Requires Python, numpy/Pillow, C++ compiler and Mesa EGL/GLES.
Optional tree-sitter + tree-sitter-c-sharp enables full C# syntax parsing.
This does NOT invoke Unity or compile C#.
"""
from pathlib import Path
import subprocess,sys
root=Path(__file__).resolve().parents[2]
steps=['Validation/Perf3_3_1/validate_merge.py','Validation/Perf3_2/verify_surface_cpp.py','Validation/WaterClimate/verify_drainage_cpp.py','Validation/WaterClimate/verify_swe_cpp.py','Validation/FlatWorld/verify_classifier_cpp.py','Validation/Perf3_2/generate_fragment_probes.py','Validation/Perf3_2/render_fragment_fixtures.py','Validation/Perf3_3_1/compile_scroll_probe.py','Validation/Perf3_3_1/render_detail.py']
for path in steps:subprocess.run([sys.executable,str(root/path)],cwd=root,check=True)
print('PASS within offline scope. Unity/C#/D3D11/Play Mode/FPS remain untested.')
