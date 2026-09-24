using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace Orbis.Editor
{
    public sealed class OrbisWorldMapWindow:EditorWindow
    {
        TextField heightPath,riverPath;FloatField sea,span;Label status;
        [MenuItem("Tools/ORBIS/V4/Heightmap and River Map")]
        public static void Open(){GetWindow<OrbisWorldMapWindow>("ORBIS 雙圖建圖");}
        public void CreateGUI()
        {
            var root=rootVisualElement;root.style.paddingLeft=12;root.style.paddingRight=12;root.style.paddingTop=12;
            var intro=new Label("2:1 全球等經緯圖，兩圖同尺寸且地理對齊。支援 8/16 位灰度 PNG、未壓縮 BMP。像素值按線性資料讀取；不是相片的明暗。國家與人口請使用完整世界存檔。");intro.style.whiteSpace=WhiteSpace.Normal;root.Add(intro);
            heightPath=new TextField("高度圖");riverPath=new TextField("河流圖（可空）");root.Add(heightPath);
            root.Add(new Button(()=>{string p=EditorUtility.OpenFilePanelWithFilters("選取灰度高度圖","",new[]{"PNG / BMP","png,bmp"});if(!string.IsNullOrEmpty(p))heightPath.value=p;}){text="選擇高度图文件"});root.Add(riverPath);
            root.Add(new Button(()=>{string p=EditorUtility.OpenFilePanelWithFilters("選取河流中心線图","",new[]{"PNG / BMP","png,bmp"});if(!string.IsNullOrEmpty(p))riverPath.value=p;}){text="選擇河流图文件"});
            sea=new FloatField("海平面灰度 / 0–1"){value=.475f};span=new FloatField("歸一化高度跨度"){value=2};root.Add(sea);root.Add(span);
            root.Add(new Button(ValidateRaster){text="只校验圖像，不替換世界"});
            root.Add(new Button(Import){text="在 Play Mode 中建立世界（先備份）"});
            root.Add(new Button(Export){text="匯出正在執行的世界雙圖"});
            status=new Label("請先打開 OrbisDemo 場景；導入世界需要 Play Mode。");status.style.whiteSpace=WhiteSpace.Normal;root.Add(status);
        }
        OrbisWorldMap Read()=>OrbisMapIO.Read(heightPath.value,riverPath.value,sea.value,span.value);
        void ValidateRaster()
        {
            try{var m=Read();status.text="圖像校驗通過："+m.width+"×"+m.height+"，"+(m.HasRivers?"含河流中心線。河道坡度在建圖時進一步檢查。":"無河流中心線，將由高度生成河網。");}
            catch(Exception e){status.text=e.Message;Debug.LogException(e);}
        }
        static OrbisDemo Running()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("請先按 Play，再匯入或匯出。");
            var demo=UnityEngine.Object.FindFirstObjectByType<OrbisDemo>();if(!demo)throw new InvalidOperationException("場景沒有 OrbisDemo。");return demo;
        }
        void Import()
        {
            try
            {
                var demo=Running();var source=Read();
                if(!EditorUtility.DisplayDialog("建立新的雙圖世界？","目前世界會另存備份；現有國家與人口不會搬到新高度圖。","建立","取消"))return;
                demo.ApplyWorldMap(source);status.text=demo.Notice;
            }catch(Exception e){status.text=e.Message;Debug.LogException(e);}
        }
        void Export()
        {
            try{string path=OrbisMapIO.Export(Running().View);status.text="已匯出："+path;EditorUtility.RevealInFinder(path);}
            catch(Exception e){status.text=e.Message;Debug.LogException(e);}
        }
    }
}
