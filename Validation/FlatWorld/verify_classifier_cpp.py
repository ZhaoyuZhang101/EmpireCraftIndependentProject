#!/usr/bin/env python3
"""Mechanically translate the ACTUAL classifier body to C++ and test fixtures.
This is a numerical cross-check, not a C# or Unity compilation. Requires Python 3 + g++.
Generated executable is temporary and not part of the delivery.
"""
import re,json,hashlib,subprocess,tempfile,datetime
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
core=ROOT/'Assets/ORBIS/Runtime/FlatWorld/Core'
source=(core/'KoppenClassifier.cs').read_text()
types=(core/'FlatTypes.cs').read_text()
body=source[source.index('            if(temperature==null'):source.rindex('\n        }')]
# Drop managed-array null checks; vectors are validated with explicit bounds instead.
body=re.sub(r'            if\(temperature==null[\s\S]*?throw new ArgumentException\("Twelve temperature and precipitation values are required\."\);',r'            if(offset<0 || temperature.size()<offset+12 || precipitation.size()<offset+12) throw std::runtime_error("Invalid arrays");',body,count=1)
body=body.replace('FMath.Finite','std::isfinite').replace('Math.Min','std::fmin').replace('Math.Max','std::fmax').replace('Koppen.','Koppen::').replace('throw new ArgumentException','throw std::runtime_error')
enums=re.search(r'public enum Koppen : byte \{([^}]+)',types).group(1)
fixtures=json.loads((ROOT/'Validation/FlatWorld/koppen-fixtures.json').read_text())
if isinstance(fixtures,dict):fixtures=fixtures.get('cases',fixtures.get('fixtures'))
print('Fixture shape:',str(fixtures[0])[:300])
cpp='#include <vector>\n#include <cmath>\n#include <stdexcept>\n#include <iostream>\nusing std::vector;\nenum class Koppen {'+enums+'};\nKoppen Classify(const vector<float>& temperature,const vector<float>& precipitation,int offset=0){\n'+body+'\n}\n'
cpp+='int main(){int assertions=0;\n'
def vec(v):return '{'+','.join(format(float(x),'.9g')+('f' if '.' in format(float(x),'.9g') else '.0f') for x in v)+'}'
for row in fixtures:
    ts=row.get('temperature',row.get('T'));ps=row.get('precipitation',row.get('P'));expected=row.get('expected',row.get('class'));name=row.get('name',expected)
    cpp+='if(Classify('+vec(ts)+','+vec(ps)+')!=Koppen::'+expected+'){std::cerr<<"FAIL '+name.replace('"','')+'\\n";return 1;}assertions++;\n'
cpp+='std::cout<<"PASS "<<assertions<<" classifier fixtures\\n";return 0;}\n'
with tempfile.TemporaryDirectory() as d:
    path=Path(d);(path/'verify.cpp').write_text(cpp)
    compile_result=subprocess.run(['g++','-std=c++17','-O2','-Wall',str(path/'verify.cpp'),'-o',str(path/'verify')],text=True,capture_output=True)
    if compile_result.returncode:raise RuntimeError(compile_result.stderr)
    result=subprocess.run([str(path/'verify')],text=True,capture_output=True)
report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'C++17 translation of the actual C# KoppenClassifier method; numeric cross-check only. Managed/runtime semantics and Unity are NOT tested.','source_sha256':hashlib.sha256(source.encode()).hexdigest(),'fixtures':len(fixtures),'status':'PASS' if result.returncode==0 else 'FAIL','stdout':result.stdout,'stderr':result.stderr,'csharp_executed':False,'unity_editor_executed':False}
(ROOT/'Validation/FlatWorld/classifier-cpp-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print(json.dumps(report,ensure_ascii=False,indent=2));raise SystemExit(result.returncode)
