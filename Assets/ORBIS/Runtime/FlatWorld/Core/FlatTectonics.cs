using System;
using System.Collections.Generic;
using System.Threading;

namespace Orbis.Flat
{
    public enum TectonicSetting : byte { Interior, Collision, Subduction, IslandArc, Rift, Ridge, Transform }
    public enum VolcanoKind : byte { Arc, Hotspot, Rift }

    public readonly struct Volcano
    {
        public readonly int Tile;public readonly VolcanoKind Kind;public readonly float Activity;
        public Volcano(int tile,VolcanoKind kind,float activity){Tile=tile;Kind=kind;Activity=activity;}
    }

    public sealed class TectonicPlate
    {
        public float AxisX,AxisY,AxisZ,Speed;
        public bool Continental;
        public int Tiles;
    }

    // Immutable record of how a generated bed was built. Volcano sites, plate
    // margins and seismic belts are inputs for later eruption/earthquake events.
    public sealed class FlatTectonicFacts
    {
        public readonly TectonicPlate[] Plates;
        public readonly byte[] Plate;
        public readonly TectonicSetting[] Setting;
        public readonly float[] Seismicity;
        public readonly Volcano[] Volcanoes;
        internal FlatTectonicFacts(TectonicPlate[] plates,byte[] plate,TectonicSetting[] setting,float[] seismicity,Volcano[] volcanoes)
        {Plates=plates;Plate=plate;Setting=setting;Seismicity=seismicity;Volcanoes=volcanoes;}
    }

    // Plate-tectonic bed synthesis on the sphere. Plates rotate about Euler poles;
    // the relative motion across each margin decides collision ranges and
    // plateaus, subduction trenches with volcanic arcs, island arcs, rifts,
    // mid-ocean ridges and transform faults. Hotspots leave volcano chains along
    // plate motion. Distances are in reference tiles of a 512-wide world so the
    // same seed keeps its geography at every map size.
    public static class FlatTectonics
    {
        struct Margin { public TectonicSetting Setting;public float Convergence,Shear;public bool OwnContinental,OtherContinental,Overriding; }

