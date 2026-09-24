using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Orbis
{
    // Records frame intervals, not CPU/GPU execution time. No automatic upload or file writes.
    public sealed partial class OrbisDemo
    {
        const int PerformanceCapacity=1800;
        struct FrameSample
        {
            public float ms,altitude;public int frame,layer,cloudQuality,terrainQuality,visiblePatches,localVertices,pendingVertices;
            public bool map,paused,focused,localBuilding;public double editMilliseconds;
        }
        [Serializable] sealed class FrameReport
        {
            public string version="V4.2 GreenRingFix TerrainPerf1",startedUtc,exportedUtc,unityVersion,operatingSystem,device,graphicsAPI,seed;
            public string scope="Measured unscaled frame intervals, including waits/Editor overhead. NOT separated CPU/GPU timings or a controlled benchmark. Context columns describe the start of the recorded frame.";
            public int samples,screenWidth,screenHeight,targetFrameRate,vSyncCount,cloudQuality,terrainQuality,initialLayer,initialWorldRevision,initialWorldRuntimeId;
            public bool editor,startedInMap,startedPaused,contextChangedDuringCapture;
            public double meanMs,p50Ms,p95Ms,p99Ms,maxMs,framesPerSecondFromMean;
        }
        readonly FrameSample[] performanceFrames=new FrameSample[PerformanceCapacity];
        FrameReport performanceReport;int performanceCount,performanceStartFrame;
        bool performanceRecording;string performanceSummary="尚未記錄幀時間";
        float recentFrameMilliseconds;double nextPerformanceDisplay;
        public string LastPerformanceExportPath{get;private set;}
        public void BeginPerformanceCapture()
        {
            if(World==null)return;
            performanceCount=0;performanceRecording=true;performanceStartFrame=Time.frameCount;LastPerformanceExportPath=null;
            performanceReport=new FrameReport{startedUtc=DateTime.UtcNow.ToString("o"),unityVersion=Application.unityVersion,
                operatingSystem=SystemInfo.operatingSystem,device=SystemInfo.graphicsDeviceName,graphicsAPI=SystemInfo.graphicsDeviceType.ToString(),
                seed=World.seed,screenWidth=Screen.width,screenHeight=Screen.height,targetFrameRate=Application.targetFrameRate,
                vSyncCount=QualitySettings.vSyncCount,cloudQuality=World.settings.cloudQuality,terrainQuality=World.settings.terrainQuality,
                editor=Application.isEditor,startedInMap=MapMode,startedPaused=Paused,initialLayer=View.layer,initialWorldRevision=World.Revision,initialWorldRuntimeId=World.GetHashCode()};
            performanceSummary="正在記錄，最多 1800 幀";Notice="已開始幀時間記錄；保持同一視角和設定以便比較。導出按鈕可提前停止。";hud?.Refresh();
        }
        void TickPerformanceCapture()
        {
            if(Time.realtimeSinceStartupAsDouble>=nextPerformanceDisplay)
            {recentFrameMilliseconds=Time.unscaledDeltaTime*1000;nextPerformanceDisplay=Time.realtimeSinceStartupAsDouble+.5;}
            // Skip the interval that began before the Record button was pressed.
            if(!performanceRecording||World==null||Time.frameCount<=performanceStartFrame+1)return;
            float ms=Time.unscaledDeltaTime*1000;if(float.IsNaN(ms)||float.IsInfinity(ms)||ms<=0)return;
            var local=View!=null?View.LocalSurface:null;
            performanceFrames[performanceCount++]=new FrameSample{ms=ms,frame=Time.frameCount,altitude=Altitude,
                layer=View!=null?View.layer:0,cloudQuality=World.settings.cloudQuality,terrainQuality=World.settings.terrainQuality,
                map=MapMode,paused=Paused,focused=Application.isFocused,visiblePatches=View!=null?View.VisibleRenderPatches:0,
                localVertices=local!=null?local.VertexCount:0,pendingVertices=local!=null?local.PendingVertices:0,
                localBuilding=local!=null&&local.IsBuilding,editMilliseconds=LastTerrainEditMilliseconds};
            if(World.Revision!=performanceReport.initialWorldRevision||World.GetHashCode()!=performanceReport.initialWorldRuntimeId||View.layer!=performanceReport.initialLayer||World.seed!=performanceReport.seed||MapMode!=performanceReport.startedInMap||Paused!=performanceReport.startedPaused||
                Screen.width!=performanceReport.screenWidth||Screen.height!=performanceReport.screenHeight||
                World.settings.cloudQuality!=performanceReport.cloudQuality||World.settings.terrainQuality!=performanceReport.terrainQuality)
                performanceReport.contextChangedDuringCapture=true;
            if(performanceCount>=PerformanceCapacity)CompletePerformanceCapture();
        }
        void CompletePerformanceCapture()
        {
            performanceRecording=false;if(performanceReport==null||performanceCount==0)return;
            var sorted=new float[performanceCount];double sum=0;
            for(int i=0;i<performanceCount;i++){sorted[i]=performanceFrames[i].ms;sum+=sorted[i];}
            Array.Sort(sorted);performanceReport.samples=performanceCount;
            performanceReport.meanMs=sum/performanceCount;performanceReport.maxMs=sorted[performanceCount-1];
            performanceReport.p50Ms=Percentile(sorted,.5);performanceReport.p95Ms=Percentile(sorted,.95);performanceReport.p99Ms=Percentile(sorted,.99);
            performanceReport.framesPerSecondFromMean=1000/performanceReport.meanMs;
            performanceSummary=string.Format(CultureInfo.InvariantCulture,"{0} 幀 · 均值 {1:F2} ms · P95 {2:F2} ms · P99 {3:F2} ms",
                performanceCount,performanceReport.meanMs,performanceReport.p95Ms,performanceReport.p99Ms);
        }
        static double Percentile(float[] sorted,double p)
        {
            double index=(sorted.Length-1)*p;int a=(int)index,b=Math.Min(a+1,sorted.Length-1);return sorted[a]+(sorted[b]-sorted[a])*(index-a);
        }
        public string PerformanceStatus()
        {
            if(World==null||View==null)return "地形初始化中";
            var local=View.LocalSurface;
            return "最近幀間隔 "+recentFrameMilliseconds.ToString("F1")+" ms（非 CPU/GPU 拆分）\n"+
                "球面共享頂點 "+View.SurfaceVertexCount+" · 渲染塊 "+(MapMode?0:View.VisibleRenderPatches)+" / "+View.RenderPatchCount+"\n"+
                "近地頂點 "+(local!=null?local.VertexCount:0)+" · 待生成 "+(local!=null?local.PendingVertices:0)+"\n"+
                "本次幾何重採樣 "+View.GeometrySampleCount+" · 上次編輯 "+LastTerrainEditMilliseconds.ToString("F1")+" ms\n"+
                (performanceRecording?"記錄中 "+performanceCount+" / "+PerformanceCapacity:performanceSummary);
        }
        public void ExportPerformanceCapture()
        {
            if(performanceCount==0||performanceReport==null){Notice="尚無有效記錄，請先按「記錄幀時間」。";hud?.Refresh();return;}
            CompletePerformanceCapture();
            try
            {
                string folder=Path.Combine(Application.persistentDataPath,"TerrainPerf1");Directory.CreateDirectory(folder);
                string stem="frames-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
                string csv=Path.Combine(folder,stem+".csv"),json=Path.Combine(folder,stem+".json");
                var text=new StringBuilder(performanceCount*100);
                text.AppendLine("frame,interval_ms,map,paused,focused,altitude,layer,cloud_quality,terrain_quality,visible_globe_patches,local_vertices,pending_vertices,local_building,last_terrain_edit_ms");
                for(int i=0;i<performanceCount;i++)
                {
                    var f=performanceFrames[i];text.AppendFormat(CultureInfo.InvariantCulture,
                        "{0},{1:F5},{2},{3},{4},{5:F7},{6},{7},{8},{9},{10},{11},{12},{13:F4}\n",
                        f.frame,f.ms,f.map?1:0,f.paused?1:0,f.focused?1:0,f.altitude,f.layer,f.cloudQuality,f.terrainQuality,
                        f.map?0:f.visiblePatches,f.localVertices,f.pendingVertices,f.localBuilding?1:0,f.editMilliseconds);
                }
                performanceReport.exportedUtc=DateTime.UtcNow.ToString("o");
                File.WriteAllText(csv,text.ToString(),new UTF8Encoding(false));File.WriteAllText(json,JsonUtility.ToJson(performanceReport,true),new UTF8Encoding(false));
                LastPerformanceExportPath=folder;Notice="已導出 CSV 原始幀時間與 JSON 摘要："+folder;
                Debug.Log("[ORBIS TerrainPerf1] Performance export: "+csv+"\n"+json);
            }
            catch(Exception e){Debug.LogException(e);Notice="導出失敗："+e.Message;}
            hud?.Refresh();
        }
    }
}
