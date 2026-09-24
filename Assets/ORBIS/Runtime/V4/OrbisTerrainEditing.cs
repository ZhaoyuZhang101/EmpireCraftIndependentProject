using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace Orbis
{
    public sealed partial class OrbisWorld
    {
        /// <summary>Edit authoritative simulation heights; every mode shares the globe/map data.
        /// Radius is chord distance on the unit sphere, not a latitude-dependent pixel radius.</summary>
        public int ApplyTerrainBrush(int tile,int mode,float radius,float strength,float target)
        {
            if(tile<0||tile>=tiles.Length||(mode!=3&&mode!=4&&mode!=6&&mode!=7))return 0;
            if(float.IsNaN(radius)||float.IsInfinity(radius)||float.IsNaN(strength)||float.IsInfinity(strength)||float.IsNaN(target)||float.IsInfinity(target))
                throw new ArgumentException("Non-finite terrain brush parameter.");
            radius=Mathf.Clamp(radius,.02f,.35f);strength=Mathf.Clamp(strength,.01f,.25f);target=Mathf.Clamp(target,-.5f,1f);
            var original=new float[tiles.Length];for(int i=0;i<tiles.Length;i++)original[i]=tiles[i].h;
            Vector3 center=grid.points[tile];int changed=0;
            for(int i=0;i<tiles.Length;i++)
            {
                float distance=Vector3.Distance(center,grid.points[i]);if(distance>=radius)continue;
                float t=1-distance/radius,w=t*t*(3-2*t),delta;
                if(mode==3||mode==4)delta=w*strength*(mode==3?1:-1);
                else
                {
                    float goal=target;
                    if(mode==6){goal=original[i];foreach(int j in grid.neighbors[i])goal+=original[j];goal/=grid.neighbors[i].Length+1;}
                    delta=(goal-original[i])*Mathf.Min(1,strength*4)*w;
                }
                float edit=Mathf.Clamp(tiles[i].edit+delta,-3,3);
                if(Mathf.Abs(edit-tiles[i].edit)<1e-7f)continue;
                tiles[i].edit=edit;changed++;
            }
            if(changed>0)Terrain(); // Reuses cached tectonic base; drainage/shorelines are globally revalidated.
            return changed;
        }
    }

    public sealed partial class OrbisDemo
    {
        sealed class TerrainSnapshot
        {
            public float[] edits,population;public int[] owners,capitals;public bool[] cities;
            public static TerrainSnapshot Capture(OrbisWorld w)
            {
                int n=w.tiles.Length;var s=new TerrainSnapshot{edits=new float[n],population=new float[n],owners=new int[n],cities=new bool[n],capitals=new int[w.civs.Count]};
                for(int i=0;i<n;i++){var t=w.tiles[i];s.edits[i]=t.edit;s.population[i]=t.pop;s.owners[i]=t.owner;s.cities[i]=t.city;}
                for(int i=0;i<s.capitals.Length;i++)s.capitals[i]=w.civs[i].capital;return s;
            }
            public bool Matches(OrbisWorld w)
            {
                if(w.tiles.Length!=edits.Length||w.civs.Count!=capitals.Length)return false;
                for(int i=0;i<edits.Length;i++){var t=w.tiles[i];if(t.edit!=edits[i]||t.pop!=population[i]||t.owner!=owners[i]||t.city!=cities[i])return false;}
                for(int i=0;i<capitals.Length;i++)if(w.civs[i].capital!=capitals[i])return false;return true;
            }
            public void Restore(OrbisWorld w)
            {
                for(int i=0;i<edits.Length;i++)w.tiles[i].edit=edits[i];
                w.Terrain();
                // Restore population AFTER terrain relocation; undo must also undo brush-caused flooding.
                for(int i=0;i<edits.Length;i++){var t=w.tiles[i];t.pop=population[i];t.owner=owners[i];t.city=cities[i];}
                for(int i=0;i<capitals.Length;i++)w.civs[i].capital=capitals[i];w.UpdateStats();
            }
        }
        sealed class TerrainCommand{public TerrainSnapshot before,after;}
        readonly List<TerrainCommand> terrainUndo=new List<TerrainCommand>(),terrainRedo=new List<TerrainCommand>();
        OrbisWorld terrainHistoryWorld;int terrainHistoryRevision=-1;
        static readonly ProfilerMarker EditMarker=new ProfilerMarker("ORBIS.Terrain.BrushCommit");
        public double LastTerrainEditMilliseconds{get;private set;}
        public int TerrainUndoCount=>terrainHistoryWorld==World&&terrainHistoryRevision==World.Revision&&Paused?terrainUndo.Count:0;
        public int TerrainRedoCount=>terrainHistoryWorld==World&&terrainHistoryRevision==World.Revision&&Paused?terrainRedo.Count:0;
        public void ClearTerrainHistory(){terrainUndo.Clear();terrainRedo.Clear();terrainHistoryWorld=World;terrainHistoryRevision=World.Revision;}
        void PrepareTerrainHistory()
        {
            if(terrainHistoryWorld!=World||terrainHistoryRevision!=World.Revision||!Paused)ClearTerrainHistory();
        }
        public void SetFlattenFromSelection()
        {
            if(Selected<0)return;World.settings.terrainFlattenHeight=Mathf.Clamp(World.tiles[Selected].h,-.5f,1);
            hud.SyncSettings();Notice="整平目標已取自所選地塊；這是遊戲高程，不是實測測繪高度。";hud.Refresh();
        }
        void EditTerrain(int tile,int mode)
        {
            PrepareTerrainHistory();Paused=true;AutoOrbit=false;StopAtmosphereFlight();
            TerrainSnapshot before=TerrainSnapshot.Capture(World);double start=Time.realtimeSinceStartupAsDouble;
            try
            {
                using(EditMarker.Auto())
                {
                    var s=World.settings;int changed=World.ApplyTerrainBrush(tile,mode,s.terrainBrushRadius,s.terrainBrushStrength,s.terrainFlattenHeight);
                    if(changed==0){Notice="筆刷沒有改變地形。";hud.Refresh();return;}
                    if(World.MapSource!=null)
                    {
                        var rivers=new OrbisRiverNetwork();int hint=0;
                        rivers.Build(World,p=>{var sample=View.SurfaceSampler.Locate(p,hint);hint=sample.a;return View.TerrainHeight(p,sample);});
                        if(rivers.MaxUpliftWarnings>0)throw new InvalidOperationException("編輯使導入河線與高度圖衝突："+rivers.MaxUpliftWarnings+" 個抬升警告。請調整河線或在其他區域編輯。");
                    }
                    var command=new TerrainCommand{before=before,after=TerrainSnapshot.Capture(World)};
                    terrainUndo.Add(command);if(terrainUndo.Count>16)terrainUndo.RemoveAt(0);terrainRedo.Clear();
                    terrainHistoryWorld=World;terrainHistoryRevision=World.Revision;CommitTerrainViews();
                    Notice="已修改 "+changed+" 個地塊；海岸、河網與自然分區已更新。編輯已暫停模擬，可撤銷／重做。";
                }
            }
            catch(Exception e)
            {
                Debug.LogException(e);before.Restore(World);ClearTerrainHistory();CommitTerrainViews();Notice="地形編輯未完成，已還原本次編輯："+e.Message;
            }
            finally{LastTerrainEditMilliseconds=(Time.realtimeSinceStartupAsDouble-start)*1000;hud.Refresh();}
        }
        public void UndoTerrain(){TravelTerrainHistory(false);}
        public void RedoTerrain(){TravelTerrainHistory(true);}
        void TravelTerrainHistory(bool redo)
        {
            PrepareTerrainHistory();var from=redo?terrainRedo:terrainUndo;var to=redo?terrainUndo:terrainRedo;
            if(from.Count==0){Notice="沒有可用的地形編輯歷史；繼續模擬或更換世界會清空歷史。";hud.Refresh();return;}
            var command=from[from.Count-1];var expected=redo?command.before:command.after;
            if(!expected.Matches(World)){ClearTerrainHistory();Notice="世界已被其他操作改變，已清空歷史，避免覆蓋人口或領土。";hud.Refresh();return;}
            double start=Time.realtimeSinceStartupAsDouble;
            try
            {
                (redo?command.after:command.before).Restore(World);from.RemoveAt(from.Count-1);to.Add(command);
                terrainHistoryRevision=World.Revision;CommitTerrainViews();Notice=redo?"已重做地形編輯。":"已撤銷地形編輯與本次淹水造成的人口遷移。";
            }
            catch(Exception e){Debug.LogException(e);expected.Restore(World);ClearTerrainHistory();CommitTerrainViews();Notice="歷史還原未完成，已回復操作前狀態。";}
            finally{LastTerrainEditMilliseconds=(Time.realtimeSinceStartupAsDouble-start)*1000;hud.Refresh();}
        }
        void CommitTerrainViews()
        {
            View.RebuildGeometry();View.UpdateWeather(true);lastRevision=World.Revision;
            if(MapMode)Atlas.Refresh(true);SelectionView.Invalidate();overlays.Tick(0,true);markerAge=0;
        }
    }
}