        public static FlatTectonicFacts Build(WorldData w,CancellationToken cancel)
        {
            int n=w.Count,width=w.Width,height=w.Height;
            uint seed=FMath.Seed(w.Seed)^0x7ec70a1cu;var preset=w.Preset;
            float scale=width/512f;
            var px=new float[n];var py=new float[n];var pz=new float[n];var rowScale=new float[height];var capDistance=new float[n];
            // The polar ocean prior covers the same spherical area for every
            // seed. Offset its geological axis within the polar region so its
            // coastlines and relief need not follow exact lines of latitude.
            // Use half the original ocean-prior transition width as the tilt.
            // This is a generator prior, independent of the plate motion model.
            const double polarCore=.02,polarEdge=.14;
            const double capTilt=(polarEdge-polarCore)*Math.PI*.5;
            double azimuth=2*Math.PI*FMath.Unit(seed^0x36b52b1fu);
            float capX=(float)(Math.Sin(capTilt)*Math.Cos(azimuth)),capY=(float)(Math.Sin(capTilt)*Math.Sin(azimuth)),capZ=(float)Math.Cos(capTilt);
            for(int y=0;y<height;y++)
            {
                double lat=((y+.5)/height-.5)*Math.PI;rowScale[y]=(float)Math.Cos(lat);
                for(int x=0;x<width;x++)
                {
                    double lon=(x+.5)/width*2*Math.PI-Math.PI;int i=y*width+x;
                    px[i]=(float)(Math.Cos(lat)*Math.Cos(lon));py[i]=(float)(Math.Cos(lat)*Math.Sin(lon));pz[i]=(float)Math.Sin(lat);
                    float dot=px[i]*capX+py[i]*capY+pz[i]*capZ;
                    capDistance[i]=(float)(Math.Acos(Math.Min(1,Math.Abs(dot)))/Math.PI);
                }
            }
            // Plates: warped spherical Voronoi gives irregular, interlocking margins.
            int plateCount=preset==TerrainPreset.Archipelago?18:preset==TerrainPreset.GreatContinent?11:14;
            var plates=new TectonicPlate[plateCount];var sx=new float[plateCount];var sy=new float[plateCount];var sz=new float[plateCount];
            float superX=0,superY=0,superZ=0;RandomUnit(seed^0xa11ce5u,0,out superX,out superY,out superZ);
            for(int k=0;k<plateCount;k++)
            {
                RandomUnit(seed,k*3,out sx[k],out sy[k],out sz[k]);
                if(Math.Abs(sz[k])>.82f){sz[k]*=.82f/Math.Abs(sz[k]);Normalize(ref sx[k],ref sy[k],ref sz[k]);}
                var p=new TectonicPlate();RandomUnit(seed^0x51a7e0u,k,out p.AxisX,out p.AxisY,out p.AxisZ);
                p.Speed=.45f+1.1f*FMath.Unit(seed^(uint)(k*977+31));plates[k]=p;
            }
            var plateBias=new float[plateCount];
            for(int k=0;k<plateCount;k++)
            {
                float roll=FMath.Unit(seed^(uint)(k*7919+5));
                switch(preset)
                {
                    case TerrainPreset.GreatContinent:
                        float toward=sx[k]*superX+sy[k]*superY+sz[k]*superZ;plateBias[k]=toward>.25f?.20f:-.20f;break;
                    case TerrainPreset.Archipelago: plateBias[k]=roll<.25f?.03f:-.08f;break;
                    case TerrainPreset.InlandSea: plateBias[k]=0;break;
                    default: plateBias[k]=roll<.45f?.12f:-.10f;break;
                }
            }
            var plate=new byte[n];
            for(int i=0;i<n;i++)
            {
                if((i&8191)==0)cancel.ThrowIfCancellationRequested();
                float x=px[i],y=py[i],z=pz[i];
                float wx=x+.32f*Fbm(x*2.1f,y*2.1f,z*2.1f,seed^0x11u,3),wy=y+.32f*Fbm(x*2.1f,y*2.1f,z*2.1f,seed^0x22u,3),wz=z+.32f*Fbm(x*2.1f,y*2.1f,z*2.1f,seed^0x33u,3);
                int best=0;float bestDot=-9;
                for(int k=0;k<plateCount;k++){float d=wx*sx[k]+wy*sy[k]+wz*sz[k];if(d>bestDot){bestDot=d;best=k;}}
                plate[i]=(byte)best;plates[best].Tiles++;
            }
            // Crust: continental where a low-frequency field plus the plate's core
            // bias exceeds a quantile chosen for the preset's land share.
            var crust=new float[n];
            for(int i=0;i<n;i++)
            {
                float x=px[i],y=py[i],z=pz[i];
                float c=Fbm(x*1.5f,y*1.5f,z*1.5f,seed^0x44u,5)*.55f+Fbm(x*4f,y*4f,z*4f,seed^0x45u,4)*.18f+plateBias[plate[i]];
                if(preset==TerrainPreset.Archipelago)c=Fbm(x*3.2f,y*3.2f,z*3.2f,seed^0x46u,5)*.6f+plateBias[plate[i]];
                // Inland sea: a ring of land around an enclosed ocean centred on lon 0, lat 0.
                if(preset==TerrainPreset.InlandSea){float r=x+.42f*Fbm(x*2.2f,y*2.2f,z*2.2f,seed^0x47u,4);c+=r<.45f?.22f:-.65f*(r-.45f)/.55f;}
                crust[i]=c-.55f*(1-Smooth((float)polarCore,(float)polarEdge,capDistance[i]));
            }
            float target=preset==TerrainPreset.Archipelago?.12f:preset==TerrainPreset.GreatContinent?.33f:preset==TerrainPreset.InlandSea?.40f:.29f;
            float threshold=WeightedQuantile(crust,rowScale,width,1-target);
            for(int i=0;i<n;i++)crust[i]-=threshold;
            // A plate is continental when most of it is continental crust.
            var continentalTiles=new int[plateCount];
            for(int i=0;i<n;i++)if(crust[i]>0)continentalTiles[plate[i]]++;
            for(int k=0;k<plateCount;k++)plates[k].Continental=continentalTiles[k]*2>plates[k].Tiles;
            // Margins: classify every plate-boundary tile from relative velocity.
            var margin=new Margin[n];var source=new int[n];var distance=new float[n];var heap=new FloodHeap();
            for(int i=0;i<n;i++){source[i]=-1;distance[i]=float.MaxValue;}
            for(int i=0;i<n;i++)
            {
                if((i&8191)==0)cancel.ThrowIfCancellationRequested();
                int other=-1;
                for(int d=0;d<4;d++){int m=w.SphereNeighbor(i,d);if(m>=0&&plate[m]!=plate[i]){other=m;break;}}
                if(other<0)continue;
                var a=plates[plate[i]];var b=plates[plate[other]];
                Velocity(a,px[i],py[i],pz[i],out float ax,out float ay,out float az);
                Velocity(b,px[i],py[i],pz[i],out float bx,out float by,out float bz);
                float nx=px[other]-px[i],ny=py[other]-py[i],nz=pz[other]-pz[i];
                float radial=nx*px[i]+ny*py[i]+nz*pz[i];nx-=radial*px[i];ny-=radial*py[i];nz-=radial*pz[i];Normalize(ref nx,ref ny,ref nz);
                float rx=ax-bx,ry=ay-by,rz=az-bz;float convergence=rx*nx+ry*ny+rz*nz;
                float tx=rx-convergence*nx,ty=ry-convergence*ny,tz=rz-convergence*nz;float shear=(float)Math.Sqrt(tx*tx+ty*ty+tz*tz);
                bool own=crust[i]>0,opposite=crust[other]>0;
                float ownApproach=ax*nx+ay*ny+az*nz,otherApproach=-(bx*nx+by*ny+bz*nz);
                var mg=new Margin{Convergence=convergence,Shear=shear,OwnContinental=own,OtherContinental=opposite};
                if(convergence>.25f)
                {
                    if(own&&opposite){mg.Setting=TectonicSetting.Collision;mg.Overriding=ownApproach<otherApproach||(ownApproach==otherApproach&&plate[i]<plate[other]);}
                    else if(own!=opposite){mg.Setting=TectonicSetting.Subduction;mg.Overriding=own;}
                    else{mg.Setting=TectonicSetting.IslandArc;mg.Overriding=ownApproach<otherApproach||(ownApproach==otherApproach&&plate[i]<plate[other]);}
                }
                // Continents resist rifting: only fast extension splits them.
                else if(convergence<-.25f&&!own&&!opposite)mg.Setting=TectonicSetting.Ridge;
                else if(convergence<-.75f)mg.Setting=TectonicSetting.Rift;
                else if(shear>.45f)mg.Setting=TectonicSetting.Transform;
                else continue;
                margin[i]=mg;source[i]=i;distance[i]=0;heap.Push(i,0);
            }
            // Each tile inherits the nearest margin on its own plate.
            float reach=18*scale;
            while(heap.Count>0)
            {
                int c=heap.Pop(out long key);float dc=key/1000f;if(dc>distance[c])continue;
                for(int d=0;d<4;d++)
                {
                    int m=w.SphereNeighbor(c,d);if(m<0||plate[m]!=plate[c])continue;
                    float step=d<2?rowScale[c/width]:1,next=dc+step;
                    if(next>=distance[m]||next>reach)continue;
                    distance[m]=next;source[m]=source[c];heap.Push(m,(long)(next*1000));
                }
            }
            // Seafloor age: distance from the nearest spreading ridge, across plates.
            var age=new float[n];for(int i=0;i<n;i++)age[i]=float.MaxValue;
            for(int i=0;i<n;i++)if(source[i]==i&&(margin[i].Setting==TectonicSetting.Ridge||margin[i].Setting==TectonicSetting.Rift)){age[i]=0;heap.Push(i,0);}
            while(heap.Count>0)
            {
                int c=heap.Pop(out long key);float dc=key/1000f;if(dc>age[c])continue;
                for(int d=0;d<4;d++)
                {
                    int m=w.SphereNeighbor(c,d);if(m<0)continue;float next=dc+(d<2?rowScale[c/width]:1);
                    if(next>=age[m]||next>60*scale)continue;age[m]=next;heap.Push(m,(long)(next*1000));
                }
            }
            var elevation=new float[n];var setting=new TectonicSetting[n];var seismic=new float[n];var volcanoes=new List<Volcano>();
            for(int i=0;i<n;i++)
            {
                if((i&8191)==0)cancel.ThrowIfCancellationRequested();
                float x=px[i],y=py[i],z=pz[i],s=crust[i];
                float h;
                if(s>0)
                {
                    float interior=Smooth(0,.24f,s);
                    h=40+1550*(float)Math.Pow(interior,.78)*(.68f+.32f*Fbm(x*3,y*3,z*3,seed^0x55u,4));
                    // Intracratonic sags and old eroded orogens give interiors variety.
                    float sag=Smooth(.15f,.55f,Fbm(x*2.4f,y*2.4f,z*2.4f,seed^0x56u,3));
                    h-=380*sag*interior;
                    float old=Ridged(x*3.6f,y*3.6f,z*3.6f,seed^0x57u,4);
                    h+=1300*(float)Math.Pow(old,4)*interior*Smooth(-.1f,.3f,Fbm(x*1.8f,y*1.8f,z*1.8f,seed^0x58u,2));
                }
                else
                {
                    // Thermal subsidence away from a spreading ridge. At 2048 columns
                    // the ETOPO marine depth median is 4.1 km and its 99th percentile 6.0 km.
                    float cooled=FMath.Clamp((age[i]==float.MaxValue?60*scale:age[i])/(48*scale),0,1);
                    float floor=-2500-3400*(float)Math.Sqrt(cooled);
                    floor+=320*cooled*Fbm(x*5,y*5,z*5,seed^0x69u,3);
                    float shelf=-s;
                    h=shelf<.035f?-30-170*shelf/.035f:shelf<.11f?FMath.Lerp(-200,floor,Smooth(.035f,.11f,shelf)):floor;
                    h+=70*Fbm(x*3,y*3,z*3,seed^0x59u,3);
                }
                float uplift=0,seis=0;int src=source[i];
                if(src>=0)
                {
                    var mg=margin[src];float d=distance[i]/scale;
                    float strength=FMath.Clamp(Math.Abs(mg.Convergence)/1.2f,.35f,1.4f);
                    float along=.75f+.5f*Fbm(px[src]*7,py[src]*7,pz[src]*7,seed^0x60u,3);
                    if(d<2.5f)setting[i]=mg.Setting;
                    switch(mg.Setting)
                    {
                        case TectonicSetting.Collision:
                            uplift+=3300*strength*along*Bell(d,0,2.2f);
                            if(mg.Overriding)uplift+=2900*along*Plateau(d,1.5f,7+5*along)*Smooth(0,.6f,strength);
                            else uplift-=260*Bell(d,3.6f,1.6f);
                            seis=.85f*(float)Math.Exp(-d/4);break;
                        case TectonicSetting.Subduction:
                            if(mg.OwnContinental){uplift+=2600*strength*along*Bell(d,1.6f,1.5f)-260*Bell(d,4.5f,1.5f);seis=(float)Math.Exp(-d/4.5f);}
                            else{uplift-=1500*strength*Bell(d,.4f,.9f);seis=.8f*(float)Math.Exp(-d/1.5f);}
                            break;
                        case TectonicSetting.IslandArc:
                            if(mg.Overriding){uplift+=4100*(.7f+.3f*strength)*along*Bell(d,2,1.3f);seis=.9f*(float)Math.Exp(-d/3);}
                            else{uplift-=1400*strength*Bell(d,.4f,.9f);seis=.7f*(float)Math.Exp(-d/1.5f);}
                            break;
                        case TectonicSetting.Rift:
                            if(s>0){uplift+=-380*strength*Bell(d,0,.8f)+260*Bell(d,2,1.2f);}
                            else uplift+=1200*Bell(d,0,2.5f);
                            seis=.45f*(float)Math.Exp(-d/1.2f);break;
                        case TectonicSetting.Ridge:
                            uplift+=1300*Bell(d,0,2.8f);seis=.35f*(float)Math.Exp(-d/1);break;
                        case TectonicSetting.Transform:
                            uplift+=-220*Bell(d,0,.7f)+180*Bell(d,1.6f,.8f);seis=.9f*(float)Math.Exp(-d/1.2f);break;
                    }
                }
                // Ridged relief on uplifted belts reads as crests and spurs.
                float belt=FMath.Clamp(uplift/2500,0,1.3f);
                if(belt>0)uplift+=900*belt*(Ridged(x*9,y*9,z*9,seed^0x61u,4)-.45f);
                // Troughs on dry continental crust deepen valleys but do not open sea lanes.
                float total=h+uplift;if(s>0&&uplift<0&&total<25)total=Math.Min(h,25);
                elevation[i]=total;seismic[i]=FMath.Clamp(seis,0,1);
            }
            // Arc and rift volcanoes: sparse local hash maxima along the volcanic front.
            for(int i=0;i<n;i++)
            {
                int src=source[i];if(src<0)continue;var mg=margin[src];float d=distance[i]/scale;
                VolcanoKind kind;float activity;
                if(mg.Setting==TectonicSetting.Subduction&&mg.OwnContinental&&d>1.2f&&d<2.8f){kind=VolcanoKind.Arc;activity=.6f+.4f*FMath.Clamp(mg.Convergence,0,1);}
                else if(mg.Setting==TectonicSetting.IslandArc&&mg.Overriding&&d>1.3f&&d<2.7f){kind=VolcanoKind.Arc;activity=.7f;}
                else if(mg.Setting==TectonicSetting.Rift&&crust[i]>0&&d<1.6f){kind=VolcanoKind.Rift;activity=.35f;}
                else continue;
                if(!LocalHashMaximum(w,i,seed^0x62u,(int)Math.Ceiling(1.6f*scale)))continue;
                volcanoes.Add(new Volcano(i,kind,activity));
                Cone(w,elevation,i,kind==VolcanoKind.Rift?700:1300,scale);
            }
            // Hotspots: an active head plus an ageing, subsiding chain behind it.
            int hotspots=preset==TerrainPreset.Archipelago?7:4;
            for(int k=0;k<hotspots;k++)
            {
                RandomUnit(seed^0x407u,k,out float hx,out float hy,out float hz);
                if(Math.Abs(hz)>.7f)continue;
                int tile=TileOf(w,hx,hy,hz);var pl=plates[plate[tile]];
                for(int step=0;step<7;step++)
                {
                    int t=TileOf(w,hx,hy,hz);float fade=1-step/7f;
                    if(crust[t]>0)
                    {
                        if(step==0)Plateau(w,elevation,t,1100,3.2f*scale);
                        Cone(w,elevation,t,900*fade,scale);
                    }
                    else Cone(w,elevation,t,(elevation[t]<0?-elevation[t]:0)+900*fade-(step>2?600:0),scale);
                    volcanoes.Add(new Volcano(t,VolcanoKind.Hotspot,step==0?1:Math.Max(0,.4f-step*.1f)));
                    // Trail upstream of plate motion: positions the plate carried away.
                    Velocity(pl,hx,hy,hz,out float vx,out float vy,out float vz);
                    float speed=(float)Math.Sqrt(vx*vx+vy*vy+vz*vz);if(speed<1e-4f)break;
                    float stepLength=.035f/speed;
                    hx-=vx*stepLength;hy-=vy*stepLength;hz-=vz*stepLength;Normalize(ref hx,ref hy,ref hz);
                }
            }
            for(int i=0;i<n;i++)
            {
                // Confine the polar depression to shelves and shallower crust.
                float pole=1-Smooth(0,.05f,capDistance[i]);
                float bed=elevation[i],headroom=FMath.Clamp((bed+4000)/4000,0,1);
                float h=bed-1600*pole*headroom*headroom;
                w.Elevation[i]=(short)Math.Round(FMath.Clamp(h,HeightBands.MinimumBed,HeightBands.MaximumBed));
            }
            return new FlatTectonicFacts(plates,plate,setting,seismic,volcanoes.ToArray());
        }

