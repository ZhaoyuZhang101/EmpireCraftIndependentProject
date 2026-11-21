using System;
using System.Threading;
using System.Threading.Tasks;
namespace Orbis.Flat
{
    // Earth-calibrated monthly normals for any bed and coastline. Calibration
    // and held-out comparisons are in Validation/EarthClimate.
    public static class FlatClimate
    {
        const double CircumferenceKm = 40030.229;
        static readonly float[] Summer = Harmonic(false), Lag = Harmonic(true);

        static float[] Harmonic(bool quadrature)
        {
            var months = new float[12];
            for (int m = 0; m < months.Length; m++)
            {
                double phase = (m - 6) * Math.PI / 6;
                months[m] = (float)(quadrature ? Math.Sin(phase) : Math.Cos(phase));
            }
            return months;
        }

        internal static int SurfaceHeight(WorldData w,int i)
        {return FlatTerrainAnalysis.AreaWater(w.Water[i]) ? w.WaterLevel[i] : w.Elevation[i];}

        // An exponentially weighted upwind ocean fraction. Solve the periodic
        // boundary exactly so there is no arbitrary initial coast at the seam.
        static void OceanFetch(WorldData world, int row, double memory, int direction, float[] output)
        {
            int width = world.Width, start = direction > 0 ? 0 : width - 1;
            double value = 0, incoming = 1 - memory;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 0, x = start; k < width; k++, x += direction)
                {
                    if (pass == 1) output[x] = (float)value;
                    value = memory * value + (FlatTerrainAnalysis.Ocean(world.Water[row + x]) ? incoming : 0);
                }
                if (pass == 0) value /= 1 - Math.Pow(memory, width);
            }
        }
        public static void Build(WorldData w,CancellationToken cancel,int workers=0)
        {
            int limit=FlatWorkerPolicy.Resolve(workers);
            var options=new ParallelOptions{MaxDegreeOfParallelism=limit,CancellationToken=cancel};
            Parallel.For(0,w.Height,options,y=>
            {
                double latitude=((y+.5)/w.Height-.5)*Math.PI;
                float sine=(float)Math.Sin(latitude), s=Math.Abs(sine), s2=s*s;
                double degrees=Math.Abs(latitude)*180/Math.PI;
                float westerlies=(float)((.5+.5*Math.Tanh((degrees-32)/8))*(.5-.5*Math.Tanh((degrees-68)/8)));
                float subtropics=(float)Math.Exp(-Math.Pow((degrees-24)/13,2));
                double memory=Math.Exp(-CircumferenceKm*Math.Cos(latitude)/(1800*w.Width));
                var west=new float[w.Width];var east=new float[w.Width];
                OceanFetch(w,y*w.Width,memory,1,west);
                OceanFetch(w,y*w.Width,memory,-1,east);
                for(int x=0;x<w.Width;x++)
                {
                    int i=y*w.Width+x,off=i*12;
                    float marine=FMath.Lerp(east[x],west[x],westerlies), inland=1-marine, contrast=west[x]-east[x];
                    float distance=Math.Min(1,w.CoastDistance[i]/(.05f*w.Width));
                    // Area-weighted fit to monthly land normals, using elevation in metres.
                    float annual=28.258f-22.939f*s2-23.472f*s2*s2-.004577f*SurfaceHeight(w,i)
                        +5.409f*s2*(marine-.5f)+4.867f*subtropics*(inland-.5f)+6.078f*s*contrast;
                    float amplitude=sine*(8.566f+3.489f*distance+5.829f*inland+11.226f*s*inland-2.701f*contrast);
                    float lag=sine*(1.579f-2.569f*distance-3.087f*inland+4.774f*s*inland+.111f*contrast);
                    for(int m=0;m<12;m++)
                        w.Temperature[off+m]=FMath.Clamp(annual+amplitude*Summer[m]+lag*Lag[m],-85,55);
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

