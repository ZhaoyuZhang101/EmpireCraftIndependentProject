using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Orbis.Flat
{
    /// <summary>Streams an immutable river graph into one sparse, revision-matched GPU field.</summary>
    internal sealed class FlatRiverAtlas : IDisposable
    {
        internal const string DiagnosticKeyword="ORBIS_DIAGNOSTIC";
        const int MaximumSlots=65534;
        const int MaximumTexels=20*1024*1024;
        const int DetailIntervals=32;
        const int PreviewIntervals=8;
        const int UploadRows=8;
        const int UploadPixelsPerTick=131072;
        const double UploadMillisecondsPerTick=2;
        readonly Material material;
        readonly TextureFormat format;
        readonly int maximumTextureSize;
        BuildOperation operation;
        Texture2D staging;
        Texture2D overview;
        FlatRiverCurves overviewCurves;
        int overviewRevision=-1;
        bool overviewVisible,surveyVisible;
        FlatRiverCurves requestedCurves,publishedCurves;
        int requestedRevision=-1,publishedRevision=-1;
        bool stagingApplied,publishedPreview,exhausted,previewReady,disposed;

        public Texture2D Texture {get;private set;}
        public Vector4 Layout {get;private set;}
        public bool Ready {get;private set;}
        public bool Pending
        {
            get
            {
                if(disposed||requestedCurves==null)return false;
                if(operation!=null)return true;
                if(exhausted)return false;
                return !PublishedMatchesRequest()||!Ready;
            }
        }

        public FlatRiverAtlas(Material material)
        {
            this.material=material??throw new ArgumentNullException(nameof(material));
            format=SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf)?TextureFormat.RGBAHalf:TextureFormat.RGBAFloat;
            if(!SystemInfo.SupportsTextureFormat(format))throw new NotSupportedException("River fields require RGBAHalf or RGBAFloat textures.");
            maximumTextureSize=SystemInfo.maxTextureSize;
            Bind(material);
        }

        public void Bind(Material target)
        {
            target.SetFloat("_RiverDisplayScale",requestedCurves?.MapWidthScale??1f);
            if(target.HasProperty("_RiverOverview"))
            {
                target.SetTexture("_RiverOverview",overview?overview:Texture2D.blackTexture);
                target.SetFloat("_RiverOverviewReady",overviewVisible?1:0);
                target.SetFloat("_RiverSurvey",surveyVisible?1:0);
            }
            target.SetTexture("_RiverAtlas",Texture?Texture:Texture2D.blackTexture);
            target.SetVector("_RiverAtlasLayout",Layout);
            target.SetFloat("_RiverAtlasReady",Ready?1:0);
            target.SetFloat("_RiverPreviewReady",previewReady?1:0);
            BindMode(target);
        }

        internal void SetDiagnosticMode(Material target,bool active)
        {
            if(target.IsKeywordEnabled(DiagnosticKeyword)==active)return;
            if(active)target.EnableKeyword(DiagnosticKeyword);else target.DisableKeyword(DiagnosticKeyword);
            BindMode(target);
        }

        void BindMode(Material target)
        {
            // Separate the expensive temporary D8 reconstruction from the
            // resident distance and vector fields in production fragment shaders.
            bool published=Ready||previewReady;
            bool rivers=!target.IsKeywordEnabled(DiagnosticKeyword);
            bool vectors=rivers&&published&&Layout.z<0;
            bool raster=rivers&&!vectors&&(published||surveyVisible||requestedCurves!=null&&requestedCurves.HasSurvey);
            if(vectors)target.EnableKeyword("ORBIS_RIVER_VECTORS");else target.DisableKeyword("ORBIS_RIVER_VECTORS");
            if(raster)target.EnableKeyword("ORBIS_RIVER_RASTER");else target.DisableKeyword("ORBIS_RIVER_RASTER");
        }

        public void Tick(FlatRiverCurves curves,int terrainRevision,int uploadedTerrainRevision)
        {
            if(disposed)return;
            if(!ReferenceEquals(requestedCurves,curves)||requestedRevision!=terrainRevision)
            {
                requestedCurves=curves;requestedRevision=terrainRevision;exhausted=false;
                if(operation!=null)operation.Cancel();
                ReleaseStaging();
            }
            bool matches=PublishedMatchesRequest();
            bool showOverview=overview && overviewRevision==uploadedTerrainRevision &&
                (uploadedTerrainRevision!=terrainRevision || ReferenceEquals(overviewCurves,curves));
            bool showSurvey=uploadedTerrainRevision==terrainRevision?curves!=null&&curves.HasSurvey:surveyVisible;
            if(overviewVisible!=showOverview||surveyVisible!=showSurvey)
            {overviewVisible=showOverview;surveyVisible=showSurvey;Bind(material);}
            bool visible=curves!=null&&Texture!=null&&publishedRevision==uploadedTerrainRevision&&
                (uploadedTerrainRevision!=terrainRevision||matches);
            SetReady(visible&&(!publishedPreview||exhausted&&matches),visible&&publishedPreview);

            if(operation!=null&&operation.Cancelled)
            {
                if(!operation.Completed)return;
                operation.DisposeCompleted();operation=null;
            }
            if(operation==null)
            {
                if(curves==null||matches&&!publishedPreview||exhausted)return;
                operation=new BuildOperation(curves,terrainRevision,maximumTextureSize,preview:!matches);
            }
            if(uploadedTerrainRevision==operation.Revision && operation.Preview)
            {
                var ink=operation.TakeOverview();
                if(ink!=null && SystemInfo.SupportsTextureFormat(TextureFormat.R8))
                {
                    int width=curves.OverviewWidth;
                    if(!overview || overview.width!=width)
                    {
                        FlatMapRenderer.ReleaseTexture(overview);
                        overview=new Texture2D(width,width/2,TextureFormat.R8,true,true) {
                            name="River network overview",filterMode=FilterMode.Trilinear,
                            wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp
                        };
                    }
                    overview.SetPixelData(ink,0);overview.Apply(true,false);
                    overviewCurves=curves;overviewRevision=operation.Revision;overviewVisible=true;Bind(material);
                }
            }
            PumpUploads();
            if(operation==null||!operation.Completed)return;
            if(operation.Error!=null)
            {
                exhausted=true;
                Exception error=operation.Error;
                operation.DisposeCompleted();operation=null;ReleaseStaging();
                Debug.LogException(error);return;
            }
            if(operation.Plan==null)
            {
                // Keep the completed coarse field if no finer layout fits the device.
                if(operation.Preview && uploadedTerrainRevision!=operation.Revision)return;
                exhausted=true;
                if(matches&&visible)SetReady(true,false);
                operation.DisposeCompleted();operation=null;return;
            }
            if(!operation.UploadsEmpty)return;
            if(!stagingApplied)
            {
                staging.Apply(false,true);stagingApplied=true;
            }
            // Terrain jobs and this graph may finish in either order.
            if(uploadedTerrainRevision!=operation.Revision)return;
            Texture2D previous=Texture;Texture=staging;staging=null;stagingApplied=false;
            Layout=operation.Plan.Layout;
            publishedCurves=requestedCurves;publishedRevision=operation.Revision;
            publishedPreview=operation.Preview&&!operation.Plan.IsGeometry;
            operation.DisposeCompleted();operation=null;
            SetReady(!publishedPreview,publishedPreview);Bind(material);
            FlatMapRenderer.ReleaseTexture(previous);
        }

        bool PublishedMatchesRequest()
        {return Texture!=null&&publishedRevision==requestedRevision&&ReferenceEquals(publishedCurves,requestedCurves);}

        void SetReady(bool ready,bool preview)
        {
            if(Ready==ready&&previewReady==preview)return;
            Ready=ready;previewReady=preview;
            material.SetFloat("_RiverAtlasReady",ready?1:0);
            material.SetFloat("_RiverPreviewReady",preview?1:0);
            BindMode(material);
        }

        void PumpUploads()
        {
            long started=System.Diagnostics.Stopwatch.GetTimestamp();int uploaded=0;
            UploadBlock block;
            while(operation.TryTake(out block))
            {
                if(staging==null)
                {
                    var storage=block.Plan.IsGeometry&&SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat)?TextureFormat.RGBAFloat:format;
                    staging=new Texture2D(block.Plan.Width,block.Plan.Height,storage,false,true)
                    {
                        name="Continuous global river distance / depth / tangent width",
                        filterMode=block.Plan.IsGeometry?FilterMode.Point:FilterMode.Bilinear,wrapModeU=TextureWrapMode.Clamp,
                        wrapModeV=TextureWrapMode.Clamp,anisoLevel=0
                    };
                }
                staging.SetPixels(0,block.Row,block.Plan.Width,block.Rows,block.Pixels);
                uploaded+=block.Pixels.Length;
                double elapsed=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                if(uploaded>=UploadPixelsPerTick||elapsed>=UploadMillisecondsPerTick)break;
            }
        }

        void ReleaseStaging()
        {
            FlatMapRenderer.ReleaseTexture(staging);
            staging=null;stagingApplied=false;
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;SetReady(false,false);ReleaseStaging();
            FlatMapRenderer.ReleaseTexture(Texture);
            FlatMapRenderer.ReleaseTexture(overview);
            Texture=null;
            if(operation!=null){operation.CancelAndDisposeWhenComplete();operation=null;}
        }

        sealed class UploadBlock
        {
            public readonly BuildPlan Plan;
            public readonly int Row,Rows;
            public readonly Color[] Pixels;
            public UploadBlock(BuildPlan plan,int row,int rows,Color[] pixels)
            {Plan=plan;Row=row;Rows=rows;Pixels=pixels;}
        }

        sealed class BuildOperation
        {
            readonly CancellationTokenSource cancellation=new CancellationTokenSource();
            readonly BlockingCollection<UploadBlock> uploads=new BlockingCollection<UploadBlock>(3);
            readonly Task worker;
            byte[] overviewInk;
            internal byte[] TakeOverview()=>Interlocked.Exchange(ref overviewInk,null);
            public readonly int Revision;
            public readonly bool Preview;
            public BuildPlan Plan {get;private set;}
            public bool Cancelled {get{return cancellation.IsCancellationRequested;}}
            public bool Completed {get{return worker.IsCompleted;}}
            public bool UploadsEmpty {get{return uploads.Count==0;}}
            public Exception Error {get{return worker.IsFaulted?worker.Exception.GetBaseException():null;}}

            public BuildOperation(FlatRiverCurves curves,int revision,int maximumTextureSize,bool preview)
            {
                Revision=revision;Preview=preview;
                // Workers capture only the immutable graph and managed output buffers.
                worker=Task.Run(()=>Bake(curves,maximumTextureSize,cancellation.Token));
            }

            void Bake(FlatRiverCurves curves,int maximumTextureSize,CancellationToken cancel)
            {
                try
                {
                    if(Preview)Interlocked.Exchange(ref overviewInk,curves.DrawOverview(cancel));
                    if(curves.HasSurvey)Plan=BuildPlan.CreateGeometry(curves,maximumTextureSize,cancel);
                    int minimum=Preview?2:PreviewIntervals*2;
                    for(int intervals=Preview?PreviewIntervals:DetailIntervals;intervals>=minimum&&Plan==null;intervals/=2)
                        Plan=BuildPlan.Create(curves,maximumTextureSize,intervals,cancel);
                    if(Plan==null)return;
                    var options=new ParallelOptions{CancellationToken=cancel,
                        MaxDegreeOfParallelism=Math.Max(1,Math.Min(4,Environment.ProcessorCount-2))};
                    for(int row=0;row<Plan.Height;row+=UploadRows)
                    {
                        cancel.ThrowIfCancellationRequested();
                        int firstRow=row,rows=Math.Min(UploadRows,Plan.Height-row);
                        var pixels=new Color[Plan.Width*rows];
                        Parallel.For(0,rows,options,localRow=>Plan.SampleRow(curves,firstRow+localRow,pixels,localRow*Plan.Width,cancel));
                        uploads.Add(new UploadBlock(Plan,row,rows,pixels),cancel);
                    }
                }
                finally {uploads.CompleteAdding();}
            }

            public bool TryTake(out UploadBlock block) {return uploads.TryTake(out block);}
            public void Cancel() {cancellation.Cancel();}
            public void DisposeCompleted()
            {
                // Observing faults also covers cancellation followed by a replacement request.
                if(worker.IsFaulted){var ignored=worker.Exception;}
                uploads.Dispose();cancellation.Dispose();
            }
            public void CancelAndDisposeWhenComplete()
            {
                Cancel();
                if(Completed){DisposeCompleted();return;}
                worker.ContinueWith(_=>DisposeCompleted(),CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            }
        }

        // A filtered unsigned distance field can erase a narrow river between two
        // raster nodes. Keep the surveyed segments in tile coordinates instead.
        // The sparse directory also avoids allocating an RGBA header for every
        // cell of a 4096-wide Earth. Addresses remain exact on half-float devices.
        sealed class GeometryPages
        {
            public const int GroupSize=4;
            const int AddressBase=2048;
            public readonly int[] Groups;
            public readonly List<Color> Body=new List<Color>();

            GeometryPages(int groups) { Groups=new int[groups]; }
            public static Color Header(int code,int count,float sediment)
            {return new Color(code%AddressBase,code/AddressBase,sediment,count);}

            readonly struct Candidate
            {
                public readonly Color Ends,Section,Flow;
                public Candidate(FlatRiverCurves.Segment segment,Vector2 bend,int x,int y,int worldWidth,float mapWidthScale)
                {
                    var a=segment.A;var b=segment.B;
                    float ax=a.X-x;ax-=(float)Math.Round(ax/worldWidth)*worldWidth;
                    Ends=new Color(ax,a.Y-y,ax+(b.X-a.X),b.Y-y);
                    Section=new Color(a.Radius*mapWidthScale,b.Radius*mapWidthScale,a.Depth,b.Depth);
                    // Magnitude carries the signed turn at the station; the GPU
                    // decodes and interpolates the original unit tangents.
                    Flow=new Color(a.FlowX*(2+bend.x),a.FlowY*(2+bend.x),b.FlowX*(2+bend.y),b.FlowY*(2+bend.y));
                }

                float Distance(float x,float y,float metric)
                {
                    float dx=(Ends.b-Ends.r)*metric,dy=Ends.a-Ends.g;
                    float px=(x-Ends.r)*metric,py=y-Ends.g;
                    float t=Math.Max(0,Math.Min(1,(px*dx+py*dy)/Math.Max(1e-12f,dx*dx+dy*dy)));
                    px-=t*dx;py-=t*dy;
                    return (float)Math.Sqrt(px*px+py*py);
                }

                public void Bounds(float x,float y,float minMetric,float maxMetric,out float lower,out float upper)
                {
                    lower=Distance(x,y,minMetric)-Math.Max(Section.r,Section.g);
                    upper=Distance(x,y,maxMetric)-Math.Min(Section.r,Section.g);
                }
            }

            static float Metric(float row,int height)
            {return Math.Max(.08f,(float)Math.Cos((row/height-.5)*Math.PI));}

            // Measure bends along each connected centreline, not by sampling
            // the nearest bank at offsets in screen space. Inner-bank pixels
            // on the two adjoining segments share a station and its deposit.
            static Vector2[] StationBends(FlatRiverCurves curves,CancellationToken cancel)
            {
                var bends=new Vector2[curves.SegmentCount];
                var distances=new float[32];
                for(int first=0;first<bends.Length;)
                {
                    cancel.ThrowIfCancellationRequested();
                    int last=first;
                    while(last+1<bends.Length)
                    {
                        var a=curves.SegmentAt(last);var b=curves.SegmentAt(last+1);
                        float dx=a.B.X-b.A.X;dx-=(float)Math.Round(dx/curves.Width)*curves.Width;
                        if(a.Reach!=b.Reach||Math.Abs(dx)>.001f||Math.Abs(a.B.Y-b.A.Y)>.001f||
                            a.B.FlowX*b.A.FlowX+a.B.FlowY*b.A.FlowY<.995f)break;
                        last++;
                    }
                    int count=last-first+1;
                    if(distances.Length<=count)Array.Resize(ref distances,count+1);
                    distances[0]=0;
                    for(int j=0;j<count;j++)
                    {
                        var s=curves.SegmentAt(first+j);
                        float dx=(s.B.X-s.A.X)*Metric((s.A.Y+s.B.Y)*.5f,curves.Height),dy=s.B.Y-s.A.Y;
                        distances[j+1]=distances[j]+Math.Max(.000001f,(float)Math.Sqrt(dx*dx+dy*dy));
                    }
                    Vector2 Tangent(float at,float metric)
                    {
                        int j=Array.BinarySearch(distances,0,count+1,at);
                        j=j<0?Math.Max(0,~j-1):Math.Min(count-1,j);
                        var s=curves.SegmentAt(first+j);
                        float t=(at-distances[j])/Math.Max(.000001f,distances[j+1]-distances[j]);
                        return new Vector2((s.A.FlowX+t*(s.B.FlowX-s.A.FlowX))*metric,s.A.FlowY+t*(s.B.FlowY-s.A.FlowY));
                    }
                    float previous=0;
                    for(int j=0;j<=count;j++)
                    {
                        var v=j<count?curves.SegmentAt(first+j).A:curves.SegmentAt(last).B;
                        // Average over several measured half-widths; 1.6 km
                        // is the smallest support for generalized Earth lines.
                        float support=Math.Max(curves.Width*.00004f,v.Radius*3.5f);
                        float a=Math.Max(0,distances[j]-support),b=Math.Min(distances[count],distances[j]+support);
                        float metric=Metric(v.Y,curves.Height);
                        var inFlow=Tangent(a,metric);var outFlow=Tangent(b,metric);
                        float turn=(float)Math.Atan2(inFlow.x*outFlow.y-inFlow.y*outFlow.x,
                            inFlow.x*outFlow.x+inFlow.y*outFlow.y);
                        float curvature=turn*v.Radius*3.5f/Math.Max(support,b-a);
                        float bend=curvature/(1+Math.Abs(curvature));
                        if(j>0)bends[first+j-1]=new Vector2(previous,bend);
                        previous=bend;
                    }
                    first=last+1;
                }
                return bends;
            }

            int Reserve(int count)
            {
                int start=Body.Count;
                for(int i=0;i<count;i++)Body.Add(default);
                return start;
            }

            public static GeometryPages Create(FlatRiverCurves curves,int maximumTextureSize,CancellationToken cancel)
            {
                int width=curves.Width,height=curves.Height,groupColumns=(width+GroupSize-1)/GroupSize;
                int groups=groupColumns*((height+GroupSize-1)/GroupSize);
                int packingWidth=Math.Min(maximumTextureSize,4096);
                if(packingWidth<1)return null;
                int headerRows=(groups+packingWidth-1)/packingWidth;
                long budget=((long)Math.Min(maximumTextureSize,MaximumTexels/packingWidth)-headerRows)*packingWidth;
                budget=Math.Min(budget,AddressBase*AddressBase-1);
                if(budget<0)return null;
                var pages=new GeometryPages(groups);
                var bends=StationBends(curves,cancel);
                // Subdivision only limits the shader's candidate loop. On a
                // smaller device keep exact banks with a coarser lookup first.
                for(int ceiling=16;ceiling>=1;ceiling/=2)
                {
                    if(ceiling!=16){pages.Body.Clear();Array.Clear(pages.Groups,0,pages.Groups.Length);}
                    if(pages.TryPack(curves,bends,groupColumns,budget,ceiling,cancel))return pages;
                }
                return null;
            }

            bool TryPack(FlatRiverCurves curves,Vector2[] bends,int groupColumns,long budget,int ceiling,CancellationToken cancel)
            {
                int width=curves.Width,height=curves.Height;
                // On coarse worlds the same river surveys are concentrated in
                // fewer tiles. Save leaf space for densely braided channels.
                int candidatesPerDivision=curves.SegmentCount>(long)width*height/2?8:5;
                var candidates=new Candidate[16];var lower=new float[16];
                for(int i=0;i<curves.ActiveTileCount;i++)
                {
                    if((i&127)==0)cancel.ThrowIfCancellationRequested();
                    int tile=curves.GetActiveTile(i),x=tile%width,y=tile/width;
                    int entries=curves.TileSegmentCount(tile),count=0;
                    if(entries>candidates.Length){Array.Resize(ref candidates,entries);Array.Resize(ref lower,entries);}
                    for(int j=0;j<entries;j++)
                    {
                        int index=curves.TileSegmentIndex(tile,j);
                        var segment=curves.SegmentAt(index);
                        if(segment.ShoreDefinesBanks)continue;
                        candidates[count++]=new Candidate(segment,bends[index],x,y,width,curves.MapWidthScale);
                    }
                    if(count==0)continue;
                    int group=y/GroupSize*groupColumns+x/GroupSize;
                    if(Groups[group]==0)Groups[group]=Reserve(GroupSize*GroupSize)+1;
                    int entry=Groups[group]-1+y%GroupSize*GroupSize+x%GroupSize;
                    int divisions=1;
                    if(count>12)
                        while(divisions<ceiling&&divisions*candidatesPerDivision<count)divisions*=2;
                    float sediment=curves.SampleMap(x+.5f,y+.5f).Sediment;
                    int cells=divisions*divisions,first=divisions==1?entry:Reserve(cells);
                    if(divisions>1)Body[entry]=Header(first+1,-divisions,sediment);
                    // Geometry is shared by the entire tile. A subdivided leaf
                    // stores four local segment indices per texel, so a dense
                    // river delta can be subdivided without repeating endpoints.
                    int records=Body.Count;
                    for(int j=0;j<count;j++)
                    {
                        Body.Add(candidates[j].Ends);Body.Add(candidates[j].Section);Body.Add(candidates[j].Flow);
                    }
                    if(Body.Count>budget||count>=AddressBase)return false;
                    if(divisions==1)
                    {
                        Body[entry]=Header(records+1,count,sediment);
                        continue;
                    }
                    float a=Metric(y,height),b=Metric(y+1,height),minMetric=Math.Min(a,b),maxMetric=Math.Max(a,b);
                    // A point in this subcell is at most half its diagonal from
                    // its centre. Lower/upper bounds include the full radius
                    // taper and latitude range; every possible winner survives.
                    float diagonal=(float)Math.Sqrt(1+maxMetric*maxMetric)/divisions+.00002f;
                    for(int k=0;k<cells;k++)
                    {
                        float best=float.MaxValue;
                        float cx=(k%divisions+.5f)/divisions,cy=(k/divisions+.5f)/divisions;
                        for(int j=0;j<count;j++)
                        {
                            candidates[j].Bounds(cx,cy,minMetric,maxMetric,out lower[j],out float upper);
                            best=Math.Min(best,upper);
                        }
                        int start=Body.Count,accepted=0;Color indices=default;
                        for(int j=0;j<count;j++)
                        {
                            if(lower[j]>best+diagonal)continue;
                            indices[accepted%4]=j;
                            accepted++;
                            if((accepted&3)==0)Body.Add(indices);
                        }
                        if((accepted&3)!=0)Body.Add(indices);
                        if(Body.Count>budget)return false;
                        Body[first+k]=Header(start+1,accepted,sediment);
                    }
                }
                return true;
            }
        }

        sealed class BuildPlan
        {
            public readonly int Width,Height,Pitch,HeaderRows,Span,Intervals,Columns;
            public readonly Vector4 Layout;
            readonly int[] slotsByTile,origins;
            readonly int worldWidth;
            readonly GeometryPages geometry;
            public bool IsGeometry => geometry!=null;

            BuildPlan(GeometryPages pages,int width,int rows,int headerRows)
            {
                geometry=pages;Width=width;Height=rows;HeaderRows=headerRows;
                Layout=new Vector4(width,rows,-GeometryPages.GroupSize,headerRows);
            }

            public static BuildPlan CreateGeometry(FlatRiverCurves curves,int maximumTextureSize,CancellationToken cancel)
            {
                var pages=GeometryPages.Create(curves,maximumTextureSize,cancel);
                if(pages==null)return null;
                int width=128,total=pages.Groups.Length+pages.Body.Count;
                while(width<maximumTextureSize && width<4096 && width*width<total)width*=2;
                width=Math.Min(width,maximumTextureSize);
                int header=(pages.Groups.Length+width-1)/width,rows=header+(pages.Body.Count+width-1)/width;
                if(rows>maximumTextureSize||(long)rows*width>MaximumTexels)return null;
                return new BuildPlan(pages,width,rows,header);
            }

            BuildPlan(int width,int height,int pitch,int headerRows,int span,int intervals,
                int[] slotsByTile,int[] origins,int worldWidth)
            {
                Width=width;Height=height;Pitch=pitch;HeaderRows=headerRows;Span=span;
                Intervals=intervals;Columns=width/pitch;this.slotsByTile=slotsByTile;
                this.origins=origins;this.worldWidth=worldWidth;
                Layout=new Vector4(width,height,pitch,headerRows);
            }

            public static BuildPlan Create(FlatRiverCurves curves,int maximumTextureSize,int intervalsPerTile,CancellationToken cancel)
            {
                int count=checked(curves.Width*curves.Height);
                // Adjacent active tiles may share header slots on dense networks.
                for(int span=1;span<=32;span*=2)
                {
                    cancel.ThrowIfCancellationRequested();
                    int groupWidth=(curves.Width+span-1)/span,groupHeight=(curves.Height+span-1)/span;
                    var groups=new int[groupWidth*groupHeight];int slots=0;
                    for(int j=0;j<curves.ActiveTileCount;j++)
                    {
                        if((j&2047)==0)cancel.ThrowIfCancellationRequested();
                        int tile=curves.GetActiveTile(j);
                        int group=tile/curves.Width/span*groupWidth+tile%curves.Width/span;
                        if(groups[group]==0)groups[group]=++slots;
                    }
                    if(slots>MaximumSlots)continue;
                    int width,height,headerRows,intervals=intervalsPerTile*span,pitch=intervals+3;
                    if(!TryLayout(count,slots,pitch,maximumTextureSize,out width,out height,out headerRows))continue;
                    var slotsByTile=new int[count];var origins=new int[slots];
                    for(int j=0;j<curves.ActiveTileCount;j++)
                    {
                        if((j&2047)==0)cancel.ThrowIfCancellationRequested();
                        int tile=curves.GetActiveTile(j),x=tile%curves.Width,y=tile/curves.Width;
                        int slot=groups[y/span*groupWidth+x/span];
                        slotsByTile[tile]=slot;origins[slot-1]=(y/span*span)*curves.Width+x/span*span;
                    }
                    return new BuildPlan(width,height,pitch,headerRows,span,intervals,slotsByTile,origins,curves.Width);
                }
                return null;
            }

            static bool TryLayout(int tiles,int slots,int pitch,int maximumTextureSize,out int width,out int height,out int headerRows)
            {
                width=height=headerRows=0;
                double target=Math.Min(4096,Math.Max(128,Math.Sqrt(tiles+(double)slots*pitch*pitch)));
                double bestScore=double.MaxValue;
                for(int candidate=1;candidate>0&&candidate<=maximumTextureSize;candidate*=2)
                {
                    if(candidate<pitch)continue;
                    int columns=candidate/pitch;
                    int header=((tiles+candidate-1)/candidate+pitch-1)/pitch*pitch;
                    int rows=header+(slots+columns-1)/columns*pitch;
                    if(rows>maximumTextureSize||(long)candidate*rows>MaximumTexels)continue;
                    double score=Math.Abs(Math.Log(candidate/target));
                    if(score>=bestScore)continue;
                    bestScore=score;width=candidate;height=rows;headerRows=header;
                }
                return width!=0;
            }

            public void SampleRow(FlatRiverCurves curves,int row,Color[] pixels,int offset,CancellationToken cancel)
            {
                if(geometry!=null)
                {
                    if(row<HeaderRows)
                    {
                        int start=row*Width,end=Math.Min(Width,geometry.Groups.Length-start);
                        for(int x=0;x<end;x++)pixels[offset+x]=GeometryPages.Header(geometry.Groups[start+x],0,0);
                    }
                    else
                    {
                        int start=(row-HeaderRows)*Width;
                        geometry.Body.CopyTo(start,pixels,offset,Math.Min(Width,geometry.Body.Count-start));
                    }
                    return;
                }
                if(row<HeaderRows)
                {
                    for(int x=0;x<Width;x++)
                    {
                        if((x&127)==0)cancel.ThrowIfCancellationRequested();
                        int tile=row*Width+x;
                        if(tile>=slotsByTile.Length)break;
                        int code=slotsByTile[tile];
                        if(code==0)continue;
                        FlatRiverSample sample=curves.SampleMap(tile%worldWidth+.5f,tile/worldWidth+.5f);
                        // A=0 is the normal one-tile contract. Dense fallbacks carry their block span.
                        pixels[offset+x]=new Color(code&255,code>>8,FiniteClamp(sample.Sediment,0,1,0),Span==1?0:Span);
                    }
                    return;
                }
                int bodyRow=row-HeaderRows,slotRow=bodyRow/Pitch,localY=bodyRow%Pitch;
                float step=(float)Span/Intervals;
                for(int x=0;x<Width;x++)
                {
                    if((x&63)==0)cancel.ThrowIfCancellationRequested();
                    int slot=slotRow*Columns+x/Pitch;
                    if(x>=Columns*Pitch||slot>=origins.Length)
                    {pixels[offset+x]=new Color(8,0,0,0);continue;}
                    int origin=origins[slot];
                    FlatRiverSample sample=curves.SampleMap(origin%worldWidth+(x%Pitch-1)*step,
                        origin/worldWidth+(localY-1)*step);
                    float halfWidth=FiniteClamp(sample.HalfWidth,0,8,0);
                    float flowX=sample.FlowX,flowY=sample.FlowY;
                    double length=Math.Sqrt((double)flowX*flowX+(double)flowY*flowY);
                    if(double.IsNaN(length)||double.IsInfinity(length)||length<1e-10)
                    {flowX=halfWidth;flowY=0;}
                    else {flowX=(float)(flowX/length*halfWidth);flowY=(float)(flowY/length*halfWidth);}
                    pixels[offset+x]=new Color(FiniteClamp(sample.Distance,-8,8,8),
                        FiniteClamp(sample.Depth,0,65504,0),flowX,flowY);
                }
            }

            static float FiniteClamp(float value,float minimum,float maximum,float fallback)
            {return float.IsNaN(value)||float.IsInfinity(value)?fallback:Math.Max(minimum,Math.Min(maximum,value));}
        }
    }
}
