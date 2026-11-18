using System;

namespace Orbis.Flat
{
    // Optional observer for an already running layered solver. Use and dispose on
    // its owner thread; Latest belongs to the observer until the next accepted frame.
    public sealed class ClimateWeatherHistory : IDisposable
    {
        const int Width = FlatWeatherSimulation.Width, Height = FlatWeatherSimulation.Height;
        const int RegionSide = 8, Regions = (Width/RegionSide)*(Height/RegionSide);
        readonly FlatWeatherSimulation solver;
        readonly ClimateFeatureSet features;
        readonly int minimumFrames, refitFrames;
        readonly double minimumPhysicalSeconds, stepDays;
        readonly ClimateScoreWorkspace work = new ClimateScoreWorkspace();
        readonly Bucket[] history = new Bucket[12*Regions*2];
        readonly double[] weights = new double[Height];
        long lastTick = long.MinValue, epoch;
        double lastDay = double.NaN, lastModelTime = double.NaN;
        bool disposed;

        sealed class Bucket
        {
            internal ClimateReference Training, Prior;
            internal int Frames, LastFit;
            internal long LastAccepted = long.MinValue;
        }

        public long Frames { get; private set; }
        public double LastSampleDay { get; private set; } = double.NaN;
        public ClimateScore[] Latest { get; } = new ClimateScore[Width*Height];

        public ClimateWeatherHistory(FlatWeatherSimulation solver, double solverStepSeconds = .1,
            int minimumFrames = 8, int refitFrames = 8, double minimumPhysicalSeconds = 60, ClimateFeatureSet features = null)
        {
            this.solver = solver ?? throw new ArgumentNullException(nameof(solver));
            if (!OnlineClimateMoments.Finite(solverStepSeconds) || solverStepSeconds <= 0 || solverStepSeconds > .100001)
                throw new ArgumentOutOfRangeException(nameof(solverStepSeconds));
            if (minimumFrames < 2 || refitFrames < 1 || !(minimumPhysicalSeconds >= 0) || double.IsInfinity(minimumPhysicalSeconds))
                throw new ArgumentException("Invalid observation cadence or prior support.");
            this.minimumFrames = minimumFrames;
            this.refitFrames = refitFrames;
            this.minimumPhysicalSeconds = minimumPhysicalSeconds;
            this.features = features ?? ClimateFeatureSet.Weather;
            stepDays = solverStepSeconds*OrbisAtmosphereCore.PhysicalSecondsPerSecond/86400;
            for (int y = 0; y < Height; y++) weights[y] = FlatRiverDischarge.RelativeArea(y,Height);
            epoch = solver.Model.StateEpoch;
            solver.Stepped += Observe;
        }

        void Observe(FlatWeatherSimulation source, double day)
        {
            if (!OnlineClimateMoments.Finite(day) || Math.Abs(day) > 1e15) return;
            double modelTime = source.Model.Time;
            if (day < lastDay || modelTime < lastModelTime || source.Model.StateEpoch != epoch)
            {
                Array.Clear(history, 0, history.Length);
                Array.Clear(Latest, 0, Latest.Length);
                Frames = 0;
                lastTick = long.MinValue;
                lastModelTime = LastSampleDay = double.NaN;
                epoch = source.Model.StateEpoch;
            }
            lastDay = day;
            long tick = FlatCycloneWeather.StepAt(day);
            if (tick == lastTick || (modelTime-lastModelTime)*OrbisAtmosphereCore.PhysicalSecondsPerSecond < minimumPhysicalSeconds)
                return;
            var probe = source.Instantaneous(0);
            if (probe.Source != FlatClimateObservationSource.LayeredStep ||
                !(Math.Abs(probe.PrecipitationWindowDays-stepDays) <= stepDays*1e-8)) return;
            int month = FMath.Wrap((int)(Math.Floor(day/30) % 12),12);
            int start = month*Regions*2;
            // Freeze the reference before observing any cell in the new frame.
            // Repeated samples from a single frame cannot satisfy minimumFrames.
            for (int i = start; i < start+Regions*2; i++)
            {
                var bucket = history[i];
                if (bucket == null || bucket.Frames < minimumFrames ||
                    (bucket.LastFit > 0 && bucket.Frames-bucket.LastFit < refitFrames)) continue;
                bucket.Prior = new ClimateReference(features, stepWindowDays:stepDays);
                bucket.Prior.Merge(bucket.Training);
                bucket.Prior.Fit();
                bucket.LastFit = bucket.Frames;
            }
            for (int cell = 0; cell < Latest.Length; cell++)
            {
                int y = cell/Width;
                int region = y/RegionSide*(Width/RegionSide) + cell%Width/RegionSide;
                int key = start + 2*region + (source.IsWaterSurface(cell) ? 1 : 0);
                var bucket = history[key];
                if (bucket == null)
                    history[key] = bucket = new Bucket {Training=new ClimateReference(features, stepWindowDays:stepDays)};
                var row = source.Instantaneous(cell);
                Latest[cell] = bucket.Prior?.Score(row,work) ?? default;
                if (bucket.Training.Add(row, weights[y], work) && bucket.LastAccepted != tick)
                {
                    bucket.LastAccepted = tick;
                    bucket.Frames++;
                }
            }
            Frames++;
            lastModelTime = modelTime;
            lastTick = tick;
            LastSampleDay = day;
        }

        public void Dispose()
        {
            if (disposed) return;
            solver.Stepped -= Observe;
            disposed = true;
        }
    }
}
