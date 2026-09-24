"""Reproduce offline checks. Requires Python, numpy, Pillow, g++, Mesa EGL/GLES.
Optional: tree_sitter + tree_sitter_c_sharp for syntax parsing. NOT a Unity build.
"""
from pathlib import Path
import subprocess,sys
root=Path(__file__).resolve().parents[2]
for script in ['Perf3_2/generate_fragment_probes.py','Perf3_3_2/compile_scroll_probe.py',
               'Perf3_3_2/render_detail.py','Perf3_3_2/render_clouds_and_mouth.py',
               'Perf3_3_2/verify_surface_cpp.py','Perf3_3_2/validate_integration.py']:
 subprocess.run([sys.executable,str(root/'Validation'/script)],check=True,cwd=root)
print('Offline checks complete. Unity compilation and Play Mode remain unverified.')
