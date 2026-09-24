"""Current-version offline checks; requires Python/numpy/Pillow, g++, Mesa EGL/GLES.
Optional C# syntax parsing: tree_sitter and tree_sitter_c_sharp. Does not launch Unity.
"""
from pathlib import Path
import subprocess,sys
here=Path(__file__).resolve().parent
for name in ['check_shader_identifiers.py','compile_scroll_probe.py','verify_planet_core.py','verify_navigation.py','verify_seam_gpu.py','validate_integration.py']:
 subprocess.run([sys.executable,str(here/name)],check=True,cwd=here.parents[1])
print('PASS: offline checks only. Run Unity import, Play Mode camera tests and visual seam inspection separately.')
