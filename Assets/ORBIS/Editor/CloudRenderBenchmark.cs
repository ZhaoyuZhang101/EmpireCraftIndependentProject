using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
namespace Orbis.Editor
{
 // Fixed-resolution, GPU-synchronised render comparison. Readback time is included:
 // useful for A/B cost, not an assertion about standalone player FPS.
 public static class CloudRenderBenchmark
 {
  public static string Run(string label)
  {
   var volume=UnityEngine.Object.FindFirstObjectByType<OrbisAtmosphereRenderer>();
   if(!volume)throw new InvalidOperationException("Enter Play mode first");
   var camera=volume.GetComponent<Camera>();var nav=volume.scroll;
   var app=UnityEngine.Object.FindFirstObjectByType<Flat.FlatWorldApp>();
   var target=camera.targetTexture;var rect=camera.pixelRect;var active=RenderTexture.active;
   bool enabled=volume.enabled;
   bool integration=volume.referenceCloudIntegration;
   var rt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);rt.Create();
   var read=new Texture2D(1,1,TextureFormat.RGB24,false);
   try
   {
    camera.targetTexture=rt;nav.SetViewport(new Rect(0,0,1280,720));nav.Tick(0,app.CalendarDay);
    Func<double> measure=()=>
    {
     var times=new double[9];
     for(int i=0;i<11;i++)
     {
      var timer=Stopwatch.StartNew();camera.Render();RenderTexture.active=rt;
      read.ReadPixels(new Rect(0,0,1,1),0,0);read.Apply();timer.Stop();
      if(i>=2)times[i-2]=timer.Elapsed.TotalMilliseconds;
     }
     Array.Sort(times);return times[4];
    };
    volume.referenceCloudIntegration=true;double reference=measure();
    volume.referenceCloudIntegration=false;double withCloud=measure();volume.enabled=false;double terrain=measure();
    string report=label+" | "+nav.ModeName+" | "+SystemInfo.graphicsDeviceName+
     " | 1280x720 synchronous camera render median: volume="+withCloud.ToString("F2")+
     " ms, original-integration="+reference.ToString("F2")+" ms, no-volume="+terrain.ToString("F2")+" ms\n";
    Directory.CreateDirectory("Documentation/ClimateCloudUpgrade");
    File.AppendAllText("Documentation/ClimateCloudUpgrade/render-cost.txt",report);return report;
   }
   finally
   {
    volume.enabled=enabled;volume.referenceCloudIntegration=integration;camera.targetTexture=target;nav.SetViewport(rect);nav.Tick(0,app.CalendarDay);
    RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(read);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
   }
  }
 }
}
