using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis
{
    [Serializable] public sealed class PlanetSettings
    {
        public float tilt=23.4f,heat=1,greenhouse=1,rotationPeriod=120,windPower=1,oceanPower=1,monsoonPower=1;
        public bool rotation=true,clouds=true,night=true,autoWar=false;
        public bool atmosphere=true,followSurface=false;
        public bool autoExpeditions=true,showWind=false,showCurrents=false,showBorders=true,showNames=true,cloudShadows=true;
        public int cloudQuality=1;
        public int climateWorkerLimit=0; // 0 = auto; atmosphere reserves CPU headroom for civilization gameplay.
        public int terrainQuality=1; // Independent of cloud quality: 48/64/96/128 local grid.
        public float terrainBrushRadius=.12f,terrainBrushStrength=.12f,terrainFlattenHeight=0;
        public bool dynamicWeather=true,multipleScattering=true,enhancedWater=true,localGrid=true,surfaceLOD=true;
        public float scatteringStrength=.65f,waveStrength=.65f,airDensity=1,haze=1;
        public int transportView=0; // 0 follows wind/current toggles, 1 wind, 2 moisture, 3 ocean current
        public bool godRays=true,mapClouds=true;
        public bool rainEffects=true,lightningEffects=true,weatherAudio=true,wetSurfaces=true;
        public float exposure=1f,shadowStrength=.8f;
        public int surfaceDebug=0; // final / albedo / N.L / sun transmittance / cloud shadow / snow
        public int selectionMode=0; // 0 stable 4x4 gameplay Zone; 1/2 natural geography inspection only
        public bool regionExpansion=true,spriteActors=true;
        public float cloudCover=1,cloudDensity=1,waterRoughness=1,surfaceDetail=1;
        public OrbisNightLightingSettings nightLighting=new OrbisNightLightingSettings();
    }
    [Serializable] public sealed class Tile
    {
        public float h,edit,temp,thermal,moist=.8f,soil=.52f,rain,cloud,snow,flow,flood,lake,pop,boost;
        public int plate,owner=-1,downstream=-1,basin=-1,coast;
        [NonSerialized] public int zoneId=-1,chunkId=-1;
        [NonSerialized] public float authoredRiver;
        public float RiverStrength=>Mathf.Max(flow,authoredRiver*12);
        public bool city;
        public Vector3 wind,current;
        public Vector3 cloudDrift; // legacy save field: never accumulated or sampled in V3
        public float vapor,cloudWater,cloudRain,convection;
        public Vector3 cloudWind;
        [NonSerialized] public float precipitationMmH,snowfallMmH,surfaceWetness,cloudBaseMeters,cloudTopMeters,cape,updraft;
        [NonSerialized] public int stormStage;
        [NonSerialized] public float pressure=1013,vorticity;
        public float Temperature=>temp+thermal;
        public float Fertility=>h<=0||lake>.018f?0:Mathf.Clamp01(soil*(1-Mathf.Abs(Temperature-21)/55)*(1-Mathf.Max(0,h-.55f)));
    }
    [Serializable] public sealed class Civilization
    {
        public string name;public Color color;public int capital;public float food=100,tech=1;
    }
    [Serializable] public sealed class Plate
    {
        public Vector3 center,axis;public float speed,offset;public bool continental;
    }
    [Serializable] public sealed class WorldSave
    {
        public string format="ORBIS_UNITY",seed;public int version=4;public uint rngState;
        public float day,geo,seaLevel,rotationAngle,weatherClock,stability;public bool war;
        public DemoViewState view;
        public string producer="ORBIS_V4_2_TERRAINPERF2_ZONE4X4";
        public OrbisMapSave mapSource;public float territoryClock;
        public bool weatherInitialized=true;
        public double weatherTime,weatherRemainder,visualTime;
        public OrbisAtmosphereState layeredAtmosphere;
        public List<Expedition> expeditions;public float expeditionClock;
        public TerrainChunk[] chunks;public TerrainZone[] zones;
        public Tile[] tiles;public List<Civilization> civs;public PlanetSettings settings;public List<string> events;
    }
    public sealed partial class OrbisWorld
    {
        [NonSerialized] public bool DeferWeatherUpdates; // runtime scheduling only; never persisted

        public const int TileResolution=24;
        public const int TileCount=6*TileResolution*TileResolution;
        public readonly SphereGrid grid=SphereGrid.CreateTiles(TileResolution);
        public string seed;public Tile[] tiles;public Plate[] plates;
        public List<Civilization> civs=new List<Civilization>();public List<string> events=new List<string>();
        public PlanetSettings settings=new PlanetSettings();public float day=100,geo,seaLevel,rotationAngle,weatherClock,stability;public bool war;
        public RandomStream rng;public Noise3D noise;
        public OrbisWeather Weather {get;private set;}
        public double VisualTime {get;private set;}
        public double AtmosphereClock=>Weather!=null?Weather.Clock:weatherClock;
        public readonly OrbisGeography geography=new OrbisGeography();
        int[] drainageOrder;readonly float[] nextMoist,nextCloud,nextTemp,nextThermal;
        float[] cachedRawTerrain;float cachedRawGeo=float.NaN;Noise3D cachedRawNoise;
        readonly bool[] aliveScratch=new bool[12];
        public int Revision {get;private set;}
        public int LandCount {get;private set;} public int AliveCivilizations {get;private set;} public float Population {get;private set;} public float MeanTemperature {get;private set;}
        public OrbisWorld(string worldSeed){int n=grid.points.Length;nextMoist=new float[n];nextCloud=new float[n];nextTemp=new float[n];nextThermal=new float[n];Generate(worldSeed);}
        public Vector3 SunLocal => SunAt(day, rotationAngle);
        public Vector3 SunAt(float calendarDay, float angle)
        {
            float phase=2*Mathf.PI*(calendarDay%360-80)/360;
            float decl=Mathf.Asin(Mathf.Sin(settings.tilt*Mathf.Deg2Rad)*Mathf.Sin(phase));
            return new Vector3(Mathf.Sin(-angle)*Mathf.Cos(decl),Mathf.Sin(decl),Mathf.Cos(-angle)*Mathf.Cos(decl));
        }
        public void Log(string s){events.Insert(0,"第 "+(1+(int)(day/360))+" 年　"+s);if(events.Count>40)events.RemoveAt(events.Count-1);}
        public void Generate(string s)
        {
            Weather=null;VisualTime=0;MapSource=null;TerritoryClock=0;
            seed=string.IsNullOrWhiteSpace(s)?"ORBIS-VI":s.Substring(0,Math.Min(32,s.Length));rng=new RandomStream(RandomStream.Hash(seed));noise=new Noise3D(RandomStream.Hash(seed)^0x5eeda5);
            day=100;geo=0;rotationAngle=0;weatherClock=0;stability=0;war=false;civs.Clear();events.Clear();expeditions.Clear();expeditionClock=0;
            plates=new Plate[9+(int)(rng.Next()*3)];
            for(int k=0;k<plates.Length;k++) {Vector3 p=rng.Unit();for(int tries=0;tries<40;tries++){bool near=false;for(int j=0;j<k;j++)if(Vector3.Dot(p,plates[j].center)>.78f)near=true;if(!near)break;p=rng.Unit();}plates[k]=new Plate{center=p,axis=rng.Unit(),speed=rng.Range(.0035f,.009f),offset=rng.Range(-20,20),continental=rng.Next()>.42};}
            tiles=new Tile[grid.points.Length];for(int i=0;i<tiles.Length;i++)tiles[i]=new Tile{temp=28-52*grid.points[i].y*grid.points[i].y};
            BuildStorms();Terrain(true);for(int i=0;i<80;i++)Climate(1,0);Weather=new OrbisWeather(this);UpdateDischarge();geography.Build(this);RefreshNames();UpdateStats();Revision++;Log("世界形成："+plates.Length+" 個板塊、"+tiles.Length+" 個四邊形地塊。");
        }
        float PlateHeight(int k,Vector3 p){Plate pl=plates[k];Vector3 q=Quaternion.AngleAxis(-geo*pl.speed*Mathf.Rad2Deg,pl.axis)*p;float o=pl.offset;return(pl.continental?.095f:-.1f)+noise.Fbm(q*2.3f+new Vector3(o,o*.7f,-o*.4f))*.78f;}
        public void Terrain(bool initial=false)
        {
            float[] raw=cachedRawTerrain;
            if(initial||raw==null||cachedRawGeo!=geo||cachedRawNoise!=noise)
            {
            raw=new float[tiles.Length];float[] stress=new float[tiles.Length];var centers=new Vector3[plates.Length];for(int k=0;k<plates.Length;k++)centers[k]=Quaternion.AngleAxis(geo*plates[k].speed*Mathf.Rad2Deg,plates[k].axis)*plates[k].center;
            for(int i=0;i<tiles.Length;i++){float a=-2,b=-2;int first=0,second=0;for(int k=0;k<plates.Length;k++){float d=Vector3.Dot(grid.points[i],centers[k]);if(d>a){b=a;second=first;a=d;first=k;}else if(d>b){b=d;second=k;}}tiles[i].plate=first;raw[i]=Mathf.Lerp(PlateHeight(second,grid.points[i]),PlateHeight(first,grid.points[i]),Mathf.Clamp01((a-b)/.055f)*.5f+.5f);}
            for(int i=0;i<tiles.Length;i++)foreach(int j in grid.neighbors[i])if(j>i&&tiles[i].plate!=tiles[j].plate){Vector3 p=(grid.points[i]+grid.points[j]).normalized,d=Vector3.ProjectOnPlane(grid.points[j]-grid.points[i],p).normalized;Plate a=plates[tiles[i].plate],b=plates[tiles[j].plate];float r=Vector3.Dot(Vector3.Cross(b.axis,p)*b.speed-Vector3.Cross(a.axis,p)*a.speed,d);float up=r<0?Mathf.Clamp(-r*15,0,.17f):-Mathf.Clamp(r*6,0,.065f);stress[i]+=up;stress[j]+=up;}
            for(int i=0;i<tiles.Length;i++){float s=stress[i]*.25f;foreach(int j in grid.neighbors[i])s+=stress[j]*.07f;raw[i]+=s*.7f;}
            OrbisTerrainEvolution.Relax(raw,grid.neighbors,3,.055f);
            cachedRawTerrain=raw;cachedRawGeo=geo;cachedRawNoise=noise;
            }
            if(initial){float[] sorted=(float[])raw.Clone();Array.Sort(sorted);seaLevel=sorted[(int)(sorted.Length*.64f)];}
            for(int i=0;i<tiles.Length;i++)tiles[i].h=MapSource==null?Mathf.Clamp((raw[i]-seaLevel)*2.7f+tiles[i].edit,-.95f,1.05f):Mathf.Clamp(MapSource.Sample(grid.points[i])+tiles[i].edit,-.95f,1.05f);
            // Relocate flooded populations instead of silently deleting settlements.
            for(int i=0;i<tiles.Length;i++){Tile c=tiles[i];if(c.h<=0&&c.owner>=0){int safe=FindSafeLand(i,c.owner);if(safe>=0){Tile d=tiles[safe];d.owner=c.owner;d.pop+=c.pop*.8f;d.city|=c.city;if(civs[c.owner].capital==i)civs[c.owner].capital=safe;}c.pop=0;c.owner=-1;c.city=false;}}
            var q=new Queue<int>();for(int i=0;i<tiles.Length;i++){tiles[i].coast=tiles[i].h<=0?0:999;if(tiles[i].h<=0)q.Enqueue(i);}while(q.Count>0){int i=q.Dequeue();foreach(int j in grid.neighbors[i])if(tiles[j].coast>tiles[i].coast+1){tiles[j].coast=tiles[i].coast+1;q.Enqueue(j);}}
            BuildHydrology();geography.Build(this);RefreshNames();UpdateStats();Revision++;
        }
        int FindSafeLand(int start,int owner){var q=new Queue<int>();var seen=new bool[tiles.Length];q.Enqueue(start);seen[start]=true;while(q.Count>0){int i=q.Dequeue();if(tiles[i].h>0&&(tiles[i].owner==owner||tiles[i].owner<0))return i;foreach(int j in grid.neighbors[i])if(!seen[j]){seen[j]=true;q.Enqueue(j);}}return -1;}
        public void BuildHydrology()
        {
            var seen=new bool[tiles.Length];var heap=new MinHeap();var order=new List<int>();
            for(int i=0;i<tiles.Length;i++){Tile c=tiles[i];c.downstream=-1;c.flood=Mathf.Max(0,c.h);c.lake=0;c.basin=-1;if(c.h<=0){seen[i]=true;heap.Push(i,0);}}
            if(heap.Count==0){int low=0;for(int i=1;i<tiles.Length;i++)if(tiles[i].h<tiles[low].h)low=i;seen[low]=true;heap.Push(low,tiles[low].h);}
            while(heap.Count>0){int i=heap.Pop();order.Add(i);foreach(int j in grid.neighbors[i])if(!seen[j]){seen[j]=true;Tile n=tiles[j];n.flood=Mathf.Max(n.h,tiles[i].flood+.00001f);n.downstream=i;n.lake=Mathf.Max(0,n.flood-n.h);heap.Push(j,n.flood);}}
            foreach(int i in order){Tile c=tiles[i];if(c.h<=0||c.downstream<0)continue;float best=-1;foreach(int j in grid.neighbors[i])if(tiles[j].flood<c.flood){float slope=(c.flood-tiles[j].flood)/Mathf.Max(.001f,Vector3.Angle(grid.points[i],grid.points[j])*Mathf.Deg2Rad);if(slope>best){best=slope;c.downstream=j;}}}
            drainageOrder=order.ToArray();foreach(int i in drainageOrder){Tile c=tiles[i];if(c.h>0)c.basin=c.downstream<0||tiles[c.downstream].h<=0?i:tiles[c.downstream].basin;}
            UpdateDischarge();RefreshAuthoredRiverMask();
        }
        public void UpdateDischarge(){foreach(Tile c in tiles)c.flow=c.h>0?Mathf.Max(.008f,c.rain/85*(c.temp<0?.15f:1)*(.4f+.4f*c.soil)):0;for(int k=drainageOrder.Length-1;k>=0;k--){Tile c=tiles[drainageOrder[k]];if(c.downstream>=0)tiles[c.downstream].flow+=c.flow;}}
        public void Step(float realSeconds,float calendarDays)
        {
            if(IsPresentationOnly)throw new InvalidOperationException("A globe snapshot cannot advance the authoritative 2D simulation.");
            if(!Finite(realSeconds)||!Finite(calendarDays)||realSeconds<0||calendarDays<0)throw new ArgumentException("Invalid simulation step.");
            day+=calendarDays;VisualTime+=realSeconds;
            weatherClock=(float)(VisualTime%65536.0); // legacy display only; physics phases use double AtmosphereClock
            if(settings.rotation)rotationAngle=Mathf.Repeat(rotationAngle+realSeconds*2*Mathf.PI/settings.rotationPeriod,Mathf.PI*2);
            Climate(calendarDays,realSeconds);
            double weatherRate=Math.Min(2.5,Math.Sqrt(Math.Max(0,calendarDays/Math.Max(.0001f,realSeconds)/2.0)));
            if(!DeferWeatherUpdates)Weather?.Advance(realSeconds*weatherRate);
            StepCivilizations(calendarDays);StepTerritories(calendarDays);StepExpeditions(calendarDays);UpdateDischarge();UpdateStats();
            if(Population>=5000&&AliveCivilizations>=4&&MeanTemperature>5&&MeanTemperature<28&&!war&&!settings.autoWar)stability+=calendarDays;else stability=Mathf.Max(0,stability-calendarDays*.5f);
        }
        void Climate(float days,float seconds)
        {
            float phase=2*Mathf.PI*(day%360-80)/360,decl=Mathf.Asin(Mathf.Sin(settings.tilt*Mathf.Deg2Rad)*Mathf.Sin(phase));Vector3 sun=SunLocal;
            PrepareAtmosphere(decl);
            for(int i=0;i<tiles.Length;i++){
                Tile c=tiles[i];Vector3 p=grid.points[i];float lat=Mathf.Asin(p.y),band=Mathf.Abs(lat-decl*.42f)*Mathf.Rad2Deg;
                Vector3 east=new Vector3(p.z,0,-p.x).normalized;if(east.sqrMagnitude<.1f)east=Vector3.right;
                Vector3 north=Vector3.Cross(p,east).normalized;float trade=Mathf.SmoothStep(0,1,Mathf.InverseLerp(23,36,band));
                float polar=Mathf.SmoothStep(0,1,Mathf.InverseLerp(53,67,band));
                float zonal=Mathf.Lerp(Mathf.Lerp(-.8f,1,trade),-.6f,polar);
                float hemisphere=(float)Math.Tanh((lat-decl*.42f)*8);
                c.wind=(east*zonal+north*Mathf.Lerp(-.28f,.25f,trade)*hemisphere)*settings.windPower;
                if(c.h>0&&c.coast<5)c.wind+=north*(Mathf.Sin(phase)*Mathf.Exp(-c.coast/4f)*settings.monsoonPower*.4f);
                // Pressure-gradient perturbations use the same local temperatures as the climate model.
                Vector3 pressure=Vector3.zero;
                foreach(int j in grid.neighbors[i])pressure+=Vector3.ProjectOnPlane(grid.points[j]-p,p).normalized*Mathf.Clamp((tiles[j].Temperature-c.Temperature)/35,-.4f,.4f);
                c.wind=Vector3.ClampMagnitude(Vector3.ProjectOnPlane(c.wind+(pressure*.22f+PressureWind(i))*settings.windPower,p)*(1-Mathf.Clamp(c.h*.3f,0,.3f)),3);
                c.cloudDrift=Vector3.zero; // V3: transport the state, not an ever-growing per-cell UV offset.
                float gyre=Mathf.Sin(Mathf.Atan2(p.x,p.z)*2+plates[0].offset)*Mathf.Sin(Mathf.Abs(lat)*2.5f)*Mathf.Sign(lat);
                c.current=c.h<=0?(east*zonal*.45f+north*gyre*.6f+c.wind*.18f-Vector3.Cross(p,c.wind)*.14f*(float)Math.Tanh(lat/.105f))*settings.oceanPower:Vector3.zero;
                if(c.h<=0)foreach(int j in grid.neighbors[i])if(tiles[j].h>0){Vector3 d=Vector3.ProjectOnPlane(grid.points[j],p).normalized;c.current-=d*Mathf.Max(0,Vector3.Dot(c.current,d));}
            }
            for(int i=0;i<tiles.Length;i++){
                Tile c=tiles[i];Vector3 p=grid.points[i];float lat=Mathf.Asin(p.y),cl=Mathf.Sqrt(Mathf.Max(0,1-p.y*p.y));
                float hour=Mathf.Acos(Mathf.Clamp(-Mathf.Tan(lat)*Mathf.Tan(decl),-1,1));
                float ins=Mathf.Max(0,(hour*p.y*Mathf.Sin(decl)+cl*Mathf.Cos(decl)*Mathf.Sin(hour))/Mathf.PI);
                float incoming=0,cloudIn=0,thermalIn=0,heatIn=0,weight=0,lift=0;
                foreach(int j in grid.neighbors[i]){Vector3 d=Vector3.ProjectOnPlane(grid.points[j],p).normalized;float w=Mathf.Max(0,-Vector3.Dot(c.wind,d))+.04f;incoming+=tiles[j].moist*w;cloudIn+=tiles[j].cloud*w;thermalIn+=tiles[j].thermal*w;heatIn+=tiles[j].temp*w;weight+=w;lift+=Mathf.Max(0,c.h-tiles[j].h);}
                incoming/=weight;cloudIn/=weight;thermalIn/=weight;heatIn/=weight;
                Vector3 weatherPoint=p*4.3f; // climatological seed only; OrbisWeather owns the evolving cloud state.
                float transient=noise.Get(weatherPoint.x,weatherPoint.y,weatherPoint.z);
                float moistTarget=c.h<=0?Mathf.Clamp(.77f+c.temp*.006f,.5f,1):Mathf.Clamp01(incoming*(.97f-Mathf.Min(.2f,lift*.05f))+.01f+c.boost*.15f);
                nextMoist[i]=Mathf.Lerp(c.moist,moistTarget,1-Mathf.Exp(-Mathf.Max(days,seconds*.3f)*.35f));
                float belt=lat-decl*.65f;
                float rainBase=(170*Mathf.Exp(-Mathf.Pow(belt/.28f,2))+85*Mathf.Exp(-Mathf.Pow((Mathf.Abs(belt)-.85f)/.3f,2))+18)*nextMoist[i];
                float stormRain=stormLift[i]*140+Mathf.Clamp01((1013-c.pressure)/20)*65;
                if(Weather==null)c.rain=Mathf.Min(1800,Mathf.Max(0,rainBase*(.65f+Mathf.Max(0,transient))*(1+lift*.6f)+stormRain*nextMoist[i]+c.boost*90));
                float convection=Mathf.SmoothStep(0,1,Mathf.InverseLerp(8,32,c.Temperature))*nextMoist[i];
                float condensation=Mathf.Clamp01((nextMoist[i]-.35f)*1.25f+lift*.22f);
                float cloudTarget=Mathf.Clamp(condensation*.48f+c.rain/420+convection*.12f+transient*.28f+stormLift[i]*.18f-Mathf.Clamp01((c.pressure-1016)/14)*.16f,.008f,.96f);
                float cloud=Mathf.Lerp(c.cloud,cloudIn,1-Mathf.Exp(-seconds*.32f*settings.windPower));
                nextCloud[i]=Mathf.Lerp(cloud,cloudTarget,1-Mathf.Exp(-Mathf.Max(seconds,days*.15f)/(cloudTarget>cloud?9:5)));
                bool water=c.h<=0||c.lake>.018f;float snow=Mathf.Clamp01(c.snow/60);
                float ground=water?Mathf.Lerp(.075f,.58f,Mathf.Clamp01(-c.temp/3)):Mathf.Lerp(Mathf.Lerp(.32f,.2f,c.soil),.58f,snow);
                float albedo=Mathf.Clamp(ground+(1-ground)*c.cloud*.38f,.06f,.79f);
                float escape=1-Mathf.Clamp(settings.greenhouse*(.84f+c.moist*.08f+c.cloud*.05f),0,.97f)*.5f;
                float absorbed=1361*settings.heat*(ins*(1-albedo)*.48f+.25f*.7f*.52f);
                float target=Mathf.Clamp(Mathf.Pow(absorbed/(5.670374419e-8f*escape),.25f)-273.15f-Mathf.Max(0,c.h)*33,-95,65);
                nextTemp[i]=Mathf.Lerp(c.temp,target,1-Mathf.Exp(-days/(water?65:23)));
                if(water){float currentHeat=0,currentWeight=0;foreach(int j in grid.neighbors[i])if(tiles[j].h<=0){Vector3 d=Vector3.ProjectOnPlane(grid.points[j],p).normalized;float weightSea=Mathf.Max(0,-Vector3.Dot(c.current,d))+.02f;currentHeat+=tiles[j].temp*weightSea;currentWeight+=weightSea;}if(currentWeight>0)nextTemp[i]=Mathf.Lerp(nextTemp[i],currentHeat/currentWeight,1-Mathf.Exp(-days*.018f*settings.oceanPower));}
                float kelvin=Mathf.Clamp(c.Temperature+273.15f,170,355),capacity=water?6e7f:Mathf.Lerp(9e5f,3.4e6f,c.soil);
                float forcing=1361*settings.heat*(1-albedo)*(Mathf.Max(0,Vector3.Dot(p,sun))-ins);
                float restoring=4*escape*5.670374419e-8f*kelvin*kelvin*kelvin+(water?16:7);
                float heatSeconds=seconds*86400/settings.rotationPeriod;
                float anomaly=Mathf.Lerp(c.thermal,forcing/restoring,1-Mathf.Exp(-heatSeconds*restoring/capacity));
                nextThermal[i]=Mathf.Clamp(Mathf.Lerp(anomaly,thermalIn,1-Mathf.Exp(-heatSeconds/86400*.25f*settings.windPower)),-65,65);
                if(c.h>0){float evap=Mathf.Max(0,c.Temperature)*.07f;float soilTarget=Mathf.Clamp01(.18f+c.rain/210-evap*.03f+c.boost*.2f);c.soil=Mathf.Lerp(c.soil,soilTarget,1-Mathf.Exp(-days/40));if(Weather==null)c.snow=Mathf.Clamp(c.snow+(c.Temperature<0?c.rain/30:-c.Temperature*.4f)*days,0,240);}
                c.boost=Mathf.Max(0,c.boost-days*.015f);
            }
            for(int i=0;i<tiles.Length;i++){tiles[i].moist=nextMoist[i];if(Weather==null)tiles[i].cloud=nextCloud[i];tiles[i].temp=nextTemp[i];tiles[i].thermal=nextThermal[i];}
        }
        public int Spawn(int tile)
        {
            if(tile<0||tile>=tiles.Length||tiles[tile].h<=0||tiles[tile].lake>.018f||tiles[tile].owner>=0||civs.Count>=12)return -1;
            int id=civs.Count;string[] names={"蒼嵐","珠海","青原","赤燼","霜北","曜陽","滄瀾","松澤","碧空","玄影","晴川","星野"};
            civs.Add(new Civilization{name=names[id],capital=tile,color=Color.HSVToRGB(Mathf.Repeat(id*.381966f+.09f,1),.58f,.95f)});
            tiles[tile].owner=id;tiles[tile].city=true;tiles[tile].pop=500;Log(names[id]+"文明建立。");UpdateStats();return id;
        }
        public void SeedFour(){int count=0;for(int k=0;k<4;k++){int best=-1;float score=-1;for(int i=0;i<tiles.Length;i++){Tile c=tiles[i];if(c.h<=0||c.lake>.018f||c.owner>=0)continue;float distance=1;foreach(var civ in civs)distance=Mathf.Min(distance,1-Vector3.Dot(grid.points[i],grid.points[civ.capital]));float s=c.Fertility*distance;if(s>score){score=s;best=i;}}if(best>=0&&Spawn(best)>=0)count++;}Log("已播下 "+count+" 個新文明。");}
        void StepCivilizations(float dt)
        {
            if(dt<=0)return;
            for(int i=0;i<tiles.Length;i++){
                Tile c=tiles[i];if(c.owner<0||c.h<=0)continue;Civilization civ=civs[c.owner];float capacity=80+c.Fertility*3000*(1+civ.tech*.05f);
                c.pop=Mathf.Max(0,c.pop+dt*.006f*c.pop*(1-c.pop/capacity));
                if(c.pop<10){c.city=false;c.pop=0;continue;} // Empty claimed land is still national territory.
                if(c.pop<180||rng.Next()>1-Math.Exp(-dt*.08))continue;
                int[] ns=grid.neighbors[i];int j=ns[(int)(rng.Next()*ns.Length)];Tile d=tiles[j];if(d.h<=0||d.lake>.018f||d.Fertility<.08f)continue;
                float barrier=Mathf.Clamp01(1-Mathf.Max(c.h,d.h)*.7f-(d.basin!=c.basin?.15f:0));if(rng.Next()>barrier)continue;
                if(d.owner==c.owner&&d.pop<15){float settlers=c.pop*.2f;c.pop-=settlers;d.owner=c.owner;d.pop+=settlers;}

            }
            foreach(var c in civs)c.tech=Mathf.Min(10,c.tech+dt*.0004f);
        }
        public void Brush(int tile,int tool)
        {
            if(tile<0||tile>=tiles.Length)return;if(tool==1){Spawn(tile);return;}if(tool==5){tiles[tile].owner=-1;tiles[tile].pop=0;tiles[tile].city=false;return;}
            if(tool<2||tool>4)return;
            Vector3 center=grid.points[tile];for(int i=0;i<tiles.Length;i++){float d=Vector3.Distance(center,grid.points[i]);if(d>.12f)continue;float w=1-d/.12f;if(tool==2){tiles[i].boost=Mathf.Min(3,tiles[i].boost+w);tiles[i].soil=Mathf.Clamp01(tiles[i].soil+w*.2f);}else tiles[i].edit=Mathf.Clamp(tiles[i].edit+w*(tool==3?.12f:-.12f),-3,3);}
            if(tool==3||tool==4)Terrain();
        }
        public void RebuildRegions(){geography.Build(this);RefreshNames();Revision++;Log("已依自然地形重新劃分區域。");}
        public void UpdateStats(){LandCount=0;Population=0;MeanTemperature=0;AliveCivilizations=0;var alive=aliveScratch;Array.Clear(alive,0,alive.Length);foreach(Tile c in tiles){if(c.h>0)LandCount++;Population+=c.pop;MeanTemperature+=c.Temperature;if(c.owner>=0&&c.pop>0)alive[c.owner]=true;}foreach(var e in expeditions){Population+=e.strength;alive[e.civ]=true;}foreach(bool a in alive)if(a)AliveCivilizations++;MeanTemperature/=tiles.Length;}
        public WorldSave Save()=>new WorldSave{version=4,producer="ORBIS_V4_2_TERRAINPERF2_ZONE4X4",mapSource=MapSource?.Save(),territoryClock=TerritoryClock,weatherInitialized=true,
            layeredAtmosphere=Weather?.Atmosphere.Save(),weatherTime=Weather?.Clock??0,weatherRemainder=Weather?.Accumulator??0,visualTime=VisualTime,seed=seed,rngState=rng.state,day=day,geo=geo,seaLevel=seaLevel,rotationAngle=rotationAngle,weatherClock=weatherClock,stability=stability,war=war,expeditions=expeditions,expeditionClock=expeditionClock,chunks=geography.chunks,zones=geography.zones,tiles=tiles,civs=civs,settings=settings,events=events};
        public static OrbisWorld Restore(WorldSave s)
        {
            if(s!=null&&s.version==1&&s.settings!=null){s.settings.atmosphere=true;s.settings.autoExpeditions=true;s.settings.showNames=true;s.settings.showBorders=true;s.settings.cloudShadows=true;s.settings.cloudQuality=1;s.settings.cloudCover=1;s.settings.cloudDensity=1;s.settings.waterRoughness=1;s.settings.surfaceDetail=1;s.expeditions=new List<Expedition>();s.expeditionClock=0;}
            ValidateSave(s);OrbisSaveMigration.ValidateV3(s);var w=new OrbisWorld(s.seed);w.tiles=s.tiles;w.civs=s.civs;w.settings=s.settings;w.day=s.day;w.geo=s.geo;w.seaLevel=s.seaLevel;w.rotationAngle=s.rotationAngle;w.weatherClock=s.weatherClock;w.stability=s.stability;w.war=s.war;w.events=s.events??new List<string>();w.rng.state=s.rngState;w.expeditions=s.expeditions??new List<Expedition>();w.expeditionClock=s.expeditionClock;w.MapSource=OrbisWorldMap.Restore(s.mapSource);w.TerritoryClock=s.territoryClock;w.BuildHydrology();w.geography.Restore(s.chunks,s.zones,w);w.RefreshNames();if(s.version==1){for(int i=0;i<w.civs.Count;i++)w.civs[i].name=ChineseText.Name(w.seed+"civ"+i,"國");w.events.Clear();w.Log("已將舊版世界轉入中文版本。");}w.Weather=null;w.VisualTime=Math.Max(0,s.visualTime);
            OrbisNightLighting.Ensure(w.settings); // Missing old-save field gets defaults, not a black night.
            if(s.producer!="ORBIS_V4_2_TERRAINPERF2_ZONE4X4"){w.settings.selectionMode=0;w.settings.localGrid=true;} // Owners/IDs are unchanged.
            if(!Finite(w.settings.terrainBrushRadius)||w.settings.terrainBrushRadius<.02f)w.settings.terrainBrushRadius=.12f;
            if(!Finite(w.settings.terrainBrushStrength)||w.settings.terrainBrushStrength<.01f)w.settings.terrainBrushStrength=.12f;
            w.settings.terrainBrushRadius=Mathf.Clamp(w.settings.terrainBrushRadius,.02f,.35f);
            w.settings.terrainBrushStrength=Mathf.Clamp(w.settings.terrainBrushStrength,.01f,.25f);
            if(!Finite(w.settings.terrainFlattenHeight))w.settings.terrainFlattenHeight=0;
            w.settings.terrainFlattenHeight=Mathf.Clamp(w.settings.terrainFlattenHeight,-.5f,1);
            w.settings.terrainQuality=Mathf.Clamp(w.settings.terrainQuality,0,3);
            w.BuildStorms();w.PrepareAtmosphere(Mathf.Asin(w.SunLocal.y));
            w.Weather=new OrbisWeather(w,!s.weatherInitialized,s.weatherTime,s.weatherRemainder,s.layeredAtmosphere);
            w.PrepareAtmosphere(Mathf.Asin(w.SunLocal.y));w.UpdateStats();return w;
        }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        public static void ValidateSave(WorldSave s)
        {
            if(s==null||s.format!="ORBIS_UNITY"||(s.version!=1&&s.version!=2&&s.version!=3&&s.version!=4)||string.IsNullOrWhiteSpace(s.seed)||s.seed.Length>32||s.tiles==null||s.tiles.Length!=TileCount||s.civs==null||s.civs.Count>12||s.chunks==null||s.chunks.Length==0||s.chunks.Length>TileCount||s.zones==null||s.zones.Length==0||s.zones.Length>TileCount||s.settings==null)throw new ArgumentException("不支援這個 ORBIS 存檔。HTML 原版使用不同格式，無法直接載入。");
            if(!Finite(s.day)||s.day<0||!Finite(s.geo)||s.geo<0||!Finite(s.seaLevel)||!Finite(s.rotationAngle)||!Finite(s.weatherClock)||!Finite(s.stability))throw new ArgumentException("時間或地形資料無效。");
            if(!Finite(s.territoryClock)||s.territoryClock<0||s.territoryClock>20.01f)throw new ArgumentException("Invalid territory clock.");
            if(s.layeredAtmosphere!=null)OrbisAtmosphereCore.Validate(s.layeredAtmosphere,TileCount);
            PlanetSettings p=s.settings;if(p.selectionMode<0||p.selectionMode>2)throw new ArgumentException("Invalid selection mode.");if(!Finite(p.tilt)||p.tilt<0||p.tilt>45||!Finite(p.heat)||p.heat<.7f||p.heat>1.3f||!Finite(p.greenhouse)||p.greenhouse<0||p.greenhouse>2||!Finite(p.rotationPeriod)||p.rotationPeriod<30||p.rotationPeriod>300||!Finite(p.windPower)||p.windPower<0||p.windPower>2||!Finite(p.oceanPower)||p.oceanPower<0||p.oceanPower>2||!Finite(p.monsoonPower)||p.monsoonPower<0||p.monsoonPower>2)throw new ArgumentException("行星參數無效。");
            if(s.view!=null){var v=s.view;if(!Finite(v.yaw)||!Finite(v.pitch)||!Finite(v.distance)||!Finite(v.mapX)||!Finite(v.mapY)||!Finite(v.mapZoom)||!(v.speed==1||v.speed==5||v.speed==20)||v.selected<-1||v.selected>=TileCount||v.tool<0||v.tool>7||v.layer<0||v.layer>12)throw new ArgumentException("視角存檔資料無效。");}
            if(s.expeditions!=null){if(s.expeditions.Count>48||!Finite(s.expeditionClock)||s.expeditionClock<0)throw new ArgumentException("遠征資料無效。");var topology=SphereGrid.CreateTiles(TileResolution);foreach(var e in s.expeditions){if(e==null||e.civ<0||e.civ>=s.civs.Count||e.tile<0||e.tile>=TileCount||e.goal<0||e.goal>=TileCount||e.path==null||e.path.Length==0||e.path.Length>TileCount||e.index<0||e.index>=e.path.Length||!Finite(e.progress)||e.progress<0||e.progress>100||!Finite(e.strength)||e.strength<0||e.strength>1e7)throw new ArgumentException("軍團路徑資料無效。");for(int i=0;i<e.path.Length;i++){if(e.path[i]<0||e.path[i]>=TileCount)throw new ArgumentException("軍團地塊無效。");if(i>0&&Array.IndexOf(topology.neighbors[e.path[i-1]],e.path[i])<0)throw new ArgumentException("軍團路徑不相連。");}if(e.path[e.index]!=e.tile||e.path[e.path.Length-1]!=e.goal)throw new ArgumentException("軍團位置與路徑不一致。");}}
            if(p.cloudQuality<0||p.cloudQuality>3||!Finite(p.cloudCover)||p.cloudCover<.25f||p.cloudCover>1.7f||!Finite(p.cloudDensity)||p.cloudDensity<.3f||p.cloudDensity>2||!Finite(p.waterRoughness)||p.waterRoughness<.2f||p.waterRoughness>2||!Finite(p.surfaceDetail)||p.surfaceDetail<0||p.surfaceDetail>2)throw new ArgumentException("畫質參數無效。");
            foreach(var civ in s.civs)if(civ==null||civ.capital<0||civ.capital>=s.tiles.Length||civ.name==null||civ.name.Length>40||!Finite(civ.tech)||civ.tech<0||civ.tech>10||!Finite(civ.color.r)||!Finite(civ.color.g)||!Finite(civ.color.b))throw new ArgumentException("文明資料無效。");
            foreach(Tile c in s.tiles)if(c==null||c.owner<-1||c.owner>=s.civs.Count||c.plate<0||c.plate>11||!Finite(c.h)||Mathf.Abs(c.h)>1.1f||!Finite(c.edit)||Mathf.Abs(c.edit)>3||!Finite(c.temp)||Mathf.Abs(c.temp)>150||!Finite(c.thermal)||Mathf.Abs(c.thermal)>65||!Finite(c.pop)||c.pop<0||c.pop>1e8||!Finite(c.soil)||c.soil<0||c.soil>1||!Finite(c.moist)||c.moist<0||c.moist>1||!Finite(c.cloud)||c.cloud<0||c.cloud>1||!Finite(c.rain)||c.rain<0||c.rain>2000||!Finite(c.snow)||c.snow<0||c.snow>240||!Finite(c.boost)||c.boost<0||c.boost>3||!Finite(c.cloudDrift.x)||!Finite(c.cloudDrift.y)||!Finite(c.cloudDrift.z)||c.cloudDrift.sqrMagnitude>1e12f||!Finite(c.wind.x)||!Finite(c.wind.y)||!Finite(c.wind.z)||c.wind.sqrMagnitude>100||!Finite(c.current.x)||!Finite(c.current.y)||!Finite(c.current.z)||c.current.sqrMagnitude>100)throw new ArgumentException("地區資料無效。");
        }
    }
}
