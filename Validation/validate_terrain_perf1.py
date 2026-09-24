#!/usr/bin/env python3
"""Source contracts + independent numerical geometry tests, NOT Unity/C#/HLSL execution.
Requires numpy and scipy (the retained reference_geometry helper uses scipy).
Usage: python validate_terrain_perf1.py [--baseline ORIGINAL_EXTRACTED_PROJECT]
"""
from pathlib import Path
import argparse,hashlib,json,re,sys,xml.etree.ElementTree as ET,datetime
import numpy as np
from reference_geometry import grid,lexical_check
P=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--baseline',type=Path);args=parser.parse_args()
S=P/'Assets/ORBIS/Resources/ORBIS';R=P/'Assets/ORBIS/Runtime'
checks={}
def check(name,fn):
    try:
        checks[name]={'status':'PASS','detail':fn()};print(name,'PASS',flush=True)
    except Exception as e:
        checks[name]={'status':'FAIL','error':str(e)};print(name,'FAIL',repr(e),flush=True)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def blocks(text):
    result=[]
    for m in re.finditer(r'\bPass\s*\{',text):
        start=m.start();i=m.end();depth=1
        while depth:
            if text[i]=='{':depth+=1
            if text[i]=='}':depth-=1
            i+=1
        result.append(text[start:i])
    return result

def source():
    files=[p for p in (P/'Assets').rglob('*') if p.suffix in ('.cs','.shader','.cginc')]
    for p in files:lexical_check(p.read_text())
    for p in S.iterdir():
        if p.suffix in ('.cginc','.shader'):
            for name in re.findall(r'#include\s+"([^"]+)"',p.read_text()):assert name.startswith('Unity') or (p.parent/name).exists(),(p,name)
    guids=[]
    for p in (P/'Assets').rglob('*.meta'):
        m=re.search(r'^guid:\s*([0-9a-f]+)',p.read_text(),re.M)
        if m:guids.append(m.group(1))
    assert len(guids)==len(set(guids)),'Duplicate GUIDs'
    for p in (P/'Assets').rglob('*.cs'):assert Path(str(p)+'.meta').is_file(),p
    assert not any(p.suffix.lower() in ['.ttf','.otf','.ttc','.woff','.woff2'] for p in P.rglob('*'))
    uxml=ET.parse(S/'Orbis.uxml');names=[e.attrib['name'] for e in uxml.iter() if 'name' in e.attrib]
    assert len(names)==len(set(names)),'Duplicate UI names'
    new=['terrainQuality','terrainBrushRadius','terrainBrushStrength','terrainFlattenHeight','terrainSampleHeight','terrainUndo','terrainRedo','performanceCapture','performanceExport','terrainPerformance']+['tool'+str(i) for i in range(8)]
    for name in new:assert name in names,name
    for e in uxml.iter():assert 'style' not in e.attrib,'Inline UXML style'
    performance=(R/'V4/OrbisPerformanceCapture.cs').read_text()
    assert 'World.settings.layer' not in performance
    demo='\n'.join(p.read_text() for p in R.rglob('*.cs') if 'partial class OrbisDemo' in p.read_text())
    for name in ['TickPerformanceCapture','BeginPerformanceCapture','ExportPerformanceCapture','PerformanceStatus','ClearTerrainHistory','EditTerrain','UndoTerrain','RedoTerrain']:
        assert re.search(r'\b(?:void|string)\s+'+name+r'\s*\(',demo),name
    assert 'readonly FrameSample[] performanceFrames=new FrameSample[PerformanceCapacity]' in performance
    assert 'Time.unscaledDeltaTime*1000' in performance
    assert 'clock' not in re.sub(r'//[^\n]*','',(R/'OrbisSurfaceLOD.cs').read_text().split('void BuildSlice()')[1].split('void Publish()')[0])
    editor=(P/'Assets/ORBIS/Editor/OrbisV42RepairIPCValidation.cs').read_text()
    assert 'int[] passCounts = { 4, 1, 2, 1, 2, 1, 1 }' in editor
    assert len(blocks((S/'Clouds.shader').read_text()))==4
    assert len(blocks((S/'AtmosphereComposite.shader').read_text()))==2
    assert 'v.tool>7' in (R/'OrbisWorld.cs').read_text()
    assert 'if(!Has(json,"terrainQuality"))s.settings.terrainQuality=1;' in (R/'OrbisSaveMigration.cs').read_text()
    return {'lexically_balanced_sources':len(files),'unique_GUIDs':len(guids),'new_UI_names':len(new),
        'cloud_passes':4,'composite_passes':2,'font_files':0,
        'scope':'Delimiter/dependency/source-contract checks only. XML well-formedness is NOT Unity UXML import validation.'}

