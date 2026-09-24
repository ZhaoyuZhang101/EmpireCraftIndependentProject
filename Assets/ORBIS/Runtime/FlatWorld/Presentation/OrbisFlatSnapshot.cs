using System;
using UnityEngine;
using Orbis.Flat;
namespace Orbis
{
    public sealed partial class OrbisWorld
    {
        // READ-ONLY presentation input. The original 3456 cells are render samples, not simulation tiles.
        public WorldData FlatSource { get; private set; }
        public bool IsPresentationOnly { get { return FlatSource!=null; } }
        private OrbisWorld(WorldData source)
        {
            FlatSource=source;int n=grid.points.Length;
            nextMoist=new float[n];nextCloud=new float[n];nextTemp=new float[n];nextThermal=new float[n];
            tiles=new Tile[n];for(int i=0;i<n;i++)tiles[i]=new Tile();
            plates=new[]{new Plate{center=Vector3.up,axis=Vector3.up,continental=true}};
            seed=source.Seed;rng=new RandomStream(RandomStream.Hash(seed));noise=new Noise3D(RandomStream.Hash(seed));
            settings.dynamicWeather=false;settings.surfaceLOD=false;settings.spriteActors=false;settings.autoExpeditions=false;
            settings.regionExpansion=false;settings.rainEffects=false;settings.lightningEffects=false;settings.weatherAudio=false;
            settings.cloudQuality=1;settings.showNames=false;settings.localGrid=false;
            var gray=new ushort[source.Count];
            for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
            {
                int i=y*source.Width+x;float h=source.Water[i]==WaterKind.Lake?source.WaterLevel[i]:source.Elevation[i];
                gray[(source.Height-1-y)*source.Width+x]=(ushort)Mathf.RoundToInt(Mathf.Clamp01(.5f+h/14000f)*65535);
            }
            MapSource=new OrbisWorldMap(OrbisRasterCodec.Gray16(source.Width,source.Height,gray),null,.5f,2);
            CopyFlatSamples();geography.Build(this);
            Weather=new OrbisWeather(this); // allocate render-compatible layers ONCE; never advance them.
            CopyFlatSamples();BuildClimatologicalCloudSnapshot();Revision++;
        }
        public static OrbisWorld CreateFlatPresentation(WorldData source)
        {if(source==null)throw new ArgumentNullException(nameof(source));return new OrbisWorld(source);}
        public int FlatTile(Vector3 direction)
        {
            direction.Normalize();int x=Mathf.FloorToInt((Mathf.Atan2(direction.x,direction.z)/(Mathf.PI*2)+.5f)*FlatSource.Width);
            int y=Mathf.Clamp(Mathf.FloorToInt((Mathf.Asin(Mathf.Clamp(direction.y,-1,1))/Mathf.PI+.5f)*FlatSource.Height),0,FlatSource.Height-1);
            return FlatSource.Index(x,y);
        }
        public void RefreshFlatPresentation()
        {
            if(!IsPresentationOnly)throw new InvalidOperationException("Not a read-only globe snapshot.");
            CopyFlatSamples();BuildClimatologicalCloudSnapshot();Revision++;
        }
        void CopyFlatSamples()
        {
            var s=FlatSource;day=(float)s.Day;civs.Clear();
            foreach(var nation in s.Nations)civs.Add(new Civilization{name=nation.Name,color=FlatPalette.RGB(nation.Color),capital=0,food=(float)nation.Food});
            for(int j=0;j<tiles.Length;j++)
            {
                int i=FlatTile(grid.points[j]);Tile t=tiles[j];
                t.h=s.Elevation[i]/7000f;t.edit=0;t.temp=s.Temperature[i*12+s.Month];t.thermal=0;t.rain=s.Precipitation[i*12+s.Month];t.soil=s.Fertility[i];
                t.moist=Mathf.Clamp01(t.rain/180+.2f);t.owner=s.Owner[i];t.pop=0;t.city=false;t.flow=0;t.authoredRiver=0;t.downstream=-1;
                t.lake=s.Water[i]==WaterKind.Lake?(s.WaterLevel[i]-s.Elevation[i])/7000f:0;t.flood=s.WaterLevel[i]/7000f;
                t.snow=s.Biomes[i]==Biome.Snow?1:0;t.coast=s.CoastDistance[i];
                Vector3 p=grid.points[j],east=new Vector3(p.z,0,-p.x).normalized;float lat=Mathf.Abs(Mathf.Asin(p.y)*Mathf.Rad2Deg);
                t.wind=east*(lat>=30&&lat<65?1:-1);t.cloudWind=t.wind;t.surfaceWetness=Mathf.Clamp01(t.rain/300);
                t.precipitationMmH=0;t.stormStage=0; // monthly rain is NOT an instantaneous storm.
            }
        }
        void BuildClimatologicalCloudSnapshot()
        {
            var a=Weather.Atmosphere;
            for(int j=0;j<tiles.Length;j++)
            {
                int i=FlatTile(grid.points[j]);Tile t=tiles[j];
                float variation=.25f+.75f*Mathf.Clamp01(.5f+noise.Fbm(grid.points[j]*6,3));
                float cover=Mathf.Clamp01(t.rain/180)*variation;
                t.cloud=cover;t.cloudWater=cover*.55f;t.convection=0;
                for(int k=0;k<OrbisAtmosphereCore.Layers;k++)
                {
                    int at=k*a.Count+j;
                    double profile=Math.Exp(-Math.Pow((OrbisAtmosphereCore.Height[k]-2300)/1500,2))*.23;
                    double high=Math.Exp(-Math.Pow((OrbisAtmosphereCore.Height[k]-8500)/2300,2))*.035;
                    double thickness=OrbisAtmosphereCore.Edges[k+1]-OrbisAtmosphereCore.Edges[k];
                    a.Water[1][at]=profile*cover*thickness/1000;a.Water[2][at]=high*cover*thickness/1000;
                    a.VerticalVelocity[at]=0;a.Wind[at]=OrbisWeather.ToAtmos(t.wind)*.014;
                }
            }
        }
    }
}
