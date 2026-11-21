using System;
using System.Collections.Generic;
using System.Threading;

namespace Orbis.Flat
{
    public readonly struct FlatRiverSample
    {
        public readonly float Distance,HalfWidth,Depth,FlowX,FlowY,Sediment;

        internal FlatRiverSample(float distance,float halfWidth,float depth,float flowX,float flowY,float sediment)
        {
            Distance=distance;HalfWidth=halfWidth;Depth=depth;
            FlowX=flowX;FlowY=flowY;Sediment=sediment;
        }
    }

    // Immutable presentation geometry reconstructed from the conserved drainage graph.
    // All queries use the same global ribbon, including banks outside river-owned tiles.
    public sealed partial class FlatRiverCurves
    {
        const int SamplesPerReach=16;
        const float FieldMargin=.30f;
        readonly Segment[] segments;
        readonly int[] tileOffsets,tileSegments,activeTiles;
        readonly float[] longitudeScale;
        readonly bool[] editedRiverTiles;
        public bool HasSurvey { get; }
        public FlatRiverStageField Seasons { get; private set; }

        public readonly struct Vertex
        {
            public readonly float X,Y,Radius,Depth,FlowX,FlowY,Sediment;
            public Vertex(float x,float y,float radius,float depth,float flowX,float flowY,float sediment)
            {
                X=x;Y=y;Radius=radius;Depth=depth;
                FlowX=flowX;FlowY=flowY;Sediment=sediment;
            }
        }

        public readonly struct Segment
        {
            public readonly Vertex A,B;
            public readonly int Reach;
            public Segment(Vertex a,Vertex b,int reach){A=a;B=b;Reach=reach;}
        }

        public int Width { get; }
        public int Height { get; }
        public int SegmentCount { get { return segments.Length; } }
        public int ActiveTileCount { get { return activeTiles.Length; } }
        public int GetActiveTile(int index) { return activeTiles[index]; }
        public int TileSegmentCount(int tile) => tileOffsets[tile+1]-tileOffsets[tile];
        public int TileSegmentIndex(int tile,int index) => tileSegments[tileOffsets[tile]+index];
        public Segment SegmentAt(int index) => segments[index];
        public Segment TileSegment(int tile,int index) => segments[TileSegmentIndex(tile,index)];

        FlatRiverCurves(int width,int height,Segment[] segments,int[] offsets,int[] candidates,int[] active,
            bool hasSurvey,bool[] editedTiles)
        {
            Width=width;Height=height;this.segments=segments;
            HasSurvey=hasSurvey;editedRiverTiles=editedTiles;
            tileOffsets=offsets;tileSegments=candidates;activeTiles=active;
            longitudeScale=new float[height+1];
            for(int y=0;y<=height;y++)longitudeScale[y]=Metric(y,height);
        }

        public FlatRiverSample Sample(float worldX,float worldY)
        {
            return Sample(worldX,worldY,out _);
        }

        internal FlatRiverSample Sample(float worldX,float worldY,out int reach)
        {
            reach=-1;
            if(!FMath.Finite(worldX)||!FMath.Finite(worldY)||segments.Length==0)
                return new FlatRiverSample(8,0,0,0,0,0);
            worldX%=Width;if(worldX<0)worldX+=Width;
            worldY=FMath.Clamp(worldY,0,Height);
            int row=Math.Min(Height-1,(int)worldY),tile=row*Width+(int)worldX;
            float metric=FMath.Lerp(longitudeScale[row],longitudeScale[row+1],worldY-row);
            float best=8,radius=0,depth=0,flowX=0,flowY=0,sediment=0;
            for(int k=tileOffsets[tile];k<tileOffsets[tile+1];k++)
            {
                Segment segment=segments[tileSegments[k]];
                Vertex a=segment.A,b=segment.B;
                float px=Wrapped(worldX-a.X,Width)*metric,py=worldY-a.Y;
                float dx=(b.X-a.X)*metric,dy=b.Y-a.Y,lengthSquared=dx*dx+dy*dy;
                float t=lengthSquared>1e-12f?FMath.Clamp((px*dx+py*dy)/lengthSquared,0,1):0;
                float ex=px-dx*t,ey=py-dy*t;
                float r=FMath.Lerp(a.Radius,b.Radius,t);
                float distance=(float)Math.Sqrt(ex*ex+ey*ey)-r;
                if(distance>=best)continue;
                best=distance;radius=r;depth=FMath.Lerp(a.Depth,b.Depth,t);reach=segment.Reach;
                flowX=FMath.Lerp(a.FlowX,b.FlowX,t);flowY=FMath.Lerp(a.FlowY,b.FlowY,t);
                sediment=FMath.Lerp(a.Sediment,b.Sediment,t);
            }
            Normalize(ref flowX,ref flowY);
            return new FlatRiverSample(best,radius,depth,flowX,flowY,sediment);
        }

