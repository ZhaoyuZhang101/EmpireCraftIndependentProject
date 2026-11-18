using System;
using System.Threading;
using System.Threading.Tasks;
namespace Orbis.Flat
{
    // Synthetic seasonal climatology for gameplay. NOT a numerical weather forecast/GCM.
    public static class FlatClimate
    {
        internal static int SurfaceHeight(WorldData w,int i)
        {return Math.Max(Math.Max(0,(int)w.Elevation[i]),FlatTerrainAnalysis.AreaWater(w.Water[i])?w.WaterLevel[i]:0);}
        public static void Build(WorldData w,CancellationToken cancel,int workers=0)
        {
            int limit=FlatWorkerPolicy.Resolve(workers);
            var options=new ParallelOptions{MaxDegreeOfParallelism=limit,CancellationToken=cancel};
            Parallel.For(0,w.Height,options,y=>
            {
                float lat=((y+.5f)/w.Height-.5f)*180,ab=Math.Abs(lat),r=lat*(float)Math.PI/180;
                for(int x=0;x<w.Width;x++)
                {
                    int i=y*w.Width+x,off=i*12;
                    float inland=FlatMoisture.Continentality(w.CoastDistance[i],w.Width);
                    float oceanShift=(float)Math.Sin((x+.5)/w.Width*Math.PI*2+r)*Math.Min(1,Math.Abs(lat)/50)*2.8f;
                    float annual=28-48*(float)Math.Pow(ab/90,1.65)-SurfaceHeight(w,i)*.0065f+oceanShift*(1-inland);
                    float amplitude=(3+20*inland)*(float)Math.Pow(Math.Sin(Math.Abs(r)),.7);
                    for(int m=0;m<12;m++)
                    {
                        float season=(float)Math.Cos((m-6)*Math.PI*2/12)*(lat>=0?1:-1);
                        w.Temperature[off+m]=FMath.Clamp(annual+amplitude*season,-85,55);
                    }
                }
            });
            w.Moisture=FlatMoisture.Build(w,cancel,workers);
            Parallel.For(0,w.Height,options,y=>
            {
                for(int x=0;x<w.Width;x++)
                {
                    int i=y*w.Width+x;
                    for(int m=0;m<12;m++)w.Precipitation[i*12+m]=w.Moisture.Sample(w.Moisture.Rain,(x+.5f)/w.Width,(y+.5f)/w.Height,m);
                    w.Climates[i]=KoppenClassifier.Classify(w.Temperature,w.Precipitation,i*12);
                }
            });
        }
    }
}

