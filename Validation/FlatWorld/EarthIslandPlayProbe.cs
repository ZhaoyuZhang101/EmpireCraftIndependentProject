using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Orbis.Flat;
using Orbis.Planet;
using UnityEngine;
using UnityEngine.UIElements;

// Run in Play Mode in the 512 Earth preset via run_script --entry EarthIslandPlayProbe.Run.
public static class EarthIslandPlayProbe
{
    const BindingFlags Member = BindingFlags.Instance | BindingFlags.NonPublic;
    static PlanetLocation Location(double lon, double lat) => new PlanetLocation(lon*Math.PI/180, lat*Math.PI/180);
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name,Member).GetValue(owner);
    static void Invoke(object owner,string method,params object[] args) => owner.GetType().GetMethod(method,Member).Invoke(owner,args);
    static void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }

    public static async Task<string> Run()
    {
        var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
        var timeout = Stopwatch.StartNew();
        while(app!=null && (app.Busy || app.World?.Preset!=TerrainPreset.Earth || app.World.GeographicSurface==null) &&
              timeout.Elapsed.TotalMinutes<8) await Task.Delay(75);
        Check(app!=null && !app.Busy && app.World?.Preset==TerrainPreset.Earth && app.World.Width==512 &&
              app.World.GeographicSurface!=null,"Generate a 512 Earth through the game before running the island check.");
        var nav=Field<FlatScrollNavigator>(app,"navigation");
        var camera=Field<Camera>(app,"viewCamera");
        var root=Field<VisualElement>(app,"root");
        var info=Field<Label>(app,"info");
        var tools=Field<DropdownField>(app,"toolField");
        var report=new StringBuilder();
        double day=app.CalendarDay;
        tools.index=0;
        var island=Location(24.71146,37.61164);
        var west=Location(24.63468,37.61164);
        var east=Location(24.76622,37.61164);
        int tile=app.World.Index(291,181);
        void Click(Vector2 screen)
        {
            Vector2 ui=root.LocalToWorld(new Vector2(screen.x*root.resolvedStyle.width/Screen.width,
                (1-screen.y/Screen.height)*root.resolvedStyle.height));
            Invoke(app,"Pick",ui);
        }
        double Km(PlanetLocation a, PlanetLocation b) => PlanetLocalProjection.Distance(
            PlanetCoordinateSystem.SurfaceNormal(a),PlanetCoordinateSystem.SurfaceNormal(b),6371);
        for(int mode=0;mode<3;mode++)
        {
            foreach(var site in new[]{west,island,east})
            {
                nav.GoGlobe(day);nav.Tick(3,day);
                if(mode==1){nav.Focus(tile,day);nav.Tick(3,day);}
                if(mode==2)nav.OpenAtlas(day);
                nav.Recenter(site,day);nav.Tick(0,day);
                string view=nav.ModeName;
                bool land=site.Longitude==island.Longitude;
                Check((mode==0&&nav.IsGlobe)||(mode==1&&nav.IsFlat&&!nav.IsAtlas)||(mode==2&&nav.IsAtlas),
                    "Wrong starting view "+view);
                Click(camera.pixelRect.center);
                Check(Field<int>(app,"selected")==tile,"A click moved out of the common simulation tile");
                Check(info.text.Contains(land?"地表判定　陆地":"地表判定　海面"),"Wrong surface on "+view+" "+info.text);
                Check(!land || !info.text.Contains("水深："),"The underlying sea depth appeared on the island");
                Invoke(app,"Focus");
                for(int frame=0;frame<180;frame++)nav.Tick(1f/60,day);
                Check(nav.TryGetPlanetLocation(camera.pixelRect.center,out var focused),"Focus missed the world");
                double drift=Km(site,focused);
                Check(drift<.15,"Focus moved "+drift+" km in "+view);
                report.AppendLine(view+" "+(land?"island":"sea")+" | tile "+tile+" | focus error "+drift.ToString("F3")+" km");
            }
        }
        tools.index=tools.choices.FindIndex(s=>s.StartsWith("海上航线",StringComparison.Ordinal));
        Check(tools.index>=0,"Sea route tool missing");
        async Task SeaClick(PlanetLocation point)
        {
            nav.Recenter(point,day);nav.Tick(0,day);Click(camera.pixelRect.center);
            var request=Field<Task>(app,"seaRequest");
            Check(request!=null,"Click did not start a sea route request");
            if(await Task.WhenAny(request,Task.Delay(60000))!=request)throw new TimeoutException("Sea route timeout");
            await request;Invoke(app,"PublishSeaRoute");
        }
        await SeaClick(island);
        Check(!Field<FlatSeaPoint?>(app,"seaOrigin").HasValue,"Sea route accepted a dry origin");
        await SeaClick(west);await SeaClick(east);
        var path=Field<PlanetLocation[]>(nav,"route");
        Check(path?.Length>=3,"The sailing preview did not detour around the island");
        Check(Km(path[0],west)<.15 && Km(path[path.Length-1],east)<.15,"Sailing endpoints moved");
        int samples=0;
        for(int i=1;i<path.Length;i++)
        {
            var a=path[i-1];var b=path[i];
            int count=Math.Max(1,(int)Math.Ceiling(Km(a,b)/.05));
            for(int k=0;k<=count;k++){
                double t=(double)k/count;
                var p=new FlatSeaPoint((a.Longitude+(b.Longitude-a.Longitude)*t)*180/Math.PI,
                    (a.Latitude+(b.Latitude-a.Latitude)*t)*180/Math.PI);
                Check(FlatTerrainAnalysis.Ocean(FlatGeographicSurface.WaterAt(app.World,p)),"Route preview crosses dry land");
                samples++;
            }
        }
        report.Append("Gyaros sailing preview | "+path.Length+" waypoints | "+samples+" wet samples (50 m spacing)");
        tools.index=0;
        nav.Focus(island,day);nav.Tick(3,day);Click(camera.pixelRect.center);
        return report.ToString();
    }
}