        public FlatRiverSample SampleAtDay(float x,float y,double day)
        {
            var sample=Sample(x,y);
            if(Seasons==null||sample.HalfWidth<=0)return sample;
            float stage=Seasons.Ratio(x,y,day);
            // A mixed edited Earth also contains ungauged, surveyed estuaries
            // wider than the procedural flood-stage limit at their mean stage.
            float radius=Math.Min(Math.Max(.24f,sample.HalfWidth),sample.HalfWidth*FlatRiverStageField.WidthScale(stage));
            return new FlatRiverSample(sample.Distance+sample.HalfWidth-radius,radius,
                sample.Depth*FlatRiverStageField.DepthScale(stage),sample.FlowX,sample.FlowY,sample.Sediment);
        }

        public static FlatRiverCurves Build(WorldData world,FlatTerrainFacts facts,CancellationToken cancel)
        {
            if(world==null||facts==null||facts.Forms.Length!=world.Count)
                throw new ArgumentException("River curves require matching terrain facts.");
            cancel.ThrowIfCancellationRequested();
            bool surveyed=world.Preset==TerrainPreset.Earth && world.RiverReference!=null && world.WaterReference!=null;
            var edited=surveyed?FindEditedTiles(world,cancel):null;
            var built=surveyed && edited==null ? new List<Segment>() : BuildDrainageCurves(world,facts,cancel);
            if(surveyed)AddSurvey(world,built,cancel);
            var curves=Index(world.Width,world.Height,built.ToArray(),cancel,surveyed,edited);
            if(facts.RiverDischarge!=null && built.Count>0 && (!surveyed || edited!=null))
                curves.Seasons=new FlatRiverStageField(curves,facts.RiverDischarge,cancel);
            return curves;
        }

