#!/usr/bin/env python3
"""Dependency-free lexical/resource checks. NOT a C# compiler or a Unity import test."""
import re,json,sys,hashlib,datetime,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
issues=[]; warnings=[]; counts={}
def issue(path,msg): issues.append({'file':str(path.relative_to(ROOT)),'message':msg})
def strip(source):
    # Preserve offsets/newlines; mask comments, regular/verbatim strings, char literals.
    pattern=r'//[^\n]*|/\*[\s\S]*?\*/|\$?@"(?:""|[^"])*"|@\$"(?:""|[^"])*"|\$?"(?:\\[\s\S]|[^"\\])*"|\'(?:\\[\s\S]|[^\'\\])*\''
    return re.sub(pattern,lambda m: re.sub(r'[^\n]',' ',m.group()),source)
def lexical(path):
    text=path.read_text(encoding='utf-8-sig'); clean=strip(text); stack=[]
    pairs={')':'(',']':'[','}':'{'}
    for pos,c in enumerate(clean):
        if c in '([{': stack.append((c,pos))
        elif c in pairs:
            if not stack or stack[-1][0]!=pairs[c]: issue(path,'unbalanced delimiter at line '+str(clean.count('\n',0,pos)+1));return
            stack.pop()
    if stack:issue(path,'unclosed delimiter(s)')
    pre=[]
    for n,line in enumerate(clean.splitlines(),1):
        if re.match(r'\s*#(?:if|ifdef|ifndef)\b',line):pre.append(n)
        elif re.match(r'\s*#endif\b',line):
            if not pre:issue(path,'unmatched #endif at '+str(n))
            else:pre.pop()
    if pre:issue(path,'unclosed #if')
for extension in ('.cs','.shader','.cginc','.hlsl','.uss'):
    paths=list((ROOT/'Assets').rglob('*'+extension)); counts[extension]=len(paths)
    for path in paths:
        try:lexical(path)
        except Exception as e:issue(path,str(e))
for path in (ROOT/'Assets').rglob('*.asmdef'):
    try:json.loads(path.read_text())
    except Exception as e:issue(path,str(e))
for path in (ROOT/'Packages').glob('*.json'):
    try:json.loads(path.read_text())
    except Exception as e:issue(path,str(e))
for path in (ROOT/'Assets').rglob('*.uxml'):
    try:ET.parse(path)
    except Exception as e:issue(path,str(e))
ui=ROOT/'Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatWorld.uxml'
app=ROOT/'Assets/ORBIS/Runtime/FlatWorld/Presentation/FlatWorldApp.cs'
ids=[e.attrib['name'] for e in ET.parse(ui).iter() if 'name' in e.attrib]
if len(ids)!=len(set(ids)):issue(ui,'Duplicate UXML names')
required=set(re.findall(r'(?:Bind\(|\.Q(?:<[^>]+>)?\()"([^"]+)"\s*(?=[,)])',app.read_text()))
for name in sorted(required-set(ids)):issue(app,'Missing UXML element '+name)
counts['ui_named_elements']=len(ids);counts['ui_bound_elements']=len(required)
# Resource paths are relative to any Resources folder; shaders included there survive stripping.
resource_files={}
for folder in (ROOT/'Assets').rglob('Resources'):
    for file in folder.rglob('*'):
        if file.is_file() and file.suffix!='.meta':resource_files.setdefault(str(file.relative_to(folder).with_suffix('')).replace('\\','/'),[]).append(file)
for file in (ROOT/'Assets/ORBIS/Runtime/FlatWorld').rglob('*.cs'):
    for name in re.findall(r'Resources\.Load(?:<[^>]+>)?\("([^"]+)"',file.read_text()):
        if name not in resource_files:issue(file,'Missing Resource '+name)
# Built-in CG includes are supplied by Unity, not copied into this project.
builtin={'UnityCG.cginc','Lighting.cginc','AutoLight.cginc','UnityPBSLighting.cginc','UnityShaderVariables.cginc','HLSLSupport.cginc','UnityStandardUtils.cginc','UnityInstancing.cginc'}
for file in (ROOT/'Assets').rglob('*'):
    if file.suffix not in {'.shader','.cginc','.hlsl'}:continue
    for inc in re.findall(r'#include\s+"([^"]+)"',file.read_text(encoding='utf-8-sig')):
        if inc in builtin or inc.startswith('Packages/'):continue
        if not (file.parent/inc).exists() and not (ROOT/inc).exists():issue(file,'Unresolved shader include '+inc)
guids={}
for file in (ROOT/'Assets').rglob('*.meta'):
    match=re.search(r'^guid:\s*([a-fA-F0-9]{32})',file.read_text(),re.M)
    if not match:issue(file,'No GUID');continue
    g=match.group(1)
    if g in guids:issue(file,'Duplicate GUID with '+str(guids[g].relative_to(ROOT)))
    guids[g]=file
for file in (ROOT/'Assets').rglob('*.unity'):
    for g in re.findall(r'm_Script:\s*\{[^}]*guid:\s*([a-fA-F0-9]{32})',file.read_text()):
        if g not in guids:issue(file,'Unresolved scene script GUID '+g)
for folder in ('Assets/ORBIS/Runtime/FlatWorld','Assets/ORBIS/Resources/ORBIS/FlatWorld'):
    for file in (ROOT/folder).rglob('*'):
        if file.suffix!='.meta' and not Path(str(file)+'.meta').exists():issue(file,'Missing Unity .meta')
for path in re.findall(r'path:\s*(Assets/[^\n]+)',(ROOT/'ProjectSettings/EditorBuildSettings.asset').read_text()):
    if not (ROOT/path.strip()).exists():issue(ROOT/'ProjectSettings/EditorBuildSettings.asset','Missing scene '+path)
editor=ROOT/'Assets/ORBIS/Editor/OrbisDemoEditor.cs'
editor_text=editor.read_text()
if 'EditorSceneManager.OpenScene(MainScenePath)' not in editor_text or 'scenes=new[]{MainScenePath,ScenePath}' not in editor_text:issue(editor,'First-open/build entry is not the new 2D main scene')
core=list((ROOT/'Assets/ORBIS/Runtime/FlatWorld/Core').glob('*.cs'))
for path in core:
    if re.search(r'\bUnity(?:Engine|Editor)\b',strip(path.read_text())):issue(path,'Unity API reference in noEngineReferences core')
counts['core_cs_files']=len(core);counts['meta_guids']=len(guids)
report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'C#/shader lexical structure, XML parse, literal resource references, GUID/build-scene consistency. No C# semantic compilation, Unity import, shader backend, Play Mode, image or FPS test was run.','status':'PASS' if not issues else 'FAIL','counts':counts,'issues':issues,'warnings':warnings,'unity_editor_executed':False}
out=ROOT/'Validation/FlatWorld/static-report.json';out.write_text(json.dumps(report,ensure_ascii=False,indent=2))
print(json.dumps(report,ensure_ascii=False,indent=2));sys.exit(bool(issues))