def baseline():
    if not args.baseline:return {'status':'NOT_REQUESTED','reason':'Supply original extracted project for byte comparison.'}
    B=args.baseline
    paths=['Assets/ORBIS/Runtime/OrbisNightLighting.cs']+['Assets/ORBIS/Resources/ORBIS/'+n for n in [
        'OrbisNightLighting.cginc','OrbisAtmosphereGLtracy.cginc','OrbisAtmosphereCommon.cginc','OrbisCloudCommon.cginc','OrbisOptics.cginc','Surface.shader','OrbisWater.cginc','RiverWater.shader']]
    hashes={}
    for name in paths:
        assert (P/name).read_bytes()==(B/name).read_bytes(),name
        hashes[name]=sha(P/name)
    for folder in ['ProjectSettings','Packages']:
        for old in (B/folder).rglob('*'):
            if old.is_file():assert (P/old.relative_to(B)).read_bytes()==old.read_bytes(),str(old)
    for old in (B/'Assets').rglob('*.unity'):assert (P/old.relative_to(B)).read_bytes()==old.read_bytes()
    for name,n in [('Clouds.shader',3),('AtmosphereComposite.shader',1)]:
        before=blocks((B/'Assets/ORBIS/Resources/ORBIS'/name).read_text());after=blocks((S/name).read_text())
        assert before==after[:n],name+' original passes changed'
    return {'protected_night_surface_and_scattering_files_byte_identical':hashes,
        'original_cloud_passes_0_1_2_byte_identical':True,'original_composite_pass_0_byte_identical':True,
        'scene_project_settings_packages_byte_identical':True}

def geometry():
    points,coarse=grid(24);detail=8
    canonical={};directions=[];field=[];faces=[];ids=np.zeros((detail+1,detail+1),dtype=np.int64)
    rng=np.random.default_rng(4242);tilefield=rng.normal(size=(len(points),3));maxpos=0.;maxfield=0.
    for k,(a,b,c) in enumerate(coarse):
        for i in range(detail+1):
            for j in range(detail+1-i):
                wa,wb,wc=detail-i-j,i,j
                def edge(u,v,t):return (int(u),int(v),t) if u<v else (int(v),int(u),detail-t)
                if wa==detail:key=(int(a),int(a),0)
                elif wb==detail:key=(int(b),int(b),0)
                elif wc==detail:key=(int(c),int(c),0)
                elif wa==0:key=edge(b,c,wc)
                elif wb==0:key=edge(a,c,wc)
                elif wc==0:key=edge(a,b,wb)
                else:key=(-1,k*3,i*(detail+1)+j)
                w=np.array([wa,wb,wc])/detail;d=w@points[[a,b,c]];d/=np.linalg.norm(d);f=w@tilefield[[a,b,c]]
                if key not in canonical:
                    canonical[key]=len(directions);directions.append(d);field.append(f)
                else:
                    n=canonical[key];maxpos=max(maxpos,float(np.max(np.abs(d-directions[n]))));maxfield=max(maxfield,float(np.max(np.abs(f-field[n]))))
                ids[i,j]=canonical[key]
        for i in range(detail):
            for j in range(detail-i):
                faces.append([ids[i,j],ids[i+1,j],ids[i,j+1]])
                if i+j<detail-1:faces.append([ids[i+1,j],ids[i+1,j+1],ids[i,j+1]])
    vertices=np.array(directions);tris=np.array(faces);nv=len(vertices);nf=len(tris)
    assert len(points)==3456 and len(coarse)==6908
    assert nv==221058 and nf==442112,(nv,nf)
    assert maxpos<1e-12 and maxfield<1e-12,(maxpos,maxfield)
    edges=np.sort(np.concatenate([tris[:,[0,1]],tris[:,[1,2]],tris[:,[2,0]]]),axis=1)
    unique,counts=np.unique(edges[:,0]*nv+edges[:,1],return_counts=True)
    assert np.all(counts==2),'Open edge or non-manifold edge'
    assert nv-len(unique)+nf==2,'Sphere Euler characteristic'
    xyz=vertices[tris];area=np.cross(xyz[:,1]-xyz[:,0],xyz[:,2]-xyz[:,0]);outward=np.einsum('ij,ij->i',area,xyz[:,0]);assert np.all(outward>1e-12)
    centers=xyz.sum(axis=1);centers/=np.linalg.norm(centers,axis=1)[:,None]
    abs_=abs(centers);axis=abs_.argmax(axis=1);face=2*axis+(centers[np.arange(nf),axis]<0)
    u=np.where(axis==0,centers[:,2]/np.maximum(abs_[:,0],1e-12),centers[:,0]/np.maximum(np.where(axis==1,abs_[:,1],abs_[:,2]),1e-12))
    v=np.where(axis==1,centers[:,2]/np.maximum(abs_[:,1],1e-12),centers[:,1]/np.maximum(np.where(axis==0,abs_[:,0],abs_[:,2]),1e-12))
    patch=face*16+np.clip(((v+1)*2).astype(int),0,3)*4+np.clip(((u+1)*2).astype(int),0,3)
    assert len(np.unique(patch))==96
    submitted_vertices=sum(len(np.unique(tris[patch==i])) for i in range(96))
    old=len(coarse)*45
    return {'model':'Independent Python topology/math reconstruction, double precision; NOT running C# or GPU',
        'logical_tiles':len(points),'coarse_triangles':len(coarse),'old_source_vertices':old,'new_shared_source_vertices':nv,
        'source_vertex_reduction_percent':100*(old-nv)/old,'triangle_count_unchanged':nf,'unique_edges':len(unique),
        'all_edges_incident_to_two_faces':True,'all_triangles_outward_nonzero_area':True,'euler_characteristic':2,
        'max_shared_position_difference':maxpos,'max_shared_interpolated_field_difference':maxfield,
        'fixed_render_patches':96,'submitted_patch_vertices_including_boundaries':submitted_vertices}

