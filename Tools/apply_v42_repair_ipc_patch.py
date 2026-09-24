#!/usr/bin/env python3
"""Apply ONLY this V4.2 Repair patch after hash checks; dry-run unless --apply is given."""
from pathlib import Path
import argparse, datetime, hashlib, json, shutil, sys

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None

def safe(root, relative):
    p=(root/relative).resolve()
    if not p.is_relative_to(root.resolve()):raise ValueError('Unsafe path: '+relative)
    return p

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('project',type=Path,help='Existing V4.2 Repair project root (contains Assets and ProjectSettings)')
    ap.add_argument('--apply',action='store_true',help='Actually write files. Close Unity first.')
    args=ap.parse_args();package=Path(__file__).resolve().parents[1];project=args.project.resolve()
    manifest=json.loads((package/'Documentation/V4_2_Repair_IPCFix/patch-manifest.json').read_text(encoding='utf-8'))
    if not (project/'ProjectSettings/ProjectVersion.txt').is_file():raise ValueError('Not a Unity project root.')
    # Do not apply while Unity holds this project. A stale lock is deliberately not deleted.
    if args.apply and (project/'Temp/UnityLockfile').exists():
        raise ValueError('UnityLockfile exists. Close Unity; do not apply to an open project.')
    # Validate exact shader baseline BEFORE copying ANY files, including new diagnostics.
    for item in manifest['modified_existing_files']:
        target=safe(project,item['path']);current=sha(target)
        if current not in (item['before_sha256'],item['after_sha256']):
            raise ValueError('Wrong version or local shader changes: '+item['path'])
    pending=[]
    modified={x['path']:x for x in manifest['modified_existing_files']}
    for item in manifest['payload']:
        relative=item['path'];src=safe(package,relative);dst=safe(project,relative)
        if sha(src)!=item['sha256']:raise ValueError('Patch content hash mismatch: '+relative)
        current=sha(dst)
        if current==item['sha256']:continue
        if current is not None and relative not in modified:
            raise ValueError('Refusing to overwrite a different additional/local file: '+relative)
        pending.append((relative,src,dst))
    print('Base shader hashes verified: V4.2 Repair. Pending files:',len(pending))
    for relative,_,_ in pending:print('  '+relative)
    if not args.apply:
        print('DRY RUN. No files changed. Close Unity and add --apply to install.');return 0
    if not pending:print('This patch is already installed.');return 0
    stamp=datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    backup=project/('ORBIS_IPCFix_Backup_'+stamp);backup.mkdir()
    # Back up all overwritten files before writing any destination.
    record=[]
    for relative,src,dst in pending:
        existed=dst.is_file()
        if existed:
            old=safe(backup,relative);old.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(dst,old)
        record.append({'path':relative,'existed_before':existed})
    (backup/'backup-record.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
    try:
        for relative,src,dst in pending:
            dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dst)
        manifest_dst=project/'Documentation/V4_2_Repair_IPCFix/patch-manifest.json'
        if package!=project and not manifest_dst.exists():
            shutil.copy2(package/'Documentation/V4_2_Repair_IPCFix/patch-manifest.json',manifest_dst)
    except Exception:
        # Roll back only files touched by this invocation, not entire folders.
        for item in record:
            dst=safe(project,item['path'])
            if item['existed_before']:shutil.copy2(safe(backup,item['path']),dst)
            elif dst.is_file():dst.unlink()
        raise
    print('Patch installed. Backup:',backup)
    print('Unity compilation is NOT verified by this installer.')
    print('Run Tools > ORBIS > V4.2 Repair > 编译云层并保存诊断 in Unity.')
    return 0
if __name__=='__main__':
    try:sys.exit(main())
    except Exception as e:print('STOP:',e,file=sys.stderr);sys.exit(1)