        static List<Segment> BuildDrainageCurves(WorldData world,FlatTerrainFacts facts,CancellationToken cancel)
        {
            int count=world.Count;
            var river=new bool[count];var dominant=new int[count];var areaSource=new int[count];var indegree=new int[count];
            var order=new int[count];var phase=new float[count];
            var nodeX=new float[count];var nodeY=new float[count];
            var tangentX=new float[count];var tangentY=new float[count];
            var radii=new float[count];var depths=new float[count];
            int riverCount=0;
            for(int i=0;i<count;i++)
            {
                dominant[i]=-1;areaSource[i]=-1;
                river[i]=world.Water[i]==WaterKind.River&&facts.HalfWidth[i]>=.01f;
                if(river[i])riverCount++;
            }
            for(int i=0;i<count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                int next=world.Downstream[i];
                if(next<0||next>=count||!river[next])continue;
                if(!river[i])
                {
                    if(!FlatTerrainAnalysis.AreaWater(world.Water[i])||!ReachEdge(world,i,next))continue;
                    int source=areaSource[next];
                    if(source<0||world.Runoff[i]>world.Runoff[source]||
                        (world.Runoff[i]==world.Runoff[source]&&i<source))areaSource[next]=i;
                    continue;
                }
                indegree[next]++;
                int previous=dominant[next];
                if(previous<0||world.Runoff[i]>world.Runoff[previous]||
                    (world.Runoff[i]==world.Runoff[previous]&&i<previous))dominant[next]=i;
            }
            int head=0,tail=0;
            for(int i=0;i<count;i++)if(river[i]&&indegree[i]==0)order[tail++]=i;
            while(head<tail)
            {
                if((head&4095)==0)cancel.ThrowIfCancellationRequested();
                int i=order[head++],next=world.Downstream[i];
                if(next>=0&&next<count&&river[next]&&--indegree[next]==0)order[tail++]=next;
            }
            // Corrupt legacy cycles remain finite. The authoritative graph is untouched.
            if(tail<riverCount)for(int i=0;i<count;i++)if(river[i]&&indegree[i]>0)order[tail++]=i;
            uint seed=FMath.Seed(world.Seed)^0x793a927du;
            for(int k=tail-1;k>=0;k--)
            {
                int i=order[k],next=world.Downstream[i];
                float reachLength=1;
                if(next>=0&&next<count&&ReachEdge(world,i,next))
                {
                    EdgeDirection(world,i,next,out float dx,out float dy);
                    dx*=Metric(i/world.Width+.5f,world.Height);
                    reachLength=(float)Math.Sqrt(dx*dx+dy*dy);
                }
                float advance=6.2831853f*reachLength/(8+24*facts.HalfWidth[i]);
                phase[i]=next>=0&&next<count&&river[next]&&indegree[i]==0
                    ?phase[next]+advance:FMath.Unit((uint)(next>=0?next:i)^seed)*6.2831853f;
            }
            for(int k=0;k<tail;k++)
            {
                if((k&1023)==0)cancel.ThrowIfCancellationRequested();
                int i=order[k];
                Direction(world,river,dominant,areaSource,i,out float tx,out float ty);
                tangentX[i]=tx;tangentY[i]=ty;
                float confinement=Math.Max(facts.Gradient[i],facts.Relief[i]*.5f);
                float amplitude=(.46f+2.6f*facts.HalfWidth[i])/(1+confinement/190);
                int next=world.Downstream[i];
                bool spring=dominant[i]<0&&areaSource[i]<0;
                if(spring&&(next<0||next>=count||(!river[next]&&!FlatTerrainAnalysis.AreaWater(world.Water[next]))))amplitude=0;
                float desired=amplitude*(.80f*(float)Math.Sin(phase[i])+.20f*(float)Math.Sin(phase[i]*.43f+1.3f));
                float radius=FMath.Clamp(facts.HalfWidth[i]*(.89f+.16f*(float)Math.Sin(phase[i]*1.71f+.7f))
                    *(.76f+.24f/(1+confinement/300)),.006f,.22f);
                if(spring)radius=Math.Max(.006f,radius*.24f);
                float cx=i%world.Width+.5f,cy=i/world.Width+.5f;
                BankNormal(tx,ty,Metric(cy,world.Height),out float nx,out float ny);
                radius=FitRadius(world,cx,cy,world.WaterLevel[i],Tolerance(facts.Relief[i]),nx,ny,radius);
                radii[i]=radius;
                depths[i]=Math.Max(.15f,facts.Depth[i]*(.92f+.10f*(float)Math.Sin(phase[i]*1.71f+.7f)));
                float offset=ValleyOffset(world,facts,i,nx,ny,desired,amplitude,radius);
                float x=cx+nx*offset,y=FMath.Clamp(cy+ny*offset,0,world.Height);
                int upstream=dominant[i];
                // A raster cell is a drainage catchment, not a compulsory bend
                // vertex. Cut the inside of a grid elbow only on admissible ground.
                if(upstream>=0&&next>=0&&next<count&&river[next]&&
                   !PolarEdge(world,upstream,i)&&!PolarEdge(world,i,next))
                {
                    EdgeDirection(world,upstream,i,out float inX,out float inY);
                    EdgeDirection(world,i,next,out float outX,out float outY);
                    float rounding=.32f/(1+confinement/450);
                    x+=(outX-inX)*rounding;y+=(outY-inY)*rounding;
                    Constrain(world,cx,cy,world.WaterLevel[i],Tolerance(facts.Relief[i]),radius,nx,ny,ref x,ref y);
                }
                nodeX[i]=x;nodeY[i]=y;
            }
            var built=new List<Segment>(Math.Max(4,riverCount*SamplesPerReach));
            for(int k=0;k<tail;k++)
            {
                if((k&255)==0)cancel.ThrowIfCancellationRequested();
                int i=order[k],next=world.Downstream[i];
                if(next<0||next>=count||(!river[next]&&!FlatTerrainAnalysis.AreaWater(world.Water[next])))
                {
                    if(dominant[i]<0&&areaSource[i]<0)
                    {
                        var spring=new Vertex(nodeX[i],nodeY[i],Math.Max(.012f,radii[i]),depths[i],tangentX[i],tangentY[i],facts.Sediment[i]);
                        built.Add(new Segment(spring,spring,i));
                    }
                    continue;
                }
                if(PolarEdge(world,i,next))
                {
                    AddPolar(world,facts,i,next,river,radii,depths,nodeX,nodeY,tangentX,tangentY,built);
                    continue;
                }
                if(!ReachEdge(world,i,next))continue;
                AddReach(world,facts,i,next,river,dominant,radii,depths,nodeX,nodeY,tangentX,tangentY,built);
            }
            // Lake spill edges carry the same authoritative flow as river edges.
            // Their wet-centre anchors overlap basin coverage and meet the first
            // river node at its full width, depth and shared downstream tangent.
            for(int i=0;i<count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                int next=world.Downstream[i];
                if(!FlatTerrainAnalysis.AreaWater(world.Water[i])||next<0||next>=count||!river[next]||!ReachEdge(world,i,next))continue;
                nodeX[i]=i%world.Width+.5f;nodeY[i]=i/world.Width+.5f;
                EdgeDirection(world,i,next,out float tx,out float ty);Normalize(ref tx,ref ty);
                tangentX[i]=tx;tangentY[i]=ty;
                radii[i]=radii[next];depths[i]=Math.Max(.15f,facts.Depth[i]);
                if(PolarEdge(world,i,next))
                    AddPolar(world,facts,i,next,river,radii,depths,nodeX,nodeY,tangentX,tangentY,built);
                else AddReach(world,facts,i,next,river,dominant,radii,depths,nodeX,nodeY,tangentX,tangentY,built);
            }
            return built;
        }

