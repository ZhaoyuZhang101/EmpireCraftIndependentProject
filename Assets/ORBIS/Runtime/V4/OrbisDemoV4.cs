using System;
using System.IO;
using UnityEngine;
namespace Orbis
{
    public sealed partial class OrbisDemo
    {
        public OrbisSelectionView SelectionView{get;private set;}
        public int ActiveNation{get;private set;}=-1;
        float nextHover;
        public void Hover(Vector2 normalized)
        {
            if(Time.unscaledTime<nextHover)return;nextHover=Time.unscaledTime+.08f;
            SelectionView.Hovered=TryPick(normalized,out int tile)?tile:-1;
        }
        public void ClearHover(){if(SelectionView)SelectionView.Hovered=-1;}
        public void SetSelectionMode(int value){World.settings.selectionMode=Mathf.Clamp(value,0,2);SelectionView.Invalidate();hud.Refresh();}
        public void SelectNationFromTile()
        {
            ActiveNation=Selected>=0?World.tiles[Selected].owner:-1;
            Notice=ActiveNation>=0?"當前圈地國家："+World.civs[ActiveNation].name:"所選地區沒有國家；請先建立或選取已有國家。";hud.Refresh();
        }
        public void FoundSelectedNation()
        {
            int id=World.FoundNation(Selected,World.settings.selectionMode);
            if(id>=0){ActiveNation=id;Notice="已建立 "+World.civs[id].name+"；首府 500 人，領土為所點 1 個 4×4 Zone。";}
            else Notice="建立失敗：選區必須無主且包含可居住陸地，文明數不得超過 12。";
            RefreshTerritories();
        }
        public void ClaimSelectedRegion()
        {
            Notice=World.ClaimRegion(Selected,World.settings.selectionMode,ActiveNation)?"已納入所點 1 個相鄰無主 4×4 Zone（16 小地塊）。":"圈地失敗：先選擇國家；選區須與國土相鄰，不能覆蓋他國領土。";RefreshTerritories();
        }
        void RefreshTerritories(){World.UpdateStats();View.UpdateCities();View.UpdateColors(true);if(MapMode)Atlas.Refresh();overlays.Tick(0,true);hud.Refresh();}
        public void ReportV4(string message){Notice=message;hud.Refresh();}
        public bool ApplyWorldMap(OrbisWorldMap source)
        {
            OrbisWorld previous=World;
            try
            {
                // Parse, generate and validate the replacement before swapping the live state.
                var next=new OrbisWorld(World.seed);
                next.settings=JsonUtility.FromJson<PlanetSettings>(JsonUtility.ToJson(World.settings));next.settings.selectionMode=0;next.settings.regionExpansion=true;
                next.ImportWorldMap(source);
                var check=new OrbisRiverNetwork();check.Build(next,p=>source.Sample(p));
                if(check.MaxUpliftWarnings>0)throw new InvalidDataException("河线与高度图冲突："+check.MaxUpliftWarnings+" 个节点需要明显抬高水面。请调整河线/高度，避免河道逆坡或跨山。原世界未替换。");
                Directory.CreateDirectory(Application.persistentDataPath);
                string backup=Path.Combine(Application.persistentDataPath,"before-map-import-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".json");
                File.WriteAllText(backup,JsonUtility.ToJson(World.Save()));
                World=next;ClearTerrainHistory();View.world=next;View.RebuildGeometry();View.UpdateWeather();Atlas.SetWorld(next);
                lastRevision=World.Revision;visualClock=0;simulationAccumulator=0;VisualRotation=0;ActiveNation=-1;
                hasSuspendedFocus=false;mapEntrySelection=-2;SelectionView.Hovered=-1;SelectionView.Invalidate();FocusBestLand();
                if(MapMode)Atlas.Refresh();hud.SyncSettings();overlays.Tick(0,true);
                Notice="雙圖世界已載入："+source.width+"×"+source.height+"，河網 "+check.ImportedComponents+" 組，內陸終點 "+check.ClosedComponents+" 組。舊世界已自動備份。";
                hud.Refresh();return true;
            }
            catch(Exception e)
            {
                if(World!=previous){World=previous;View.world=previous;Atlas.SetWorld(previous);View.RebuildGeometry();View.UpdateWeather();if(MapMode)Atlas.Refresh();}
                Debug.LogException(e);Notice="雙圖建圖未完成："+e.Message;hud.Refresh();return false;
            }
        }
        public void ImportMapFolder(string folder)
        {try{ApplyWorldMap(OrbisMapIO.ReadFolder(folder));}catch(Exception e){Debug.LogException(e);ReportV4("讀取失敗："+e.Message);}}
        public void LoadExampleMap(){try{ApplyWorldMap(OrbisMapIO.Example());}catch(Exception e){ReportV4(e.Message);}}
        public void ExportMaps(){try{ReportV4("雙圖模板已匯出："+OrbisMapIO.Export(View));}catch(Exception e){Debug.LogException(e);ReportV4("匯出失敗："+e.Message);}}
        public void OpenMapImportFolder(){Directory.CreateDirectory(OrbisMapIO.ImportFolder);Application.OpenURL(new Uri(OrbisMapIO.ImportFolder+Path.DirectorySeparatorChar).AbsoluteUri);}
    }
}