        static float Bell(float d,float centre,float width){float t=(d-centre)/width;return (float)Math.Exp(-t*t);}
        static float Plateau(float d,float start,float end){return Smooth(start-1.2f,start+.8f,d)*(1-Smooth(end-1.5f,end+1.5f,d));}
        static float Smooth(float a,float b,float x){float t=FMath.Clamp((x-a)/(b-a),0,1);return t*t*(3-2*t);}
        static void Normalize(ref float x,ref float y,ref float z){float l=(float)Math.Sqrt(x*x+y*y+z*z);if(l<1e-9f){x=1;y=z=0;return;}x/=l;y/=l;z/=l;}
        static void RandomUnit(uint seed,int index,out float x,out float y,out float z)
        {
            float u=FMath.Unit(seed^(uint)(index*2654435761u)^0x9e37u)*2-1,phi=FMath.Unit(seed^(uint)(index*40503u+17)^0x7f4au)*6.2831853f;
            float r=(float)Math.Sqrt(Math.Max(0,1-u*u));x=r*(float)Math.Cos(phi);y=r*(float)Math.Sin(phi);z=u;
        }
        static void Velocity(TectonicPlate p,float x,float y,float z,out float vx,out float vy,out float vz)
        {
            vx=(p.AxisY*z-p.AxisZ*y)*p.Speed;vy=(p.AxisZ*x-p.AxisX*z)*p.Speed;vz=(p.AxisX*y-p.AxisY*x)*p.Speed;
        }
        static int TileOf(WorldData w,float x,float y,float z)
        {
            double lat=Math.Asin(FMath.Clamp(z,-1,1)),lon=Math.Atan2(y,x);
            int tx=FMath.Wrap((int)Math.Floor((lon+Math.PI)/(2*Math.PI)*w.Width),w.Width);
            int ty=FMath.Clamp((int)Math.Floor((lat/Math.PI+.5)*w.Height),0,w.Height-1);
            return ty*w.Width+tx;
        }
        // A disk measured in equatorial tile widths on the sphere. A polar
        // disk can include every longitude in a row, including its opposite
        // meridian. Each covered tile is visited once across the date line.
        static IEnumerable<(int Tile,float Distance)> SurfaceDisk(WorldData w,int tile,float radius)
        {
            int x=tile%w.Width,y=tile/w.Width,rows=(int)Math.Ceiling(radius);
            double angle=2*Math.PI/w.Width,latitude=(y+.5-w.Height*.5)*angle;
            double cosCenter=Math.Cos(latitude),sinRadius=Math.Sin(radius*angle*.5);
            double limit=sinRadius*sinRadius;
            for(int row=Math.Max(0,y-rows);row<=Math.Min(w.Height-1,y+rows);row++)
            {
                double north=Math.Sin((row-y)*angle*.5),north2=north*north;
                if(north2>limit)continue;
                double lonFactor=cosCenter*Math.Cos((row+.5-w.Height*.5)*angle);
                double span=2*Math.Asin(Math.Sqrt(Math.Min(1,(limit-north2)/lonFactor)))/angle;
                int dxMax=Math.Min(w.Width/2,(int)Math.Floor(span+1e-8));
                for(int dx=-dxMax;dx<=Math.Min(dxMax,(w.Width-1)/2);dx++)
                {
                    double east=Math.Sin(dx*angle*.5);
                    float distance=(float)(2*Math.Asin(Math.Sqrt(Math.Min(1,north2+lonFactor*east*east)))/angle);
                    yield return (w.Index(x+dx,row),distance);
                }
            }
        }
        static bool LocalHashMaximum(WorldData w,int tile,uint seed,int radius)
        {
            float own=FMath.Unit((uint)tile^seed);
            foreach(var p in SurfaceDisk(w,tile,radius))
                if(p.Tile!=tile&&FMath.Unit((uint)p.Tile^seed)>=own)return false;
            return true;
        }
        internal static void Cone(WorldData w,float[] h,int tile,float height,float scale)
        {
            foreach(var p in SurfaceDisk(w,tile,2*scale))
            {
                float d=p.Distance/scale;
                h[p.Tile]+=height*(float)Math.Exp(-d*d*1.8f);
            }
        }
        static void Plateau(WorldData w,float[] h,int tile,float height,float radius)
        {
            foreach(var p in SurfaceDisk(w,tile,1.5f*radius))
                h[p.Tile]+=height*(1-Smooth(radius*.6f,radius*1.5f,p.Distance));
        }
        static float WeightedQuantile(float[] values,float[] rowScale,int width,float q)
        {
            var order=new int[values.Length];for(int i=0;i<order.Length;i++)order[i]=i;
            Array.Sort(order,(a,b)=>values[a]!=values[b]?values[a].CompareTo(values[b]):a.CompareTo(b));
            double total=0;foreach(int i in order)total+=rowScale[i/width];
            double sum=0;foreach(int i in order){sum+=rowScale[i/width];if(sum>=total*q)return values[i];}
            return values[order[order.Length-1]];
        }

