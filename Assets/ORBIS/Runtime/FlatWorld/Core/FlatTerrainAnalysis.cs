using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    public enum Landform : byte { Water, Plain, Hills, Plateau, Mountains, Valley, Floodplain, Depression }
    public enum GroundMaterial : byte { Soil, Sand, Silt, Gravel, Rock, Snow }

    // These are game-scale, reproducible diagnostics, not surveyed soils or vegetation.
    // Monthly climate, water heads, drainage and elevation are the only inputs to classes.
    public sealed class FlatTerrainFacts
    {
        public readonly Landform[] Forms;
        public readonly GroundMaterial[] Materials;
        public readonly float[] Relief,Gradient,Hand,Wetness,AnnualRain,MeanTemperature;
        public readonly float[] Sediment,Freshness,FlowX,FlowY,HalfWidth,Depth,ShoreDistance,Meander;
        public readonly byte[] SaturatedMonths,Connections;
        public readonly VegetationCover[] Vegetation;public readonly float[] Canopy;
        public readonly TreeSpecies[] Trees;
        public byte[] VegetationSeasons {get;internal set;}
        public bool VegetationSeasonsReady {get;internal set;}
        public readonly ushort[] FreshDistance;
        public FlatRiverCurves RiverCurves { get; internal set; }
        public FlatRiverDischarge RiverDischarge { get; internal set; }
        public int MouthCount;
        public FlatTerrainFacts(int count)
        {
            Forms=new Landform[count];Materials=new GroundMaterial[count];
            Vegetation=new VegetationCover[count];Canopy=new float[count];
            Trees=new TreeSpecies[count];
            Relief=new float[count];Gradient=new float[count];Hand=new float[count];Wetness=new float[count];
            AnnualRain=new float[count];MeanTemperature=new float[count];
            Sediment=new float[count];Freshness=new float[count];FlowX=new float[count];FlowY=new float[count];
            HalfWidth=new float[count];Depth=new float[count];ShoreDistance=new float[count];Meander=new float[count];
            SaturatedMonths=new byte[count];Connections=new byte[count];FreshDistance=new ushort[count];
        }
        public FlatTerrainFacts Clone()
        {
            var f=new FlatTerrainFacts(Forms.Length){MouthCount=MouthCount,RiverCurves=RiverCurves,RiverDischarge=RiverDischarge,
                VegetationSeasons=VegetationSeasons==null?null:(byte[])VegetationSeasons.Clone(),VegetationSeasonsReady=VegetationSeasonsReady};
            Array.Copy(Vegetation,f.Vegetation,Forms.Length);Array.Copy(Canopy,f.Canopy,Forms.Length);
            Array.Copy(Trees,f.Trees,Forms.Length);
            Array.Copy(Forms,f.Forms,Forms.Length);Array.Copy(Materials,f.Materials,Forms.Length);
            float[][] a={Relief,Gradient,Hand,Wetness,AnnualRain,MeanTemperature,Sediment,Freshness,FlowX,FlowY,HalfWidth,Depth,ShoreDistance,Meander};
            float[][] b={f.Relief,f.Gradient,f.Hand,f.Wetness,f.AnnualRain,f.MeanTemperature,f.Sediment,f.Freshness,f.FlowX,f.FlowY,f.HalfWidth,f.Depth,f.ShoreDistance,f.Meander};
            for(int i=0;i<a.Length;i++)Array.Copy(a[i],b[i],Forms.Length);
            Array.Copy(SaturatedMonths,f.SaturatedMonths,Forms.Length);Array.Copy(Connections,f.Connections,Forms.Length);
            Array.Copy(FreshDistance,f.FreshDistance,Forms.Length);return f;
        }
    }
    public static class FlatTerrainAnalysis
    {
        public static bool Ocean(WaterKind k) { return k>=WaterKind.DeepOcean&&k<=WaterKind.ShallowOcean; }
        public static bool AreaWater(WaterKind k) { return Ocean(k)||k==WaterKind.Lake; }
        public static bool DryClimate(Koppen k) { return k>=Koppen.BWh&&k<=Koppen.BSk; }
        public static float RiverThreshold(WorldData w) { return Math.Max(18,w.Count/2600f); }
        // Game-scale hydraulic geometry: monotone sqrt(discharge), softly bounded.
        // Raw conserved runoff is independent of the threshold used to select streams.
        public static float RiverHalfWidth(float discharge)
        {
            float root=(float)Math.Sqrt(Math.Max(0,discharge));
            return .018f+.202f*(root/(root+8));
        }
        public static float RiverDepth(float discharge,float confinement)
        {
            float depth=.25f+.55f*(float)Math.Pow(Math.Max(0,discharge),.30);
            return FMath.Clamp(depth*(.8f+.2f/(1+Math.Max(0,confinement)/180)),.25f,12);
        }
        public static float RiverMeander(float halfWidth,float confinement,float phase)
        {
            float mobility=(.025f+.06f*(float)Math.Sqrt(FMath.Clamp(halfWidth/.22f,0,1)))/(1+Math.Max(0,confinement)/100);
            return mobility*(.7f*(float)Math.Sin(phase)+.3f*(float)Math.Sin(phase*.37f+1.7f));
        }
        public static int WrappedDX(int a,int b,int width)
        { int dx=a-b;if(dx>width/2)dx-=width;if(dx< -width/2)dx+=width;return dx; }
        internal static int DiagonalDirection(int dx,int dy)
        { return 4+(dx>0?1:0)+(dy>0?2:0); }

        static ushort[] Distance(WorldData w,Func<int,bool> source,CancellationToken cancel)
        {
            var dist=new ushort[w.Count];var queue=new int[w.Count];int head=0,tail=0;
            for(int i=0;i<w.Count;i++){dist[i]=ushort.MaxValue;if(source(i)){dist[i]=0;queue[tail++]=i;}}
            while(head<tail)
            {
                if((head&4095)==0)cancel.ThrowIfCancellationRequested();int i=queue[head++];
                for(int d=0;d<4;d++){int n=w.Neighbor(i,d);if(n>=0&&dist[n]==ushort.MaxValue){dist[n]=(ushort)Math.Min(65534,dist[i]+1);queue[tail++]=n;}}
            }
            return dist;
        }
        public static Landform ClassifyForm(float elevation,float relief,float gradient,float hand,bool fresh,bool water,bool depression)
        {
            if(water)return Landform.Water;
            if(depression)return Landform.Depression;
            if(fresh&&hand<=100&&gradient<=100&&elevation<=1000)return Landform.Floodplain;
            if(elevation>=1000&&relief<=400&&gradient<=200)return Landform.Plateau;
            if(relief>=800||gradient>=500)return Landform.Mountains;
            if(hand<=100&&fresh&&relief>=300)return Landform.Valley;
            if(relief>=300||gradient>=100)return Landform.Hills;
            return Landform.Plain;
        }
        public static Biome ClassifyBiome(Koppen k,float elevation,float temperature,float annualRain,
                                          byte wetMonths,float gradient,float hand,bool nearFresh,bool water,float warmest=float.NaN)
        {
            if(water)return Biome.Water;
            if(k==Koppen.EF)return Biome.Snow;
            if(k==Koppen.ET)return Biome.Tundra;
            if(elevation>=2680&&(!FMath.Finite(warmest)||warmest<=10))return temperature<0?Biome.Snow:Biome.Alpine;
            // Local perennial water can sustain vegetation inside a regional dry climate.
            if(nearFresh&&wetMonths>=8&&gradient<=100&&hand<=100&&temperature>0)return Biome.Wetland;
            if(nearFresh&&wetMonths>=4&&hand<=100&&temperature>0&&DryClimate(k))return gradient<300?Biome.Woodland:Biome.Grassland;
            if(k==Koppen.BWh||k==Koppen.BWk)return Biome.Desert;
            if(k==Koppen.BSh||k==Koppen.BSk)return Biome.Steppe;
            if(k==Koppen.Af||k==Koppen.Am)return Biome.Rainforest;
            if(k==Koppen.Aw)return Biome.Grassland;
            if(k>=Koppen.Dsa&&k<=Koppen.Dfd&&temperature<8)return Biome.Taiga;
            return annualRain>=650?Biome.Woodland:Biome.Grassland;
        }
        public static void Build(WorldData w,CancellationToken cancel,bool updateBiomes,int workers=0,FlatRiverDischarge riverDischarge=null)
        {
            var f=new FlatTerrainFacts(w.Count){VegetationSeasons=new byte[(w.Width+2)*(w.Height+2)*12*4],
                RiverDischarge=riverDischarge??FlatRiverDischarge.Route(w,null,cancel,workers)};
            float meanderSeed=FMath.Unit(FMath.Seed(w.Seed))*6.2831853f;
            ushort[] fresh=Distance(w,i=>w.Water[i]==WaterKind.River||w.Water[i]==WaterKind.Lake,cancel);
            Array.Copy(fresh,f.FreshDistance,w.Count);
            // HAND-like elevation above the drainage receiver; lakes have horizontal spill surfaces.
            var receiver=new int[w.Count];
            // Reuse the unpublished meander cache for phase until the independent
            // row pass converts each cell to its final lateral offset.
            var channelPhase=f.Meander;
            uint phaseSeed=FMath.Seed(w.Seed)^0xc2b79d35u;
            uint treeSeed=FMath.Seed(w.Seed)^0x5d2a9b71u;
            for(int k=0;k<w.Count;k++)
            {
                if((k&4095)==0)cancel.ThrowIfCancellationRequested();
                int i=w.DrainOrder[k],p=w.Downstream[i];
                receiver[i]=w.Water[i]!=WaterKind.Land?i:p<0?i:receiver[p];
                f.Hand[i]=Math.Max(0,w.Elevation[i]-w.WaterLevel[receiver[i]]);
                if(w.Water[i]==WaterKind.River)
                {
                    // Count along the actual channel, so a bend has the same tile
                    // wavelength at every longitude and resolution. Tributaries
                    // inherit the downstream phase before advancing one reach.
                    float wavelength=7+36*RiverHalfWidth(w.Runoff[i]);
                    channelPhase[i]=p>=0&&w.Water[p]==WaterKind.River
                        ?channelPhase[p]+6.2831853f/wavelength
                        :meanderSeed+FMath.Unit((uint)(p>=0?p:i)^phaseSeed)*6.2831853f;
                }
            }
            // Independent rows read the finished drainage graph and write disjoint cells.
            // HAND and downstream sediment reductions remain ordered for reproducibility.
            double southBed=0,northBed=0;
            for(int x=0;x<w.Width;x++){southBed+=w.Elevation[x];northBed+=w.Elevation[w.Count-w.Width+x];}
            southBed/=w.Width;northBed/=w.Width;
            Parallel.For(0,w.Height,FlatWorkerPolicy.Options(cancel,workers),y=>
            {
                cancel.ThrowIfCancellationRequested();
                var footprint=new FlatTerrainFootprint(w,y,southBed,northBed);
                for(int x=0;x<w.Width;x++)
                {
                    int i=y*w.Width+x;
                    footprint.Measure(w.Elevation,x,w.Elevation[i],out f.Relief[i],out float gradient);
                    f.Gradient[i]=gradient;
                    float rain=0,temp=0,coldest=1000,warmest=-1000;
                    for(int m=0;m<12;m++)
                    {
                        float monthly=w.Temperature[i*12+m];
                        rain+=w.Precipitation[i*12+m];temp+=monthly/12f;
                        coldest=Math.Min(coldest,monthly);warmest=Math.Max(warmest,monthly);
                    }
                    f.AnnualRain[i]=rain;f.MeanTemperature[i]=temp;
                    float storage=75,mean=0,growingSoil=0,growingWeight=0;int saturated=0;
                    float riparian=0,waterHand=f.Hand[i];
                    // Neighbouring freshwater supports low banks, but not elevated ridges.
                    for(int oy=-2;oy<=2;oy++)for(int ox=-2;ox<=2;ox++)
                    {
                        int n=w.Index(x+ox,y+oy);if(n<0||(w.Water[n]!=WaterKind.River&&w.Water[n]!=WaterKind.Lake))continue;
                        float above=Math.Max(0,w.Elevation[i]-w.WaterLevel[n]);
                        float supply=w.Water[n]==WaterKind.Lake?.85f:.4f+.6f*w.Runoff[n]/(w.Runoff[n]+30);
                        float influence=supply*(float)Math.Exp(-Math.Sqrt(ox*ox+oy*oy)*.55-above/65);
                        riparian=Math.Max(riparian,influence);waterHand=Math.Min(waterHand,above);
                    }
                    // Three annual spins of a 150mm bucket. PET is a declared gameplay proxy,
                    // not Thornthwaite/Penman-Monteith or a physically calibrated groundwater model.
                    for(int m=0;m<36;m++)
                    {
                        float pet=Math.Max(0,w.Temperature[i*12+m%12]+5)*3f;
                        storage=FMath.Clamp(storage+w.Precipitation[i*12+m%12]+riparian*170-pet,0,150);
                        // Low banks retain access to perennial groundwater. The
                        // same distance and bank-height falloff excludes ridges.
                        storage=Math.Max(storage,riparian*150);
                        if(m>=24)
                        {
                            mean+=storage/1800f;if(storage>=120)saturated++;
                            float warmth=Math.Max(0,w.Temperature[i*12+m%12]-5);
                            growingSoil+=storage/150*warmth;growingWeight+=warmth;
                            f.VegetationSeasons[FlatEcology.SeasonOffset(w,i,m%12)+3]=(byte)(storage/150*255+.5f);
                        }
                    }
                    f.Wetness[i]=mean;f.SaturatedMonths[i]=(byte)saturated;
                    bool area=AreaWater(w.Water[i]);
                    f.Forms[i]=ClassifyForm(w.Elevation[i],f.Relief[i],gradient,f.Hand[i],fresh[i]<=1,area,
                        w.WaterLevel[i]>w.Elevation[i]||w.Downstream[i]<0);
                    Biome biome=ClassifyBiome(w.Climates[i],w.Elevation[i],temp,rain,(byte)saturated,gradient,waterHand,riparian>.18f,area,warmest);
                    Biome ecologyBiome=biome;
                    if(updateBiomes)
                    {
                        w.Biomes[i]=biome;
                        float bonus=fresh[i]<=1&&f.Hand[i]<=100?.15f:0;
                        w.Fertility[i]=area?0:FMath.Clamp((1-Math.Abs(temp-19)/42)*FMath.Clamp(rain/1100,0,1)*(1-Math.Max(0,w.Elevation[i]-600)/4200f)+bonus,.01f,1);
                    }
                    // Loading v1 keeps the serialized biome/fertility exact until explicit rebuild.
                    // Correct the derived surface of old highland snow labels
                    // using the same growing-season climate as new terrain.
                    bool legacyHighlandSnow=w.Biomes[i]==Biome.Snow&&w.Elevation[i]>=2680&&warmest>10&&biome!=Biome.Snow;
                    if(!legacyHighlandSnow)biome=w.Biomes[i];
                    GroundMaterial material=GroundMaterial.Soil;
                    if(biome==Biome.Snow)material=GroundMaterial.Snow;
                    else if(gradient>=500||biome==Biome.Alpine)material=GroundMaterial.Rock;
                    else if(fresh[i]<=1&&f.Hand[i]<=100)material=gradient<=100&&mean>.65f?GroundMaterial.Silt:GroundMaterial.Gravel;
                    else if(w.CoastDistance[i]==1&&w.Elevation[i]<=100&&gradient<=200)material=GroundMaterial.Sand;
                    else if(biome==Biome.Desert||biome==Biome.Beach)material=GroundMaterial.Sand;
                    f.Materials[i]=material;
                    // Persistent biome labels remain authoritative for saves and
                    // civilization. Reconstructed ecology follows the actual climate.
                    f.Vegetation[i]=FlatEcology.Classify(ecologyBiome,w.Climates[i],temp,rain,gradient,warmest);
                    float seasonalSoil=growingWeight>0?growingSoil/growingWeight:0;
                    float canopy=FlatEcology.ContinuousCanopy(ecologyBiome,temp,rain,seasonalSoil,gradient,warmest);
                    float stand=FlatEcology.StandVariation(w,x,y,treeSeed,out float clearing);
                    f.Canopy[i]=FlatEcology.StandCanopy(f.Vegetation[i],canopy,seasonalSoil,warmest,riparian,clearing);
                    bool freshBank=fresh[i]<=2&&waterHand<=100;
                    f.Trees[i]=FlatEcology.SelectTree(f.Vegetation[i],w.Climates[i],coldest,warmest,seasonalSoil,freshBank,w.CoastDistance[i]<=2,stand);
                    FlatEcology.BuildSeasons(w,f,i,coldest);
                    f.Freshness[i]=w.Water[i]==WaterKind.River||w.Water[i]==WaterKind.Lake?1:0;
                    f.Depth[i]=area?Math.Max(.25f,w.WaterLevel[i]-w.Elevation[i]):0;
                    if(w.Water[i]==WaterKind.River)
                    {
                        f.HalfWidth[i]=RiverHalfWidth(w.Runoff[i]);
                        f.Depth[i]=RiverDepth(w.Runoff[i],gradient);
                        // Walls beyond the immediate bank still constrain lateral
                        // migration, even when the channel's own water head is level.
                        float confinement=Math.Max(gradient,f.Relief[i]*.5f);
                        f.Meander[i]=RiverMeander(f.HalfWidth[i],confinement,channelPhase[i]);
                        for(int d=0;d<4;d++)
                        {
                            int n=w.Neighbor(i,d);if(n<0)continue;
                            if(n>=0&&(w.Downstream[i]==n||(w.Downstream[n]==i&&w.Water[n]!=WaterKind.Land)))f.Connections[i]|=(byte)(1<<d);
                        }
                        int parent=w.Downstream[i];
                        if(parent>=0){f.FlowX[i]=WrappedDX(parent%w.Width,x,w.Width);f.FlowY[i]=parent/w.Width-y;}
                    }
                    // High bits are SW, SE, NW, NE. On a river they identify its
                    // own branches. On a dry side tile they flag the corner
                    // wetted by a diagonal between its two face neighbours.
                    bool channel=w.Water[i]==WaterKind.River;
                    for(int dy=-1;dy<=1;dy+=2)for(int dx=-1;dx<=1;dx+=2)
                    {
                        int a=channel?i:w.Index(x+dx,y);
                        int b=channel?w.Index(x+dx,y+dy):w.Index(x,y+dy);
                        if(b<0)continue;
                        var wa=w.Water[a];var wb=w.Water[b];
                        if(wa==WaterKind.Land||wb==WaterKind.Land||(wa!=WaterKind.River&&wb!=WaterKind.River))continue;
                        if(w.Downstream[a]==b||w.Downstream[b]==a)f.Connections[i]|=(byte)(1<<DiagonalDirection(dx,dy));
                    }
                }
            });
            // Accumulate a dimensionless suspended-sediment proxy using the SAME drainage graph.
            var sedimentLoad=new float[w.Count];
            for(int i=0;i<w.Count;i++)
            {
                float cover=w.Biomes[i]==Biome.Rainforest||w.Biomes[i]==Biome.Woodland||w.Biomes[i]==Biome.Taiga?.75f:.2f;
                sedimentLoad[i]=AreaWater(w.Water[i])?0:f.AnnualRain[i]/1200f*(.05f+Math.Min(2,f.Gradient[i]/400f))*(1-cover);
            }
            for(int k=w.Count-1;k>=0;k--)
            {
                int i=w.DrainOrder[k],parent=w.Downstream[i];
                float concentration=sedimentLoad[i]/Math.Max(.01f,w.Runoff[i]);
                if(w.Water[i]==WaterKind.Lake)
                {
                    f.Sediment[i]=FMath.Clamp(.015f+concentration*.35f,.015f,.40f);
                    sedimentLoad[i]*=.20f+.45f*(float)Math.Exp(-f.Depth[i]/12);
                }
                else f.Sediment[i]=w.Water[i]==WaterKind.River?FMath.Clamp(.025f+concentration,.025f,.85f):0;
                if(parent>=0)sedimentLoad[parent]+=sedimentLoad[i]*.99f;
            }
            FlatEcology.ApplyNileCropland(w,f,cancel);
            FlatEcology.CompleteSeasonAtlas(w,f);
            BuildPlumes(w,f,cancel);FlatSurfaceSampling.BuildShore(w,f,cancel);
            f.RiverCurves=FlatRiverCurves.Build(w,f,cancel);w.Facts=f;
        }
        static void BuildPlumes(WorldData w,FlatTerrainFacts f,CancellationToken cancel)
        {
            int[] stamps=new int[w.Count],queue=new int[w.Count];int stamp=0;
            for(int i=0;i<w.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();int mouth=w.Downstream[i];
                if(w.Water[i]!=WaterKind.River||mouth<0||!AreaWater(w.Water[mouth]))continue;
                bool ocean=Ocean(w.Water[mouth]);
                if(ocean)f.MouthCount++;
                stamp++;int head=0,tail=0;queue[tail++]=mouth;stamps[mouth]=stamp;
                float dx=f.FlowX[i],dy=f.FlowY[i];float length=FMath.Clamp((ocean?4:2)+(float)Math.Sqrt(w.Runoff[i]/RiverThreshold(w))*(ocean?2:1.5f),2,ocean?16:8);
                while(head<tail)
                {
                    int n=queue[head++];float ox=WrappedDX(n%w.Width,i%w.Width,w.Width),oy=n/w.Width-i/w.Width;
                    float along=ox*dx+oy*dy,cross=-ox*dy+oy*dx,spread=.65f+Math.Max(0,along)*.55f;
                    if(along<0||along>length||Math.Abs(cross)>spread*2.6f)continue;
                    float density=(float)Math.Exp(-along/(length*.43f)-cross*cross/(2*spread*spread));
                    float old=f.Freshness[n],value=FMath.Clamp(density,0,1);f.Freshness[n]=Math.Max(old,value);
                    f.Sediment[n]=Math.Max(f.Sediment[n],value*f.Sediment[i]);
                    if(value>old){f.FlowX[n]=dx;f.FlowY[n]=dy;}
                    // Restrict a lake plume to the same horizontal basin surface.
                    for(int d=0;d<4;d++)
                    {
                        int next=w.Neighbor(n,d);if(next<0||stamps[next]==stamp)continue;
                        bool connected=ocean?Ocean(w.Water[next]):w.Water[next]==WaterKind.Lake&&w.WaterLevel[next]==w.WaterLevel[mouth];
                        if(connected){stamps[next]=stamp;queue[tail++]=next;}
                    }
                }
            }
        }
    }
}
