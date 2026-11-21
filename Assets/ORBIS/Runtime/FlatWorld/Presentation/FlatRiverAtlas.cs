using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Orbis.Flat
{
    /// <summary>Streams an immutable river graph into one sparse, revision-matched GPU field.</summary>
    internal sealed class FlatRiverAtlas : IDisposable
    {
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
                        if(overview)UnityEngine.Object.Destroy(overview);
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
            publishedCurves=requestedCurves;publishedRevision=operation.Revision;publishedPreview=operation.Preview;
            operation.DisposeCompleted();operation=null;
            SetReady(!publishedPreview,publishedPreview);Bind(material);
            if(previous!=null)UnityEngine.Object.Destroy(previous);
        }

        bool PublishedMatchesRequest()
        {return Texture!=null&&publishedRevision==requestedRevision&&ReferenceEquals(publishedCurves,requestedCurves);}

        void SetReady(bool ready,bool preview)
        {
            if(Ready==ready&&previewReady==preview)return;
            Ready=ready;previewReady=preview;
            material.SetFloat("_RiverAtlasReady",ready?1:0);
            material.SetFloat("_RiverPreviewReady",preview?1:0);
        }

        void PumpUploads()
        {
            long started=System.Diagnostics.Stopwatch.GetTimestamp();int uploaded=0;
            UploadBlock block;
            while(operation.TryTake(out block))
            {
                if(staging==null)
                {
                    staging=new Texture2D(block.Plan.Width,block.Plan.Height,format,false,true)
                    {
                        name="Continuous global river distance / depth / tangent width",
                        filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Clamp,
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
            if(staging!=null)UnityEngine.Object.Destroy(staging);
            staging=null;stagingApplied=false;
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;SetReady(false,false);ReleaseStaging();
            if(Texture!=null)UnityEngine.Object.Destroy(Texture);
            if(overview)UnityEngine.Object.Destroy(overview);
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

        sealed class BuildPlan
        {
            public readonly int Width,Height,Pitch,HeaderRows,Span,Intervals,Columns;
            public readonly Vector4 Layout;
            readonly int[] slotsByTile,origins;
            readonly int worldWidth;

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
                if(row<HeaderRows)
                {
                    for(int x=0;x<Width;x++)
                    {
                        if((x&127)==0)cancel.ThrowIfCancellationRequested();
                        int tile=row*Width+x;
                        if(tile>=slotsByTile.Length)break;
                        int code=slotsByTile[tile];
                        if(code==0)continue;
                        FlatRiverSample sample=curves.Sample(tile%worldWidth+.5f,tile/worldWidth+.5f);
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
                    FlatRiverSample sample=curves.Sample(origin%worldWidth+(x%Pitch-1)*step,
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