        // 3D lattice value noise sampled on the unit sphere: seamless at the dateline and poles.
        static float Lattice(int x,int y,int z,uint seed)
        { unchecked{return FMath.Unit((uint)x*73856093u^(uint)y*19349663u^(uint)z*83492791u^seed)*2-1;} }
        static float Value(float x,float y,float z,uint seed)
        {
            // Off-grid phases keep the smooth lattice's zero derivative from
            // following the date line or equator through every noise octave.
            x+=.31f;y+=.37f;z+=.23f;
            int x0=(int)Math.Floor(x),y0=(int)Math.Floor(y),z0=(int)Math.Floor(z);
            float fx=x-x0,fy=y-y0,fz=z-z0;fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy);fz=fz*fz*(3-2*fz);
            float a=FMath.Lerp(Lattice(x0,y0,z0,seed),Lattice(x0+1,y0,z0,seed),fx),b=FMath.Lerp(Lattice(x0,y0+1,z0,seed),Lattice(x0+1,y0+1,z0,seed),fx);
            float c=FMath.Lerp(Lattice(x0,y0,z0+1,seed),Lattice(x0+1,y0,z0+1,seed),fx),d=FMath.Lerp(Lattice(x0,y0+1,z0+1,seed),Lattice(x0+1,y0+1,z0+1,seed),fx);
            return FMath.Lerp(FMath.Lerp(a,b,fy),FMath.Lerp(c,d,fy),fz);
        }
        internal static float Fbm(float x,float y,float z,uint seed,int octaves)
        {
            float sum=0,amplitude=1,norm=0;
            for(int o=0;o<octaves;o++){sum+=amplitude*Value(x,y,z,seed+(uint)o*1013u);norm+=amplitude;amplitude*=.5f;x*=2.03f;y*=2.03f;z*=2.03f;}
            return sum/norm;
        }
        static float Ridged(float x,float y,float z,uint seed,int octaves)
        {
            float sum=0,amplitude=1,norm=0,weight=1;
            for(int o=0;o<octaves;o++)
            {
                float r=1-Math.Abs(Value(x,y,z,seed+(uint)o*1013u));r*=r*weight;weight=FMath.Clamp(r*1.6f,0,1);
                sum+=amplitude*r;norm+=amplitude;amplitude*=.5f;x*=2.07f;y*=2.07f;z*=2.07f;
            }
            return sum/norm;
        }
    }
}
