using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    /// <summary>These are executable Editor checks, NOT evidence that this package was run in Unity.</summary>
    public static class OrbisV3Validation
    {
        static void Require(bool value,string message){if(!value)throw new InvalidDataException("ORBIS V3: "+message);}
        static string RemoveNumber(string json,string key)=>Regex.Replace(json,"\\\""+key+"\\\":-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?,?","");
        [MenuItem("Tools/ORBIS/V3/驗證核心與存檔")]
        public static void Core()
        {
            int checks=0;var w=new OrbisWorld("V3-MERGE-CHECK");
            Require(w.tiles.Length==3456,"logical tile count changed");checks++;
            var same=new OrbisWorld(w.seed);
            for(int i=0;i<w.tiles.Length;i++)Require(w.tiles[i].h==same.tiles[i].h&&w.tiles[i].cloudWater==same.tiles[i].cloudWater,"seed determinism");checks++;
            for(int i=0;i<40;i++)w.Step(.2f,.4f);
            foreach(var t in w.tiles)
            {
                Require(t.vapor>=0&&t.vapor<=3&&t.cloudWater>=0&&t.cloudWater<=2,"weather bounds");
                Require(t.cloudDrift.sqrMagnitude==0,"legacy drift still accumulates");
            }
            checks++;
            var sampler=w.Weather.sampler;var rng=new RandomStream(7441);float[] constant=new float[w.tiles.Length];
            for(int i=0;i<constant.Length;i++)constant[i]=.789f;
            for(int i=0;i<1000;i++)
            {
                Vector3 p=rng.Unit();var s=sampler.Locate(p);
                Require(Mathf.Abs(s.weights.x+s.weights.y+s.weights.z-1)<1e-5f,"sample weights");
                Require(Mathf.Abs(s.Read(constant)-.789f)<1e-5f,"constant preservation");
            }
            checks++;
            float[] cloud=new float[w.tiles.Length];for(int i=0;i<cloud.Length;i++)cloud[i]=w.tiles[i].cloudWater;
            w.settings.dynamicWeather=false;double oldClock=w.Weather.Clock;w.Step(.2f,.4f);
            Require(w.Weather.Clock==oldClock,"frozen weather clock");
            for(int i=0;i<cloud.Length;i++)Require(w.tiles[i].cloudWater==cloud[i],"frozen cloud reservoir");checks++;
            w.settings.dynamicWeather=true;
            WorldSave state=w.Save();state.view=new DemoViewState{altitude=.025f,distance=1.03f,speed=1,selected=-1,renderClock=w.VisualTime};
            string json=JsonUtility.ToJson(state);
            var restored=OrbisWorld.Restore(OrbisSaveMigration.Read(json));
            Require(restored.Weather.Clock==w.Weather.Clock,"weather clock round trip");
            w.Step(.2f,.4f);restored.Step(.2f,.4f);
            for(int i=0;i<cloud.Length;i++)Require(Mathf.Abs(w.tiles[i].cloudWater-restored.tiles[i].cloudWater)<2e-5f&&Mathf.Abs(w.tiles[i].vapor-restored.tiles[i].vapor)<2e-5f,"weather continuation");checks++;
            // Synthetic OLD ROOT schemas exercise the ambiguous version-2 discriminator.
            string legacy=json.Replace("\"version\":3","\"version\":2");
            WorldSave v2=OrbisSaveMigration.Read(legacy);
            Require(v2.weatherInitialized&&v2.producer=="LEGACY_V2_CLOUDWATER","V2 cloud-water detection");checks++;
            string incoming=RemoveNumber(legacy,"weatherTime");incoming=RemoveNumber(incoming,"weatherRemainder");
            incoming=incoming.Replace(",}","}");
            WorldSave alternate=OrbisSaveMigration.Read(incoming);
            Require(!alternate.weatherInitialized&&alternate.producer=="LEGACY_INCOMING","incoming V2 detection");
            OrbisWorld.Restore(alternate);checks++;
            Debug.Log("ORBIS V3: "+checks+" native core groups passed. This does not test GPU image quality, input or performance.");
        }
        [MenuItem("Tools/ORBIS/V3/驗證著色器與資源")]
        public static void GraphicsAssets()=>OrbisGraphicsValidation.Validate();
        [MenuItem("Tools/ORBIS/V3/切換全解析度大氣對照")]
        public static void Reference()
        {
            var renderer=UnityEngine.Object.FindFirstObjectByType<OrbisAtmosphereRenderer>();
            if(!renderer){Debug.LogWarning("Enter Play Mode first.");return;}
            renderer.fullResolutionReference=!renderer.fullResolutionReference;
            Debug.Log("ORBIS V3 full-resolution reference: "+renderer.fullResolutionReference);
        }
        [MenuItem("Tools/ORBIS/V3/檢查目前近景")]
        public static void InspectCurrent()
        {
            var demo=UnityEngine.Object.FindFirstObjectByType<OrbisDemo>();
            if(!Application.isPlaying||demo==null){Debug.LogWarning("Enter Play Mode first.");return;}
            var p=demo.View.transform.InverseTransformPoint(demo.PlanetCamera.transform.position);
            Require(p.magnitude>demo.View.RadiusAt(p),"camera below terrain");
            if(!demo.MapMode)
            {
                Vector3 d=demo.View.transform.InverseTransformDirection(demo.PlanetCamera.transform.forward);
                Require(demo.View.RaycastSurface(new Ray(p,d),out var hit),"center ray does not reach terrain");
                Require(Mathf.Abs(hit.magnitude-1)<1e-5f,"pick normal");
            }
            Debug.Log("ORBIS V3 current camera clearance and picking checks passed; local render vertices="+demo.View.LocalSurface.VertexCount);
        }
    }
}
