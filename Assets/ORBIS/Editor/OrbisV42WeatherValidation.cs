using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    /// <summary>Executes the real C# core in Unity, including JsonUtility snapshots.
    /// Explicit menu only. Does not change the active scene, world, assets or saves.</summary>
    public static class OrbisV42WeatherValidation
    {
        [Serializable] sealed class Check {public string name,error;public bool passed;public double value;}
        [Serializable] sealed class Report
        {
            public string version="4.2",utc,unityVersion,scope="Real C# equations / Unity JsonUtility; no visual or performance acceptance";
            public bool passed,visualAcceptance=false;public Check[] checks;
        }
        static readonly List<Check> checks=new List<Check>();
        static void CheckCase(string name,Func<double> test)
        {var item=new Check{name=name};try{item.value=test();item.passed=true;}catch(Exception e){item.error=e.ToString();}checks.Add(item);}
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        [MenuItem("Tools/ORBIS/V4.2/运行天气数值与存档检查")]
        public static void Validate(){Run();}
        public static void ValidateBatch(){bool passed=Run();if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);}
        static bool Run()
        {
            checks.Clear();
            CheckCase("Finite-volume area / positive reservoirs",()=>
            {
                var a=MakeCore();double area=0;foreach(double x in a.Area)area+=x;
                Require(Math.Abs(area-4*Math.PI)<1e-4,"Spherical areas do not close.");
                for(int n=0;n<200;n++)a.Tick(.1);
                foreach(var q in a.Water)foreach(double x in q)Require(OrbisAtmosphereCore.Finite(x)&&x>=0,"Invalid water reservoir.");
                double error=Math.Abs(a.WaterBudgetError()/a.InitialWater);Require(error<1e-9,"Water budget drift.");return error;
            });
            CheckCase("Cloud mass moves / height-dependent velocity",()=>
            {
                var a=MakeCore();foreach(var q in a.Water)Array.Clear(q,0,q.Length);
                for(int k=0;k<12;k++)for(int i=0;i<a.Count;i++)
                {var p=a.Position[i];a.Water[1][k*a.Count+i]=p.Z>.94?1:0;a.Wind[k*a.Count+i]=new AtmosVector(p.Z,0,-p.X)*(.018+k*.0012);}
                double before=a.TotalWater();var start=Centroid(a,0);
                for(int n=0;n<500;n++)a.HorizontalTransport(.1);
                double low=Angle(start,Centroid(a,0)),high=Angle(start,Centroid(a,11));
                Require(low>.3&&high>low+.2,"Macroscopic cloud advection failed.");
                Require(Math.Abs(a.TotalWater()-before)/before<1e-9,"Transport mass drift.");return low;
            });
            CheckCase("JsonUtility save / load / deterministic continuation",()=>
            {
                var a=MakeCore();for(int n=0;n<60;n++)a.Tick(.1);
                string json=JsonUtility.ToJson(a.Save());var state=JsonUtility.FromJson<OrbisAtmosphereState>(json);
                var b=MakeCore();b.Restore(state);
                Require(b.Lightning.Count==0,"Loading replayed an old lightning visual event.");
                for(int n=0;n<30;n++){a.Tick(.1);b.Tick(.1);}
                double maxError=0;
                for(int s=0;s<6;s++)for(int j=0;j<a.Water[s].Length;j++)maxError=Math.Max(maxError,Math.Abs(a.Water[s][j]-b.Water[s][j]));
                for(int j=0;j<a.Temperature.Length;j++)maxError=Math.Max(maxError,Math.Abs(a.Temperature[j]-b.Temperature[j]));
                Require(maxError<1e-9&&a.LightningCount==b.LightningCount,"Save continuation is not deterministic.");
                var bad=b.Save();bad.qv[0]=double.NaN;bool rejected=false;try{b.Restore(bad);}catch(ArgumentException){rejected=true;}
                Require(rejected,"Invalid snapshot was accepted.");return maxError;
            });
            CheckCase("No condensate / no spontaneous lightning",()=>
            {
                var a=MakeCore();foreach(var q in a.Water)Array.Clear(q,0,q.Length);
                a.SurfaceExchangeEnabled=false;Array.Clear(a.Charge,0,a.Charge.Length);a.Lightning.Clear();
                for(int n=0;n<80;n++)a.Tick(.1);
                Require(a.LightningCount==0&&a.Lightning.Count==0,"Dry atmosphere produced lightning.");
                foreach(double c in a.Coverage)Require(c==0,"Noise created physical cloud water.");return 0;
            });
            CheckCase("Legacy JSON enables V4.2 presentation / fixed dt validation",()=>
            {
                var legacy=OrbisSaveMigration.Read("{\"version\":4,\"settings\":{}}");
                Require(legacy.settings.rainEffects&&legacy.settings.lightningEffects&&legacy.settings.weatherAudio&&legacy.settings.wetSurfaces,"Legacy defaults are disabled.");
                var a=MakeCore();bool rejected=false;try{a.Tick(.2);}catch(ArgumentOutOfRangeException){rejected=true;}
                Require(rejected,"Oversized timestep accepted.");return 0;
            });
            var report=new Report{utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,passed=true,checks=checks.ToArray()};
            foreach(var item in checks)report.passed&=item.passed;
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Documentation/V4_2"));Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"Unity-weather-report.json");File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(report.passed)Debug.Log("ORBIS V4.2 C# weather checks passed. This is not visual acceptance. "+path);
            else Debug.LogError("ORBIS V4.2 weather check failed. "+path);
            return report.passed;
        }
        static double Angle(AtmosVector a,AtmosVector b)=>Math.Acos(OrbisAtmosphereCore.Clamp(AtmosVector.Dot(a,b),-1,1));
        static AtmosVector Centroid(OrbisAtmosphereCore a,int layer)
        {var sum=new AtmosVector();for(int i=0;i<a.Count;i++)sum+=a.Position[i]*(a.Water[1][layer*a.Count+i]*a.Area[i]);return sum.Normalized;}
        static OrbisAtmosphereCore MakeCore()
        {
            var grid=SphereGrid.CreateTiles(8);int n=grid.points.Length;var p=new AtmosVector[n];var areas=new double[n];
            for(int i=0;i<n;i++)p[i]=OrbisWeather.ToAtmos(grid.points[i]);
            var edges=new List<AtmosEdge>();var owners=new Dictionary<long,int>();
            for(int i=0;i<n;i++)for(int j=0;j<4;j++)
            {
                int a=grid.tileCorners[i][j],b=grid.tileCorners[i][(j+1)%4];var u=OrbisWeather.ToAtmos(grid.corners[a]);var v=OrbisWeather.ToAtmos(grid.corners[b]);
                areas[i]+=2*Math.Atan2(Math.Abs(AtmosVector.Dot(p[i],AtmosVector.Cross(u,v))),1+AtmosVector.Dot(p[i],u)+AtmosVector.Dot(u,v)+AtmosVector.Dot(v,p[i]));
                long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                if(owners.TryGetValue(key,out int other))edges.Add(new AtmosEdge{A=other,B=i,Direction=(p[i]-p[other]).Normalized,Length=Angle(u,v),Distance=Angle(p[i],p[other])});else owners[key]=i;
            }
            var core=new OrbisAtmosphereCore(p,areas,edges.ToArray(),42042);
            for(int i=0;i<n;i++)
            {
                var point=p[i];var east=new AtmosVector(point.Z,0,-point.X).Normalized;
                core.SetForcing(i,302-38*Math.Abs(point.Y),Math.Max(0,point.Z),.65,point.X<.35,east*.6,1013,0,0,0);
            }
            core.Initialize();return core;
        }
    }
}