        static float Wrapped(float difference,int width)
        {
            if(difference>width*.5f)difference-=width;
            if(difference< -width*.5f)difference+=width;
            return difference;
        }

        static void Normalize(ref float x,ref float y)
        {
            float length=(float)Math.Sqrt(x*x+y*y);
            if(length>1e-8f){x/=length;y/=length;}
        }

        static bool PolarEdge(WorldData world,int a,int b)
        {
            int y=a/world.Width;
            return (y==0||y==world.Height-1)&&b/world.Width==y&&
                Math.Abs(a%world.Width-b%world.Width)==world.Width/2;
        }

        static bool ReachEdge(WorldData world,int a,int b)
        {
            return FlatHydrology.IsDrainageNeighbor(world,a,b);
        }

        static float Metric(float y,int height)
        { return Math.Max(.08f,(float)Math.Cos((y/height-.5)*Math.PI)); }

        static void BankNormal(float tx,float ty,float metric,out float nx,out float ny)
        {
            float east=tx*metric,north=ty;
            Normalize(ref east,ref north);
            nx=-north/metric;ny=east;
        }

        static void EdgeDirection(WorldData world,int a,int b,out float x,out float y)
        {
            x=FlatTerrainAnalysis.WrappedDX(b%world.Width,a%world.Width,world.Width);
            y=b/world.Width-a/world.Width;
            if(PolarEdge(world,a,b)){x=0;y=a/world.Width==0?-1:1;}
        }

        static void Direction(WorldData world,bool[] river,int[] dominant,int[] areaSource,int i,out float x,out float y)
        {
            x=0;y=0;int at=i;float weight=1;
            for(int k=0;k<3;k++)
            {
                int next=world.Downstream[at];
                if(next<0||next>=world.Count||(!river[next]&&!FlatTerrainAnalysis.AreaWater(world.Water[next])))break;
                EdgeDirection(world,at,next,out float dx,out float dy);x+=dx*weight;y+=dy*weight;
                if(!river[next]||PolarEdge(world,at,next))break;
                at=next;weight*=.45f;
            }
            at=i;weight=1;
            for(int k=0;k<3;k++)
            {
                int previous=dominant[at];if(previous<0)previous=areaSource[at];if(previous<0)break;
                EdgeDirection(world,previous,at,out float dx,out float dy);
                if(PolarEdge(world,previous,at)){dx=0;dy=at/world.Width==0?1:-1;}
                x+=dx*weight;y+=dy*weight;
                if(PolarEdge(world,previous,at))break;
                at=previous;weight*=.45f;
            }
            if(x*x+y*y<1e-8f)
            {
                int next=world.Downstream[i];
                if(next>=0&&next<world.Count)EdgeDirection(world,i,next,out x,out y);
                if(x*x+y*y<1e-8f){x=1;y=0;}
            }
            Normalize(ref x,ref y);
        }

