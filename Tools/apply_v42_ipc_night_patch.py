#!/usr/bin/env python3
"""Apply NightLighting ONLY to the released V4.2 Repair IPCFix1 baseline.
Python 3.9+. Default dry-run; --apply backs up before atomic per-file replacement.
Does not start Unity, remove caches, change Packages/ProjectSettings, or compile.
"""
import argparse,datetime,hashlib,json,os,shutil,sys,tempfile,uuid
from pathlib import Path,PurePosixPath
SOURCE=Path(__file__).resolve().parents[1]
def digest(path):
 return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None

def safe_path(root,relative):
 rel=PurePosixPath(relative)
 if rel.is_absolute() or not rel.parts or any(p in ('..','.') or '\\' in p or ':' in p for p in rel.parts):
  raise ValueError('Unsafe relative path: '+relative)
 cur=root
 for part in rel.parts:
  cur=cur/part
  if cur.is_symlink():raise ValueError('Refusing symlink: '+str(cur))
 if cur.exists() and not cur.is_file():raise ValueError('Expected a file: '+str(cur))
 if not cur.resolve().is_relative_to(root.resolve()):raise ValueError('Path escaped project')
 return cur

def atomic_write(path,data):
 path.parent.mkdir(parents=True,exist_ok=True)
 fd,name=tempfile.mkstemp(prefix='.night-patch-',dir=str(path.parent))
 try:
  with os.fdopen(fd,'wb') as f:f.write(data);f.flush();os.fsync(f.fileno())
  os.replace(name,path)
 finally:
  if os.path.exists(name):os.unlink(name)

def install(target,apply=False):
 if target.is_symlink():raise ValueError('Refusing symlink project root')
 target=target.resolve()
 if not target.is_dir():raise ValueError('Project directory not found')
 if SOURCE==target:raise ValueError('Full project is already patched; do not install onto the patch source itself.')
 manifest=json.loads((SOURCE/'NIGHT_LIGHTING_MANIFEST.json').read_text(encoding='utf-8'))
 if (target/'Temp/UnityLockfile').exists():raise ValueError('UnityLockfile exists. Close Unity before installing; no lock or cache will be removed by this script.')
 for rel,wanted in manifest['baseline_sentinels'].items():
  if digest(safe_path(target,rel))!=wanted:raise ValueError('Not the matching IPCFix1 baseline: '+rel)
 seen=set();changes=[]
 for item in manifest['files']:
  rel=item['path']
  if rel in seen:raise ValueError('Duplicate manifest path: '+rel)
  seen.add(rel)
  src=safe_path(SOURCE,rel);dst=safe_path(target,rel)
  if digest(src)!=item['after_sha256']:raise ValueError('Patch payload integrity failed: '+rel)
  current=digest(dst)
  if current==item['after_sha256']:continue
  if current!=item['before_sha256']:raise ValueError('Custom/other-version file; refusing overwrite: '+rel)
  changes.append((item,src,dst,current))
 # The manifest is installed last and is not self-hashed.
 manifest_dst=safe_path(target,'NIGHT_LIGHTING_MANIFEST.json')
 if manifest_dst.exists() and manifest_dst.read_bytes()!=(SOURCE/'NIGHT_LIGHTING_MANIFEST.json').read_bytes():
  raise ValueError('A different night-lighting manifest is installed; refusing overwrite')
 summary={'mode':'APPLY' if apply else 'DRY_RUN','target':str(target),'files_to_write':len(changes),
  'compilation_performed':False,'caches_modified':False,'backup_directory':None}
 if not apply:return summary
 if not changes and manifest_dst.exists():summary['status']='ALREADY_APPLIED';return summary
 backup=target/('NightLighting_Backup_'+datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')+'_'+uuid.uuid4().hex[:8])
 backup.mkdir();summary['backup_directory']=str(backup)
 journal={'extension':manifest['extension'],'files':[],'state':'BACKING_UP'}
 for item,src,dst,current in changes:
  if digest(dst)!=current:raise ValueError('File changed during preflight: '+item['path'])
  if current is not None:
   b=backup/item['path'];b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(dst,b)
  journal['files'].append({'path':item['path'],'existed_before':current is not None,'before_sha256':current,'after_sha256':item['after_sha256']})
 log=backup/'INSTALL_JOURNAL.json';log.write_text(json.dumps(journal,indent=2)+'\n',encoding='utf-8')
 written=[]
 try:
  for item,src,dst,current in changes:
   if digest(dst)!=current:raise ValueError('File changed while installing: '+item['path'])
   atomic_write(dst,src.read_bytes());written.append((item,dst,current))
   if digest(dst)!=item['after_sha256']:raise IOError('Post-write hash mismatch: '+item['path'])
  atomic_write(manifest_dst,(SOURCE/'NIGHT_LIGHTING_MANIFEST.json').read_bytes())
  journal['state']='COMPLETED';summary['status']='APPLIED'
 except Exception:
  journal['state']='FAILED_ROLLBACK_ATTEMPTED'
  # Undo only files written by this invocation and unchanged since our write.
  for item,dst,current in reversed(written):
   if digest(dst)!=item['after_sha256']:continue
   if current is None:dst.unlink()
   else:atomic_write(dst,(backup/item['path']).read_bytes())
  log.write_text(json.dumps(journal,indent=2)+'\n',encoding='utf-8');raise
 log.write_text(json.dumps(journal,indent=2)+'\n',encoding='utf-8')
 return summary

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('project',type=Path);parser.add_argument('--apply',action='store_true')
 args=parser.parse_args()
 try:print(json.dumps(install(args.project,args.apply),indent=2,ensure_ascii=False));return 0
 except (ValueError,OSError,KeyError,TypeError,json.JSONDecodeError) as e:
  print('REFUSED: '+str(e),file=sys.stderr);return 1
if __name__=='__main__':sys.exit(main())
