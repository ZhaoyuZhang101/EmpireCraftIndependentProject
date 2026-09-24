#!/usr/bin/env python3
"""Run reproducible offline checks. This never runs Unity, D3D11 or native C#."""
from pathlib import Path
import subprocess,sys,shutil
ROOT=Path(__file__).resolve().parents[2];HERE=Path(__file__).resolve().parent
steps=[
 ('Validation/FlatWorld/static_check.py','Validation/FlatWorld/static-report.json','static-report.json'),
 ('Validation/Perf3_2/verify_source_contracts.py',None,None),
 ('Validation/Perf3_2/verify_surface_cpp.py',None,None),
 ('Validation/WaterClimate/verify_drainage_cpp.py','Validation/WaterClimate/drainage-cpp-report.json','drainage-cpp-report.json'),
 ('Validation/WaterClimate/verify_swe_cpp.py','Validation/WaterClimate/swe-cpp-report.json','swe-cpp-report.json'),
 ('Validation/FlatWorld/verify_classifier_cpp.py','Validation/FlatWorld/classifier-cpp-report.json','classifier-cpp-report.json'),
 ('Validation/Perf3_2/generate_fragment_probes.py',None,None),
 ('Validation/Perf3_2/render_fragment_fixtures.py',None,None),
]
for script,report,copy in steps:
 print('\n=== '+script+' ===',flush=True)
 subprocess.run([sys.executable,str(ROOT/script)],cwd=ROOT,check=True)
 if report:shutil.copy2(ROOT/report,HERE/copy)
print('\nAll requested OFFLINE checks passed. Native Unity / C# / HLSL / D3D11 / Play Mode NOT executed.')