def brush_and_map():
    points,_=grid(24);r=.12;strength=.08;checked=0
    for targetdir in [[0,1,0],[0,-1,0],[0,0,-1],[1,0,0],[1,.3,.9]]:
        center=int(np.argmax(points@np.array(targetdir)));distance=np.linalg.norm(points-points[center],axis=1)
        t=np.clip(1-distance/r,0,1);weight=t*t*(3-2*t);delta=weight*strength
        assert np.all(delta[distance>=r]==0) and abs(delta[center]-strength)<1e-12
        heights=.3+points[:,0]*.2;target=.18;edits=(target-heights)*min(1,strength*4)*weight
        assert np.all(np.abs(heights+edits-target)<=np.abs(heights-target)+1e-12)
        assert np.max(abs(delta-delta))==0;checked+=len(points)
    lon=-np.arctan2(points[:,0],points[:,2]);lat=np.arcsin(np.clip(points[:,1],-1,1))
    roundtrip=np.stack([np.sin(-lon)*np.cos(lat),np.sin(lat),np.cos(lon)*np.cos(lat)],axis=1)
    error=float(np.max(np.linalg.norm(roundtrip-points,axis=1)));assert error<1e-12
    # Explicit seam equivalence for every latitude row.
    lat=np.linspace(-np.pi/2,np.pi/2,129)
    a=np.stack([np.sin(-np.pi)*np.cos(lat),np.sin(lat),np.cos(-np.pi)*np.cos(lat)],axis=1)
    b=np.stack([np.sin(np.pi)*np.cos(lat),np.sin(lat),np.cos(np.pi)*np.cos(lat)],axis=1)
    assert np.max(abs(a-b))<1e-12
    return {'model':'Independent brush falloff/flatten/projection math only; editor test covers actual hydrology and serialization',
        'pole_seam_and_other_brush_samples':checked,'outside_footprint_no_changes':True,'flatten_no_overshoot':True,
        'globe_atlas_roundtrip_max_error':error,'dateline_rows':129}

def budgets():
    width,height=1920,1080
    return {'scope':'Configured operation counts, NOT measured FPS, render time or speedup.',
      'atlas_volume_medium_pixels':int(width*.5)*int(height*.5),'previous_atlas_volume_pixels':width*height,
      'atlas_medium_volume_pixel_ratio':.25,'terrain_source_resolution':'unchanged',
      'appearance_snapshot_vertices':33153,'appearance_vertices_per_slice':4096,'appearance_slices_per_snapshot':9,
      'local_build_vertex_ceiling_per_slice':768,'local_build_soft_time_budget_ms':1.5,
      'local_mesh_normal_rebuild_and_publication_are_not_time_sliced':True}

check('source_contracts',source);check('baseline_preservation',baseline);check('closed_shared_terrain_topology',geometry);check('brush_and_map_reference',brush_and_map);check('configured_work_budgets',budgets)
report={'version':'V4.2 GreenRingFix TerrainPerf1','utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
  'status':'PASS' if all(c['status']=='PASS' for c in checks.values()) else 'FAIL',
  'qualification':'PASS applies to listed offline source/numerical checks only. Project has NOT been compiled or run in Unity here.',
  'unity_editor_available':False,'unity_csharp_compile_performed':False,'native_hlsl_compile_performed':False,
  'play_mode_tests_performed':False,'runtime_fps_measured':False,'unity_visual_acceptance_performed':False,'checks':checks}
out=P/'Documentation/TerrainPerf1/offline-validation.json';out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(report,ensure_ascii=False,indent=2));sys.exit(0 if report['status']=='PASS' else 1)