        static float Terrain(WorldData world,float x,float y)
        {
            int bx=(int)Math.Floor(x-.5f),by=(int)Math.Floor(y-.5f);
            float tx=x-.5f-bx,ty=y-.5f-by;
            int low=FMath.Clamp(by,0,world.Height-1),high=FMath.Clamp(by+1,0,world.Height-1);
            float a=FMath.Lerp(world.Elevation[world.Index(bx,low)],world.Elevation[world.Index(bx+1,low)],tx);
            float b=FMath.Lerp(world.Elevation[world.Index(bx,high)],world.Elevation[world.Index(bx+1,high)],tx);
            return FMath.Lerp(a,b,ty);
        }

        static float Tolerance(float relief) { return FMath.Clamp(relief*.12f,12,120); }

        static float BankHeight(WorldData world,float x,float y,float nx,float ny,float radius)
        {
            return Math.Max(Terrain(world,x,y),Math.Max(Terrain(world,x+nx*radius,y+ny*radius),
                Terrain(world,x-nx*radius,y-ny*radius)));
        }

        static float FitRadius(WorldData world,float x,float y,float head,float tolerance,float nx,float ny,float radius)
        {
            if(BankHeight(world,x,y,nx,ny,radius)<=head+tolerance)return radius;
            float lo=0,hi=radius;
            for(int k=0;k<8;k++)
            {
                float candidate=(lo+hi)*.5f;
                if(BankHeight(world,x,y,nx,ny,candidate)<=head+tolerance)lo=candidate;else hi=candidate;
            }
            return Math.Max(.006f,lo);
        }

        static float ValleyOffset(WorldData world,FlatTerrainFacts facts,int i,float nx,float ny,float desired,float amplitude,float radius)
        {
            float cx=i%world.Width+.5f,cy=i/world.Width+.5f;
            float tolerance=Tolerance(facts.Relief[i]),head=world.WaterLevel[i];
            float bestOffset=0,bestCost=float.MaxValue;
            for(int k=-9;k<=9;k++)
            {
                float offset=k==0?desired:amplitude*k/9;
                float x=cx+nx*offset,y=cy+ny*offset;
                if(y<0||y>world.Height)continue;
                float elevation=BankHeight(world,x,y,nx,ny,radius);
                float excess=Math.Max(0,elevation-head-tolerance);
                float cost=(offset-desired)*(offset-desired)+excess*excess/Math.Max(1,tolerance*tolerance)*20;
                // Compare beds as well as water heads so a real off-axis valley
                // can redirect a bend inside the admissible flood corridor.
                cost+=FMath.Clamp((elevation-world.Elevation[i])/Math.Max(1,tolerance),-3,3)*.18f;
                if(cost<bestCost){bestCost=cost;bestOffset=offset;}
            }
            float chosenX=cx+nx*bestOffset,chosenY=cy+ny*bestOffset;
            Constrain(world,cx,cy,head,tolerance,radius,nx,ny,ref chosenX,ref chosenY);
            return ((chosenX-cx)*nx+(chosenY-cy)*ny)/Math.Max(1e-8f,nx*nx+ny*ny);
        }

        static void Constrain(WorldData world,float bx,float by,float head,float tolerance,float radius,float nx,float ny,ref float x,ref float y)
        {
            y=FMath.Clamp(y,0,world.Height);
            if(BankHeight(world,x,y,nx,ny,radius)<=head+tolerance)return;
            float targetX=x,targetY=y,lo=0,hi=1;
            for(int k=0;k<7;k++)
            {
                float t=(lo+hi)*.5f;
                float qx=FMath.Lerp(bx,targetX,t),qy=FMath.Lerp(by,targetY,t);
                if(BankHeight(world,qx,qy,nx,ny,radius)<=head+tolerance)lo=t;else hi=t;
            }
            x=FMath.Lerp(bx,targetX,lo);y=FMath.Lerp(by,targetY,lo);
        }

