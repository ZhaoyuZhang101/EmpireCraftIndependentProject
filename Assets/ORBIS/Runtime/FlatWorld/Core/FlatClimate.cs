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
        internal const float MarineAirCoupling = .65f;
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

        // Periodic, one-sided exponential average. Half a cell of the local
        // source lies upwind of the centre. The first circuit closes the seam.
        static void FilterLine(float[] source, float[] result, int direction, double memory)
        {
            int start = direction > 0 ? 0 : source.Length - 1;
            double value = 0, incoming = 1 - memory, half = Math.Sqrt(memory);
            for (int k = 0, j = start; k < source.Length; k++, j += direction)
                value = memory * value + incoming * source[j];
            value /= 1 - Math.Pow(memory, source.Length);
            for (int k = 0, j = start; k < source.Length; k++, j += direction)
            {
                result[j] = (float)(half * value + (1 - half) * source[j]);
                value = memory * value + incoming * source[j];
            }
        }

        // Integrate source regions before sampling a wind trajectory. A
        // meridian continues over the pole at the antipodal longitude. Both
        // exponential lengths are physical distances, independent of map size.
        static float[] Regional(float[] source, int width, int height, double lengthKm,
            ParallelOptions options, float[] zonalContrast = null)
        {
            var meridional = new float[source.Length];
            double memoryY = Math.Exp(-CircumferenceKm / (2 * height * .6 * lengthKm));
            Parallel.For(0, width / 2, options, x =>
            {
                int length = 2 * height;
                var ring = new float[length];
                var north = new float[length];
                var south = new float[length];
                for (int y = 0; y < height; y++)
                {
                    ring[y] = source[y * width + x];
                    ring[length - 1 - y] = source[y * width + x + width / 2];
                }
                FilterLine(ring, north, 1, memoryY);
                FilterLine(ring, south, -1, memoryY);
                for (int y = 0; y < height; y++)
                {
                    int other = length - 1 - y;
                    meridional[y * width + x] = .5f * (north[y] + south[y]);
                    meridional[y * width + x + width / 2] = .5f * (north[other] + south[other]);
                }
            });
            var average = new float[source.Length];
            Parallel.For(0, height, options, y =>
            {
                int row = y * width;
                double latitude = ((y + .5) / height - .5) * Math.PI;
                double memoryX = Math.Exp(-CircumferenceKm * Math.Cos(latitude) / (width * lengthKm));
                var line = new float[width];
                var west = new float[width];
                var east = new float[width];
                Array.Copy(meridional, row, line, 0, width);
                FilterLine(line, west, 1, memoryX);
                FilterLine(line, east, -1, memoryX);
                for (int x = 0; x < width; x++)
                {
                    average[row + x] = .5f * (west[x] + east[x]);
                    if (zonalContrast != null) zonalContrast[row + x] = west[x] - east[x];
                }
            });
            return average;
        }

        // Great-circle source coordinates have the same longitudinal offset for
        // an entire row. Precompute the bilinear indices once per wind path.
        readonly struct SourcePoint
        {
            readonly int offset, south, north;
            readonly float dx, dy;
            public SourcePoint(double lat, double bearing, double km, int w, int h)
            {
                double ang = km / 6371.0088, sl = Math.Sin(lat), cl = Math.Cos(lat);
                double sl2 = sl * Math.Cos(ang) + cl * Math.Sin(ang) * Math.Cos(bearing);
                double py = Math.Max(0, Math.Min(h - 1,
                    (Math.Asin(Math.Max(-1, Math.Min(1, sl2))) / Math.PI + .5) * h - .5));
                double px = Math.Atan2(Math.Sin(bearing) * Math.Sin(ang) * cl, Math.Cos(ang) - sl * sl2)
                    * w / (2 * Math.PI);
                int y = (int)Math.Floor(py), x = (int)Math.Floor(px);
                offset = FMath.Wrap(x, w); south = y * w; north = Math.Min(y + 1, h - 1) * w;
                dx = (float)(px - x); dy = (float)(py - y);
            }
            public float Read(float[] field, int x, int width)
            {
                int a = x + offset;
                if (a >= width) a -= width;
                int b = a + 1 == width ? 0 : a + 1;
                return FMath.Lerp(FMath.Lerp(field[south + a], field[south + b], dx),
                    FMath.Lerp(field[north + a], field[north + b], dx), dy);
            }
        }

        readonly struct AirPath
        {
            readonly SourcePoint near, middle, far;
            public AirPath(double lat, double bearing, int width, int height)
            {
                near = new SourcePoint(lat, bearing, 700, width, height);
                middle = new SourcePoint(lat, bearing, 1700, width, height);
                far = new SourcePoint(lat, bearing, 3100, width, height);
            }
            public float Read(float[] field, int x, int width) =>
                .25f * near.Read(field, x, width) + .4f * middle.Read(field, x, width) + .35f * far.Read(field, x, width);
        }

        // Daily-mean top-of-atmosphere shortwave flux in W/m2.
        static double SolarFlux(double latitude, int month)
        {
            double phase = (month - 6) * Math.PI / 6;
            double declination = Math.Asin(Math.Sin(23.44 * Math.PI / 180) * Math.Cos(phase + .42));
            double hour = Math.Acos(Math.Max(-1, Math.Min(1, -Math.Tan(latitude) * Math.Tan(declination))));
            return 1361 / Math.PI * (1 - .0334 * Math.Cos(phase + .20))
                * (hour * Math.Sin(latitude) * Math.Sin(declination)
                + Math.Cos(latitude) * Math.Cos(declination) * Math.Sin(hour));
        }

        // The annual mean and first harmonic are already in the background.
        // Keep the remaining equinoctial peaks and polar-day/night shape of
        // daily-mean top-of-atmosphere insolation, including orbital eccentricity.
        static float[] SolarResidual(double latitude)
        {
            var flux = new double[12];
            double mean = 0, cosine = 0, sine = 0;
            for (int m = 0; m < 12; m++)
            {
                flux[m] = SolarFlux(latitude, m);
                mean += flux[m] / 12;
                cosine += flux[m] * Summer[m] / 6;
                sine += flux[m] * Lag[m] / 6;
            }
            var residual = new float[12];
            for (int m = 0; m < 12; m++)
                residual[m] = (float)((flux[m] - mean - cosine * Summer[m] - sine * Lag[m]) / 55);
            return residual;
        }

        internal static void BuildTemperature(WorldData w, ParallelOptions options) =>
            BuildTemperature(w, options, FlatOceanCirculation.Build(w, options));

        static void BuildTemperature(WorldData w, ParallelOptions options, FlatOceanCirculation currents)
        {
            int width = w.Width, height = w.Height;
            var source = new float[w.Count];
            for (int i = 0; i < source.Length; i++)
                source[i] = FlatTerrainAnalysis.Ocean(w.Water[i]) ? 1 : 0;
            var nearbySea = Regional(source, width, height, 500, options);
            var coastContrast = new float[w.Count];
            var regionalSea = Regional(source, width, height, 1750, options, coastContrast);
            for (int i = 0; i < source.Length; i++)
                source[i] = FlatTerrainAnalysis.AreaWater(w.Water[i]) ? 0 : Math.Max(0f, w.Elevation[i]) * .001f;
            var relief = Regional(source, width, height, 650, options);
            Parallel.For(0,w.Height,options,y=>
            {
                double latitude=((y+.5)/w.Height-.5)*Math.PI;
                float sine=(float)Math.Sin(latitude), s=Math.Abs(sine), s2=s*s;
                double degrees=Math.Abs(latitude)*180/Math.PI;
                float westerlies=(float)((.5+.5*Math.Tanh((degrees-32)/8))*(.5-.5*Math.Tanh((degrees-68)/8)));
                float subtropics=(float)Math.Exp(-Math.Pow((degrees-24)/13,2));
                float equatorial = (float)Math.Exp(-Math.Pow(degrees / 11, 2));
                float tropical = (float)Math.Exp(-Math.Pow((degrees - 16) / 14, 2));
                float premonsoonBelt = (float)Math.Exp(-Math.Pow((degrees-17)/15,2));
                double coldLatitude = Math.Sqrt(s);
                float hemisphere = (float)Math.Tanh(6 * sine);
                float seaLevel = (float)(27 - 46 * Math.Pow(s, 2.8));
                float latitudeDegrees = (float)(latitude * 180 / Math.PI);
                var solar = SolarResidual(latitude);
                double stormBearing = -125 * Math.PI / 180;
                var storm = new AirPath(latitude, Math.Atan2(Math.Sin(stormBearing), Math.Cos(stormBearing) * hemisphere), width, height);
                double coldBearing = -Math.PI / 6;
                var coldWind = new AirPath(latitude, Math.Atan2(Math.Sin(coldBearing), Math.Cos(coldBearing) * hemisphere), width, height);
                var upwind = new AirPath(latitude, -Math.PI / 2, width, height);
                var north = new AirPath(latitude, 0, width, height);
                var south = new AirPath(latitude, Math.PI, width, height);
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
                    if (currents != null)
                        annual += MarineAirCoupling * currents.SampleAir((x+.5f)/width,(y+.5f)/height);
                    float amplitude=sine*(8.566f+3.489f*distance+5.829f*inland+11.226f*s*inland-2.701f*contrast);
                    float lag=sine*(1.579f-2.569f*distance-3.087f*inland+4.774f*s*inland+.111f*contrast);
                    float land = 1 - regionalSea[i], z = relief[i];
                    float poleward = FMath.Lerp(south.Read(relief, x, width), north.Read(relief, x, width), .5f + .5f*hemisphere);
                    float winterSwing = s * (7 + 17 * land);
                    float snow = FMath.Clamp((3 - (seaLevel - 5*z - winterSwing)) / 28, 0, 1.5f);
                    float melt = FMath.Clamp((seaLevel - 5.5f*z + winterSwing + 5) / 10, 0, 1);
                    float coupling = (float)Math.Sqrt(FMath.Clamp(2*(1-nearbySea[i]), 0, 1))
                        * (.25f + .75f * melt*melt*(3-2*melt));
                    // Moist midlatitude air releases heat over cold land; an intervening
                    // ridge limits that inflow. A poleward range shelters the winter lowlands.
                    float barrier = Math.Max(0, storm.Read(relief, x, width) - z);
                    float marineAir = (float)Math.Exp(-barrier / 1.2f) * storm.Read(nearbySea, x, width);
                    float westRelief = upwind.Read(relief, x, width)-z;
                    float coldBarrier = Math.Max(0, coldWind.Read(relief, x, width)-z);
                    float coldSource = (1-coldWind.Read(nearbySea, x, width))*(east[x]+Math.Max(0,westRelief))
                        *(float)(coldLatitude*Math.Exp(-coldBarrier/1.2f));
                    // Unobstructed meridional continental corridors on eastern continental margins
                    // (e.g. Siberian High cold surges down East China vs Himalaya-shielded India)
                    float polewardSea = FMath.Lerp(south.Read(regionalSea, x, width), north.Read(regionalSea, x, width), .5f + .5f*hemisphere);
                    float polewardLand = 1f - polewardSea;
                    float eastMargin = Math.Max(0f, east[x] - west[x]);
                    float openCorridor = (float)Math.Exp(-Math.Max(0f, poleward - z) / .95f);
                    float coldSurge = eastMargin * polewardLand * openCorridor * (float)Math.Exp(-Math.Pow((degrees - 25) / 8.0, 2));
                    float winterHeat = 5.8070f*snow*marineAir + 2.5175f*s*FMath.Clamp(poleward-z, -3, 3) - 1.3023f*coldSource - 2.3f*coldSurge;
                    // Circumpolar subpolar ocean air outbreaks cool warm-temperate continents facing an unbroken poleward ocean (e.g. Southern Australia)
                    float subpolarOceanCool = FMath.Clamp((polewardSea - .68f) / .28f, 0f, 1f) * (float)Math.Exp(-Math.Pow((degrees - 35) / 7.5, 2));
                    // High-albedo polar continental ice-sheet & katabatic inversion cooling (Gleba polar continental drop on interior ice domes)
                    float polarLat = FMath.Clamp((float)((degrees - 67.0) / 14.0), 0f, 1f);
                    polarLat = polarLat * polarLat * (3f - 2f * polarLat);
                    float polarElev = FMath.Clamp((SurfaceHeight(w, i) - 1200f) / 1300f, 0f, 1f);
                    float iceInterior = 1f - FMath.Clamp(nearbySea[i] / .14f, 0f, 1f);
                    float polarIceCapCool = !FlatTerrainAnalysis.AreaWater(w.Water[i])
                        ? 21.0f * polarLat * polarElev * iceInterior : 0f;
                    // Dry ground heats ahead of summer maritime inflow. On a
                    // high plateau the warm-season free-air lapse is steeper.
                    float seaContrast = hemisphere*(south.Read(nearbySea, x, width)-north.Read(nearbySea, x, width));
                    float premonsoon = Math.Max(0,seaContrast)*(1+Math.Max(0,poleward-z))*premonsoonBelt;
                    float summerLapse = -.6556f*source[i];
                    annual += coupling * (-.6123f*equatorial + 2.7301f*subtropics*land - 3.5319f*subtropics*coastContrast[i]
                        - 2.2503f*s*FMath.Clamp(westRelief, -3, 3) - 1.8f*subpolarOceanCool + .5f*(winterHeat+summerLapse)) - polarIceCapCool;
                    amplitude += coupling * (2.1601f*sine*(marine-nearbySea[i]) + .5f*hemisphere*(summerLapse-winterHeat));
                    lag -= coupling * hemisphere*(.4912f*tropical+.5713f*premonsoon);
                    // In warm-season westerlies, a broad elevated continental source
                    // heats air above the lowland boundary layer. Marine air damps
                    // the effect. westRelief is a regional height difference in km.
                    float plateauHeat=2.9068f*westerlies*(1-nearbySea[i])*FMath.Clamp(westRelief-.7f,0,2.5f);
                    float excursion = 5 + 23*land + 4*Math.Min(2, poleward)*land;
                    for (int m = 0; m < 12; m++)
                    {
                        float c = Summer[m], q = Lag[m];
                        float fromConvergence = (latitudeDegrees - excursion*c) / 13;
                        float radiative = 1.2139f*solar[m] + 2.8346f*land*solar[(m+11)%12];
                        float snowShape = s2*land*(-1.6792f*(2*c*c-1) + 1.0032f*(2*c*q));
                        float cloudCooling = .7768f*(float)Math.Exp(-fromConvergence*fromConvergence);
                        w.Temperature[off+m] = FMath.Clamp(annual + amplitude*c + lag*q + plateauHeat*Math.Max(0,hemisphere*c)
                            + coupling*(radiative + snowShape - cloudCooling), -85, 55);
                    }
                }
            });
        }

        // This predictor transports moisture from the background temperature. The
        // final moisture solve then uses the adjusted surface normals, as on save reload.
        // Keep both on the same grid so rotating by an atmospheric cell moves the
        // cloud forcing, even at coasts and when a shift crosses the map seam.
        internal static FlatMoistureState PredictRadiativeCloud(WorldData w, CancellationToken cancel, int workers, bool seasonalWinds)
            => FlatMoisture.BuildWithCirculation(w, cancel, workers, true, null, seasonalWinds);

        static void ApplyLandCloudRadiation(WorldData w, FlatMoistureState predictor, ParallelOptions options)
        {
            Parallel.For(0, w.Height, options, y =>
            {
                float v = (y + .5f) / w.Height;
                var sunlight = new float[12];
                for (int m = 0; m < 12; m++) sunlight[m] = (float)(SolarFlux((v - .5) * Math.PI, m) / 300);
                var snow = new FlatSeasonalSnow();
                for (int x = 0; x < w.Width; x++)
                {
                    int i = y*w.Width + x;
                    if (FlatTerrainAnalysis.AreaWater(w.Water[i])) continue;
                    float u = (x + .5f) / w.Width;
                    bool freezes = false;
                    for (int m = 0; m < 12; m++)
                    {
                        // Use the frequency of precipitating cloud as a proxy for cloud
                        // exposure. The two reference fractions are the calibration-land
                        // means weighted by incident sunlight and freezing respectively.
                        float condensation = 30*predictor.Sample(predictor.Condensation, u, v, m);
                        float cloud = snow.Cloud[m] = (float)(1 - Math.Exp(-condensation / 45f));
                        int j = i*12 + m;
                        float air = w.Temperature[j], cold = FMath.Clamp((4 - air) / 18, 0, 1);
                        float stable = cold*cold*(3 - 2*cold);
                        // Cloud shades warm ground. In a cold, stable boundary layer its
                        // downward longwave emission limits surface radiative cooling.
                        float shortwave = .9384f * (.6461f - cloud) * sunlight[m];
                        float longwave = 8.7247f * (cloud - .3500f) * stable;
                        w.Temperature[j] = FMath.Clamp(air + shortwave + longwave, -85, 55);
                        freezes |= w.Temperature[j] < 0;
                    }
                    if (!freezes) continue;
                    for (int m = 0; m < 12; m++) snow.Rain[m] = predictor.Sample(predictor.Rain,u,v,m);
                    snow.Apply(w.Temperature,i*12,sunlight);
                }
            });
        }

        public static void Build(WorldData w,CancellationToken cancel,int workers=0)
            => Build(w,cancel,workers,true,true);

        // The offline circulation experiment can hold either forcing at its control.
        internal static void Build(WorldData w,CancellationToken cancel,int workers,bool seasonalWinds,bool oceanHeat)
        {
            var options=FlatWorkerPolicy.Options(cancel,workers);
            var currents=oceanHeat?FlatOceanCirculation.Build(w,options):null;
            BuildTemperature(w,options,currents);
            ApplyLandCloudRadiation(w,PredictRadiativeCloud(w,cancel,workers,seasonalWinds),options);
            w.Moisture=FlatMoisture.BuildWithCirculation(w,cancel,workers,true,currents,seasonalWinds);
            FlatOrographicRainfall.Distribute(w,w.Moisture,options);
            Parallel.For(0,w.Height,options,y=>
            {
                for(int x=0;x<w.Width;x++)
                {
                    int i=y*w.Width+x;
                    w.Climates[i]=KoppenClassifier.Classify(w.Temperature,w.Precipitation,i*12);
                }
            });
        }
    }
}

