"""Run the supplied World Atlas engine and export physical elevation, never map colors."""
from pathlib import Path
import argparse, hashlib, json, struct, zipfile
import numpy as np
from world_atlas.core.procedural_planet import PlanetRecipe, generate_planet_surface

TERRAIN_REVISION = "spherical-relief-2"

def seed_hash(text):
    value=2166136261
    for c in text:
        value=((value ^ ord(c))*16777619)&0xffffffff
    return value

def export(seed, width, output):
    recipe=PlanetRecipe(seed_hash(seed),width,width//2,14,8,.35,.34,.82,.28,False,False)
    surface=generate_planet_surface(recipe, spherical_relief=True)
    # Source surface is north-first. ORBIS's authoritative array is south-first.
    bed=np.where(surface.land_mask,np.maximum(1,surface.elevation*4000),-np.maximum(1,surface.bathymetry*4000))
    bed=np.rint(np.clip(bed,-4000,4000)).astype('<i2')[::-1].copy()
    target=Path(output);target.parent.mkdir(parents=True,exist_ok=True)
    content=struct.pack('<4sIII',b'OAT1',width,width//2,seed_hash(seed))+bed.tobytes()
    temporary=target.with_suffix('.tmp');temporary.write_bytes(content);temporary.replace(target)
    target.with_suffix('.json').write_text(json.dumps({'engine':'World Atlas 1.3.0','terrainRevision':TERRAIN_REVISION,'seed':seed,'seedHash':seed_hash(seed),'width':width,'height':width//2,'southFirst':True,'metersPerUnit':1,'sha256':hashlib.sha256(content).hexdigest(),'hydrology':'ORBIS spherical drainage and spill lakes rebuilt from this physical bed','diagnostics':dict(surface.diagnostics)},ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'output':str(target),'landFraction':float(surface.land_mask.mean()),'minMeters':int(bed.min()),'maxMeters':int(bed.max())}),flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--seed',default='ORBIS-2D-2026');p.add_argument('--seed-file');p.add_argument('--width',type=int,default=512);p.add_argument('--output',required=True)
    a=p.parse_args()
    if a.width not in (256,512,1024):p.error('width must be 256, 512 or 1024')
    export(Path(a.seed_file).read_text(encoding="utf-8-sig") if a.seed_file else a.seed,a.width,a.output)