        static void AddReach(WorldData world,FlatTerrainFacts facts,int i,int next,bool[] river,int[] dominant,
            float[] radii,float[] depths,float[] nodeX,float[] nodeY,float[] tangentX,float[] tangentY,List<Segment> built)
        {
            bool continuing=river[next];
            float ax=nodeX[i],ay=nodeY[i];
            float bx=continuing?nodeX[next]:next%world.Width+.5f;
            float by=continuing?nodeY[next]:next/world.Width+.5f;
            bx=ax+Wrapped(bx-ax,world.Width);
            float tx=continuing?tangentX[next]:tangentX[i],ty=continuing?tangentY[next]:tangentY[i];
            float length=(float)Math.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay));
            float handle=Math.Min(1.25f,length)*.34f;
            float c1x=ax+tangentX[i]*handle,c1y=ay+tangentY[i]*handle;
            float c2x=bx-tx*handle,c2y=by-ty*handle;
            bool main=continuing&&(dominant[next]==i||FlatTerrainAnalysis.AreaWater(world.Water[i]));
            float endRadius=main?radii[next]:Math.Min(.22f,radii[i]*(continuing?1:1.10f));
            int previous=dominant[i],after=continuing?world.Downstream[next]:-1;
            float beforeRadius=previous>=0?radii[previous]:radii[i];
            float afterRadius=main&&after>=0&&after<world.Count&&river[after]&&dominant[after]==next?radii[after]:endRadius;
            float endDepth=main?depths[next]:depths[i];
            float endSediment=main?facts.Sediment[next]:facts.Sediment[i];
            float baseAX=i%world.Width+.5f,baseAY=i/world.Width+.5f;
            float baseBX=baseAX+FlatTerrainAnalysis.WrappedDX(next%world.Width,i%world.Width,world.Width);
            float baseBY=next/world.Width+.5f;
            bool outlet=FlatTerrainAnalysis.AreaWater(world.Water[i]);
            Vertex previousVertex=default;
            for(int k=0;k<=SamplesPerReach;k++)
            {
                float t=k/(float)SamplesPerReach,s=1-t;
                float x=s*s*s*ax+3*s*s*t*c1x+3*s*t*t*c2x+t*t*t*bx;
                float y=s*s*s*ay+3*s*s*t*c1y+3*s*t*t*c2y+t*t*t*by;
                float fx=3*s*s*(c1x-ax)+6*s*t*(c2x-c1x)+3*t*t*(bx-c2x);
                float fy=3*s*s*(c1y-ay)+6*s*t*(c2y-c1y)+3*t*t*(by-c2y);
                Normalize(ref fx,ref fy);
                float radius=FlatSurfaceSampling.Cubic(beforeRadius,radii[i],endRadius,afterRadius,t);
                float smooth=t*t*(3-2*t),depth=FMath.Lerp(depths[i],endDepth,smooth);
                if(outlet||!continuing)
                {
                    // Only a drainage edge that actually joins a basin opens its banks.
                    float shore=FlatSurfaceSampling.SampleShore(world,facts,x,y);
                    float approach=outlet?1-smooth:smooth;
                    radius=MouthRadius(radius,shore,approach);
                    if(outlet)depth=OutletDepth(endDepth,depths[i],shore,facts.ShoreDistance[i],approach);
                }
                if(k>0&&k<SamplesPerReach)
                {
                    float head=FMath.Lerp(world.WaterLevel[i],world.WaterLevel[next],t);
                    float tolerance=Tolerance(FMath.Lerp(facts.Relief[i],facts.Relief[next],t));
                    BankNormal(fx,fy,Metric(y,world.Height),out float nx,out float ny);
                    Constrain(world,FMath.Lerp(baseAX,baseBX,t),FMath.Lerp(baseAY,baseBY,t),
                        head,tolerance,radius,nx,ny,ref x,ref y);
                    radius=FitRadius(world,x,y,head,tolerance,nx,ny,radius);
                }
                var vertex=new Vertex(x,y,radius,depth,fx,fy,
                    FMath.Lerp(facts.Sediment[i],endSediment,smooth));
                if(k>0)
                {
                    bool useReceiver=continuing&&(FlatTerrainAnalysis.AreaWater(world.Water[i])||main&&t>=.5f);
                    built.Add(new Segment(previousVertex,vertex,useReceiver?next:i));
                }
                previousVertex=vertex;
            }
        }

        static float MouthRadius(float radius,float shore,float approach)
        {
            float t=FMath.Clamp((shore+.24f)/.34f,0,1);
            return Math.Min(.22f,radius+Math.Min(.10f,.032f+.82f*radius)*approach*t*t*(3-2*t));
        }

        static float OutletDepth(float channel,float basin,float shore,float basinShore,float approach)
        {
            // A spill sill takes the depth of the outgoing channel. Keep the measured
            // lake floor at the wet anchor, and confine its slope to the submerged side.
            float t=FMath.Clamp((shore/Math.Max(.001f,basinShore)-.25f)/.75f,0,1);
            return FMath.Lerp(channel,basin,Math.Min(approach,t*t*(3-2*t)));
        }

        static void AddPolar(WorldData world,FlatTerrainFacts facts,int i,int next,bool[] river,float[] radii,float[] depths,
            float[] nodeX,float[] nodeY,float[] tangentX,float[] tangentY,List<Segment> built)
        {
            float boundary=i/world.Width==0?0:world.Height,flow=boundary==0?-1:1;
            float endRadius=river[next]?radii[next]:radii[i];
            float radius=(radii[i]+endRadius)*.5f;
            bool outlet=!river[i];
            float seamDepth=outlet?depths[next]:depths[i];
            var a=new Vertex(nodeX[i],nodeY[i],radii[i],depths[i],0,flow,facts.Sediment[i]);
            // Both meridians meet at the same pole. Fix the two chart endpoints
            // at their drainage meridians while the interior channel can meander.
            var edgeA=new Vertex(i%world.Width+.5f,boundary,radius,seamDepth,0,flow,facts.Sediment[i]);
            float targetX=river[next]?nodeX[next]:next%world.Width+.5f;
            float targetY=river[next]?nodeY[next]:next/world.Width+.5f;
            var edgeB=new Vertex(next%world.Width+.5f,boundary,radius,seamDepth,0,-flow,facts.Sediment[i]);
            var b=new Vertex(targetX,targetY,endRadius,river[next]?depths[next]:depths[i],
                river[next]?tangentX[next]:0,river[next]?tangentY[next]:-flow,river[next]?facts.Sediment[next]:facts.Sediment[i]);
            built.Add(new Segment(a,edgeA,river[i]?i:next));built.Add(new Segment(edgeB,b,river[next]?next:i));
        }

        static void Bounds(Segment segment,int height,out int minX,out int maxX,out int minY,out int maxY)
        {
            float padding=Math.Max(segment.A.Radius,segment.B.Radius)+FieldMargin;
            float latitude=Math.Max(Math.Abs(segment.A.Y-height*.5f),Math.Abs(segment.B.Y-height*.5f));
            float paddingX=padding/Metric(Math.Min(height,height*.5f+latitude+padding),height);
            minX=(int)Math.Floor(Math.Min(segment.A.X,segment.B.X)-paddingX);
            maxX=(int)Math.Floor(Math.Max(segment.A.X,segment.B.X)+paddingX);
            minY=FMath.Clamp((int)Math.Floor(Math.Min(segment.A.Y,segment.B.Y)-padding),0,height-1);
            maxY=FMath.Clamp((int)Math.Floor(Math.Max(segment.A.Y,segment.B.Y)+padding),0,height-1);
        }

        static FlatRiverCurves Index(int width,int height,Segment[] segments,CancellationToken cancel,
            bool surveyed,bool[] edited)
        {
            var offsets=new int[width*height+1];
            for(int k=0;k<segments.Length;k++)
            {
                if((k&4095)==0)cancel.ThrowIfCancellationRequested();
                Bounds(segments[k],height,out int minX,out int maxX,out int minY,out int maxY);
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                {
                    int tile=y*width+FMath.Wrap(x,width);
                    if(edited==null||edited[tile]==(segments[k].Reach>=0))offsets[tile+1]++;
                }
            }
            int activeCount=0;
            for(int i=0;i<width*height;i++)
            {
                if(offsets[i+1]>0)activeCount++;
                offsets[i+1]+=offsets[i];
            }
            var candidates=new int[offsets[offsets.Length-1]];
            var cursors=(int[])offsets.Clone();var active=new int[activeCount];int nextActive=0;
            for(int i=0;i<width*height;i++)if(offsets[i+1]>offsets[i])active[nextActive++]=i;
            for(int k=0;k<segments.Length;k++)
            {
                if((k&4095)==0)cancel.ThrowIfCancellationRequested();
                Bounds(segments[k],height,out int minX,out int maxX,out int minY,out int maxY);
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                {
                    int tile=y*width+FMath.Wrap(x,width);
                    if(edited==null||edited[tile]==(segments[k].Reach>=0))candidates[cursors[tile]++]=k;
                }
            }
            cancel.ThrowIfCancellationRequested();
            return new FlatRiverCurves(width,height,segments,offsets,candidates,active,surveyed,edited);
        }
    }
}
