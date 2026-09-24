from pathlib import Path
import sys,json,re
sys.path.insert(0,str(Path(__file__).resolve().parents[3]/'validation_deps'))
from tree_sitter import Language,Parser
import tree_sitter_c_sharp
parser=Parser(Language(tree_sitter_c_sharp.language()));R=Path(__file__).resolve().parents[2]
errors=[];count=0
for p in (R/'Assets').rglob('*.cs'):
 data=p.read_bytes();tree=parser.parse(data);count+=1
 def visit(n):
  if n.type=='ERROR' or n.is_missing:errors.append((str(p.relative_to(R)),n.start_point,'syntax'))
  if n.type=='variable_declaration' and n.children and n.children[0].text==b'var' and sum(c.type=='variable_declarator' for c in n.children)>1:errors.append((str(p.relative_to(R)),n.start_point,'multiple implicit declarations'))
  for c in n.children:visit(c)
 visit(tree.root_node)
assert not errors,errors
report=dict(status='PASS',csharp_files_parsed=count,semantic_compilation=False,unity_executed=False)
(Path(__file__).parent/'source-report.json').write_text(json.dumps(report,indent=2));print(report)
