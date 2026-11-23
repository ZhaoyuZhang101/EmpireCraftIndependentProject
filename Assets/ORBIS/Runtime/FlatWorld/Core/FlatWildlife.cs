using System;
using System.Collections.Generic;

namespace Orbis.Flat
{
    public enum WildlifeSpecies : byte { Rabbit = 1, Deer = 2, Bird = 3, Butterfly = 4 }
    public enum WildlifeState : byte { Idle, Wandering, SeekingFood, Eating, SeekingRest, Sleeping }

    [Serializable]
    public sealed class WildlifeActor
    {
        public int Id, HomeTile, GoalTile;
        public WildlifeSpecies Species;
        public WildlifeState State;
        public double X, Y;
        // Needs increase towards one. MoveSpeed is the current equatorial tile distance per second.
        public float Hunger, Fatigue, Facing = 1, AnimationTime, MoveSpeed;
        public uint RandomState = 1;
        // Local climate is derived at a staggered cadence and does not extend the save schema.
        public float Activity { get; internal set; } = 1;
        public float LocalTemperature { get; internal set; } = 15;

        internal int[] Path;
        internal int PathIndex, RequestToken, FailedGoal = -1, ClimateTile = -1;
        internal bool PathPending, ResumeTravel, SegmentActive;
        internal float DecisionDelay, RequestAge, ClimateDelay;
        internal double SegmentX, SegmentY, SegmentDX, SegmentDY, SegmentLength, SegmentProgress;

        public WildlifeActor Clone()
        {
            return new WildlifeActor
            {
                Id = Id, HomeTile = HomeTile, GoalTile = GoalTile, Species = Species, State = State,
                X = X, Y = Y, Hunger = Hunger, Fatigue = Fatigue, Facing = Facing,
                AnimationTime = AnimationTime, MoveSpeed = MoveSpeed, RandomState = RandomState,
                Activity = Activity, LocalTemperature = LocalTemperature
            };
        }
    }

    public interface IWildlifePathfinder
    {
        bool CanRequestPath => true;
        void RequestPath(int start, int goal, bool flying, Action<int[]> completed);
    }

    /// <summary>Independent animal needs and motion. The injected pathfinder supplies every travel route.</summary>
    public sealed class FlatWildlifeSimulation : IDisposable
    {
        public const int MaxActors = 512;
        public const int GoalSearchRadius = 8;
        const int SearchLimit = 128;
        const float StepSeconds = .1f;
        readonly WorldData world;
        readonly IWildlifePathfinder pathfinder;
        readonly int[] searchTiles = new int[SearchLimit], searchDepth = new int[SearchLimit], visited;
        readonly object completionGate = new object();
        readonly Queue<PathResult> completions = new Queue<PathResult>();
        volatile bool disposed;
        double pendingSeconds, pendingPathWaitSeconds;
        int searchStamp, requestBudget, decisionBudget, decisionCursor;

        struct PathResult
        {
            public WildlifeActor Actor;
            public int Token;
            public int[] Tiles;
        }

        public FlatWildlifeSimulation(WorldData world, IWildlifePathfinder pathfinder)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            visited = new int[world.Count];
            Initialize();
            foreach (var actor in world.Wildlife) { ResetRuntime(actor); RefreshClimate(actor, world.Day); }
        }

        static bool Flying(WildlifeActor actor)
        { return actor.Species == WildlifeSpecies.Bird || actor.Species == WildlifeSpecies.Butterfly; }

        static bool Travelling(WildlifeState state)
        { return state == WildlifeState.Wandering || state == WildlifeState.SeekingFood || state == WildlifeState.SeekingRest; }

        static void ResetRuntime(WildlifeActor actor)
        {
            actor.Path = null;
            actor.PathIndex = 0;
            actor.PathPending = actor.SegmentActive = false;
            actor.RequestToken++;
            actor.RequestAge = 0;
            actor.MoveSpeed = 0;
            actor.ClimateTile = -1;
            actor.ClimateDelay = 0;
            actor.ResumeTravel = Travelling(actor.State);
            actor.DecisionDelay = actor.ResumeTravel ? 0 : .2f + actor.Id % 13 * .11f;
        }

        static float Random(WildlifeActor actor)
        {
            uint state = actor.RandomState == 0 ? (uint)actor.Id + 1 : actor.RandomState;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            actor.RandomState = state;
            return (state & 0xffffff) / 16777216f;
        }

        void Initialize()
        {
            if (world.WildlifeInitialized || world.Facts == null) return;
            int capacity = Math.Min(MaxActors, Math.Max(16, world.Count / 512));
            var sites = new int[capacity];
            int count = 0;
            uint seed = FMath.Seed(world.Seed) ^ 0x63a93f17u;
            // One deterministic reservoir scan occurs only when a world first gains wildlife.
            for (int tile = 0; tile < world.Count; tile++)
            {
                if (!IsGroundTile(world, tile) ||
                    (!SpeciesHabitat(tile, WildlifeSpecies.Rabbit) && !SpeciesHabitat(tile, WildlifeSpecies.Deer) &&
                     !SpeciesHabitat(tile, WildlifeSpecies.Bird) && !SpeciesHabitat(tile, WildlifeSpecies.Butterfly))) continue;
                count++;
                int slot = count <= capacity ? count - 1 : (int)(FMath.Hash(seed ^ (uint)tile) % (uint)count);
                if (slot < capacity) sites[slot] = tile;
            }
            for (int i = 0; i < Math.Min(count, capacity); i++)
            {
                int tile = sites[i];
                uint random = FMath.Hash(seed ^ (uint)tile ^ (uint)(i * 43));
                var species = WildlifeSpecies.Bird;
                float total = 0;
                for (int choice = 1; choice <= 4; choice++) total += SpeciesSuitability(world, tile, (WildlifeSpecies)choice);
                float selection = FMath.Unit(random ^ 0x97b3d42fu) * total;
                for (int choice = 1; choice <= 4; choice++)
                {
                    var candidate = (WildlifeSpecies)choice;
                    float weight = SpeciesSuitability(world, tile, candidate);
                    if (weight <= 0) continue;
                    species = candidate;
                    if (selection < weight) break;
                    selection -= weight;
                }
                var actor = new WildlifeActor
                {
                    Id = i + 1, HomeTile = tile, GoalTile = tile, Species = species,
                    X = tile % world.Width + .5, Y = tile / world.Width + .5,
                    RandomState = random == 0 ? 1 : random
                };
                actor.Hunger = .15f + Random(actor) * .45f;
                actor.Fatigue = .08f + Random(actor) * .45f;
                actor.Facing = Random(actor) < .5f ? -1 : 1;
                actor.AnimationTime = Random(actor) * 20;
                ResetRuntime(actor);
                world.Wildlife.Add(actor);
            }
            world.WildlifeInitialized = true;
        }

        public static int Tile(WorldData world, WildlifeActor actor)
        {
            if (world == null || actor == null) return -1;
            return FlatScrollMath.Tile(actor.X, actor.Y, world.Width, world.Height);
        }

        static bool GroundClass(WorldData world, int tile)
        {
            return world != null && world.Facts != null && tile >= 0 && tile < world.Count &&
                world.Water[tile] == WaterKind.Land && world.Buildings[tile] == Structure.None &&
                world.Elevation[tile] >= 0 && world.Elevation[tile] < 3520 && world.Climates[tile] != Koppen.EF &&
                world.Facts.Vegetation[tile] != VegetationCover.Ice &&
                world.Facts.Gradient[tile] < 280;
        }

        static void Fold(WorldData world, ref double x, ref double y)
        {
            y = FlatScrollMath.Wrap(y, world.Height * 2.0);
            if (y >= world.Height)
            {
                y = world.Height * 2.0 - y;
                x += world.Width * .5;
            }
            x = FlatScrollMath.Wrap(x, world.Width);
            y = Math.Min(world.Height - 1e-8, Math.Max(0, y));
        }

        static bool GroundPoint(WorldData world, double x, double y)
        {
            Fold(world, ref x, ref y);
            int tile = FlatScrollMath.Tile(x, y, world.Width, world.Height);
            if (!GroundClass(world, tile)) return false;
            float fx = (float)x, fy = (float)y;
            if(world.GeographicSurface!=null && !FlatGeographicSurface.IsGroundAt(world,
                new FlatSeaPoint(FlatGeographicSurface.Wrap(x*360/world.Width-180),Math.Max(-90,Math.Min(90,y*180/world.Height-90)))))return false;
            return FlatSurfaceSampling.SampleShore(world, fx, fy) < -.045f &&
                FlatWaterGeometry.RiverDistance(world, tile, fx - tile % world.Width, fy - tile / world.Width) > .055f;
        }

        public static bool IsGroundTile(WorldData world, int tile)
        {
            return GroundClass(world, tile) && GroundPoint(world, tile % world.Width + .5, tile / world.Width + .5);
        }

        static int Direction(WorldData world, int from, int to)
        {
            if (from < 0 || from >= world.Count || to < 0 || to >= world.Count || from == to) return -1;
            for (int direction = 0; direction < 4; direction++)
                if (world.SphereNeighbor(from, direction) == to) return direction;
            return -1;
        }

        public static bool CanStep(WorldData world, int from, int to, bool flying)
        {
            if (world == null) return false;
            int direction = Direction(world, from, to);
            if (direction < 0) return false;
            if (flying) return true;
            if (!IsGroundTile(world, from) || !IsGroundTile(world, to) || !FlatGeographicSurface.GroundEdge(world,from,to) ||
                Math.Abs(world.Elevation[from] - world.Elevation[to]) > 140) return false;
            double x = from % world.Width + .5, y = from / world.Width + .5;
            double dx = direction == 0 ? -1 : direction == 1 ? 1 : 0;
            double dy = direction == 2 ? -1 : direction == 3 ? 1 : 0;
            for (int sample = 1; sample < 8; sample++)
                if (!GroundPoint(world, x + dx * sample / 8, y + dy * sample / 8)) return false;
            return true;
        }

        bool Habitat(int tile, WildlifeSpecies species)
        {
            return IsGroundTile(world, tile) && SpeciesHabitat(tile, species);
        }

        bool SpeciesHabitat(int tile, WildlifeSpecies species)
        { return SpeciesSuitability(world, tile, species) > 0; }

        public static float HabitatSuitability(WorldData world, int tile, WildlifeSpecies species)
        { return IsGroundTile(world, tile) ? SpeciesSuitability(world, tile, species) : 0; }

        static float SpeciesSuitability(WorldData world, int tile, WildlifeSpecies species)
        {
            var cover = world.Facts.Vegetation[tile];
            if (cover == VegetationCover.Bare || cover == VegetationCover.Ice) return 0;
            float temperature = world.Facts.MeanTemperature[tile], weight, moistureMinimum;
            switch (species)
            {
                case WildlifeSpecies.Rabbit:
                    if (temperature < -24 || temperature > 31 || cover == VegetationCover.TropicalRainforest ||
                        cover == VegetationCover.Marsh || cover == VegetationCover.Tundra) return 0;
                    weight = cover == VegetationCover.Grassland || cover == VegetationCover.Shrubland ? 1 :
                        cover == VegetationCover.DrySteppe || cover == VegetationCover.AlpineMeadow ? .75f : .5f;
                    moistureMinimum = cover == VegetationCover.DrySteppe ? .035f : .10f;
                    break;
                case WildlifeSpecies.Deer:
                    if (temperature < -25 || temperature > 32 || cover == VegetationCover.Marsh || cover == VegetationCover.Tundra) return 0;
                    weight = cover >= VegetationCover.SeasonalForest && cover <= VegetationCover.BorealForest ? 1 : .6f;
                    moistureMinimum = .14f;
                    break;
                case WildlifeSpecies.Bird:
                    if (temperature < -12 || temperature > 35) return 0;
                    weight = cover == VegetationCover.Marsh ? 1 : cover >= VegetationCover.SeasonalForest &&
                        cover <= VegetationCover.TropicalRainforest ? .85f : .6f;
                    moistureMinimum = .035f;
                    break;
                case WildlifeSpecies.Butterfly:
                    if (temperature < 8 || temperature > 32 || cover == VegetationCover.BorealForest || cover == VegetationCover.Tundra) return 0;
                    weight = cover == VegetationCover.Grassland ? 1 : cover == VegetationCover.Shrubland ? .8f : .55f;
                    moistureMinimum = .16f;
                    break;
                default: return 0;
            }
            var climate = world.Climates[tile];
            bool arid = climate >= Koppen.BWh && climate <= Koppen.BSk;
            if (arid && !(species == WildlifeSpecies.Rabbit && cover == VegetationCover.DrySteppe))
                moistureMinimum = Math.Max(moistureMinimum, .18f);
            float moisture = Math.Max(world.Facts.Wetness[tile], Math.Max(world.Facts.Freshness[tile] * .65f,
                FMath.Clamp(world.Facts.AnnualRain[tile] / 3000, 0, .45f)));
            if (moisture < moistureMinimum) return 0;
            float water = FMath.Clamp((moisture - moistureMinimum) / Math.Max(.1f, .65f - moistureMinimum), 0, 1);
            float heat = 1 - FMath.Clamp((temperature - 24) / 16, 0, .65f);
            return weight * (.45f + .55f * water) * heat;
        }

        static float Ramp(float value, float minimum, float maximum)
        {
            float t = FMath.Clamp((value - minimum) / (maximum - minimum), 0, 1);
            return t * t * (3 - 2 * t);
        }

        static float MonthlyTemperature(WorldData world, int tile, double day)
        {
            double position = day / 30, whole = Math.Floor(position);
            int month = (int)((whole % 12 + 12) % 12);
            var climate = FlatClimateYear.At(FMath.Seed(world.Seed), day);
            climate.AtTile(tile, world.Width, world.Height, out float degrees, out _);
            return degrees + FMath.Lerp(world.Temperature[tile * 12 + month], world.Temperature[tile * 12 + (month + 1) % 12], (float)(position - whole));
        }

        public static float SeasonalActivity(WorldData world, WildlifeActor actor, double day)
        {
            int tile = Tile(world, actor);
            if (tile < 0 || world.Facts == null) return 0;
            float temperature = MonthlyTemperature(world, tile, day);
            if (actor.Species == WildlifeSpecies.Butterfly)
            {
                FlatEcology.SampleSeason(world, tile, day, out float growth, out _);
                return Ramp(temperature, 6, 16) * (1 - Ramp(temperature, 31, 40)) * (.25f + .75f * growth);
            }
            float cold = actor.Species == WildlifeSpecies.Bird ? -10 : -18;
            return (.45f + .55f * Ramp(temperature, cold, 8)) * (1 - .5f * Ramp(temperature, 29, 40));
        }

        void RefreshClimate(WildlifeActor actor, double day)
        {
            actor.ClimateTile = Tile(world, actor);
            if (actor.ClimateTile < 0) return;
            actor.LocalTemperature = MonthlyTemperature(world, actor.ClimateTile, day);
            actor.Activity = SeasonalActivity(world, actor, day);
            actor.ClimateDelay = 1.5f + actor.Id % 7 * .1f;
        }

        static bool ColdRest(WildlifeActor actor)
        { return actor.Species == WildlifeSpecies.Butterfly && actor.Activity < .12f; }

        public void Tick(float seconds, double calendarDay)
        { Tick(seconds, calendarDay, seconds); }

        public void Tick(float seconds, double calendarDay, float pathWaitSeconds)
        {
            // Callbacks are deliberately retained during pause, including synchronous path completions.
            if (disposed || seconds == 0) return;
            if (!FMath.Finite(seconds) || seconds < 0 || !FMath.Finite(pathWaitSeconds) || pathWaitSeconds < 0 ||
                double.IsNaN(calendarDay) || double.IsInfinity(calendarDay))
                throw new ArgumentOutOfRangeException();
            Initialize();
            pendingSeconds += seconds;
            pendingPathWaitSeconds += pathWaitSeconds;
            int steps = (int)Math.Min(32, Math.Floor((pendingSeconds + 1e-9) / StepSeconds));
            if (steps == 0) return;
            // Waiting follows elapsed real time even when simulation catch-up reaches its per-frame budget.
            float waitPerStep = (float)(pendingPathWaitSeconds / steps);
            pendingPathWaitSeconds = 0;
            for (int step = 0; step < steps; step++)
            {
                pendingSeconds = Math.Max(0, pendingSeconds - StepSeconds);
                Step(StepSeconds, calendarDay, waitPerStep);
            }
        }

        void Step(float seconds, double calendarDay, float pathWaitSeconds)
        {
            requestBudget = 8;
            decisionBudget = 8;
            ApplyCompletions();
            int count = world.Wildlife.Count;
            int first = count == 0 ? 0 : decisionCursor % count;
            for (int offset = 0; offset < count; offset++)
            {
                int index = (first + offset) % count;
                var actor = world.Wildlife[index];
                actor.MoveSpeed = 0;
                actor.AnimationTime = (actor.AnimationTime + seconds) % 3600;
                actor.ClimateDelay -= seconds;
                if (actor.ClimateDelay <= 0 || actor.ClimateTile != Tile(world, actor)) RefreshClimate(actor, calendarDay);
                if (ColdRest(actor))
                {
                    int tile = Tile(world, actor);
                    if (Habitat(tile, actor.Species) && GroundPoint(world, actor.X, actor.Y))
                    {
                        if (actor.State != WildlifeState.Sleeping) CancelPath(actor);
                        actor.ResumeTravel = false;
                        actor.State = WildlifeState.Sleeping;
                        actor.GoalTile = tile;
                    }
                    else if (actor.State != WildlifeState.SeekingRest)
                    {
                        CancelPath(actor);
                        actor.ResumeTravel = false;
                        actor.State = WildlifeState.SeekingRest;
                        actor.DecisionDelay = 0;
                    }
                }
                bool resting = actor.State == WildlifeState.Eating || actor.State == WildlifeState.Sleeping;
                if (resting && !Habitat(Tile(world, actor), actor.Species)) Idle(actor, 1);
                UpdateNeeds(actor, seconds);
                actor.DecisionDelay -= seconds;
                bool flying = Flying(actor);
                resting = actor.State == WildlifeState.Eating || actor.State == WildlifeState.Sleeping;
                if ((!flying || resting) && !GroundPoint(world, actor.X, actor.Y))
                {
                    // Editing can remove the land beneath an animal. It waits for a safe exit or restored habitat.
                    if (actor.Path != null || actor.PathPending) CancelPath(actor);
                    actor.State = WildlifeState.Idle;
                    actor.DecisionDelay = Math.Max(actor.DecisionDelay, 2);
                    continue;
                }
                if (actor.State == WildlifeState.Eating)
                {
                    if (actor.Hunger <= .08f) Idle(actor, 1 + Random(actor) * 3);
                    continue;
                }
                if (actor.State == WildlifeState.Sleeping)
                {
                    if (actor.Fatigue <= .05f && !Night(actor, calendarDay) && !ColdRest(actor))
                        Idle(actor, 1 + Random(actor) * 3);
                    continue;
                }
                if (actor.PathPending)
                {
                    actor.RequestAge += pathWaitSeconds;
                    if (actor.RequestAge >= 8) FailPath(actor);
                    continue;
                }
                if (actor.Path != null)
                {
                    Move(actor, seconds);
                    continue;
                }
                if (actor.DecisionDelay <= 0)
                {
                    Decide(actor, calendarDay);
                    if (actor.PathPending) decisionCursor = (index + 1) % count;
                }
            }
        }

        static void UpdateNeeds(WildlifeActor actor, float seconds)
        {
            float hunger = actor.State == WildlifeState.Eating ? -.075f : actor.State == WildlifeState.Sleeping ? .0005f : .0016f;
            float fatigue = actor.State == WildlifeState.Sleeping ? -.04f : Travelling(actor.State) ? .0018f : .0011f;
            if (ColdRest(actor)) hunger = .00003f;
            actor.Hunger = FMath.Clamp(actor.Hunger + hunger * seconds, 0, 1);
            actor.Fatigue = FMath.Clamp(actor.Fatigue + fatigue * seconds, 0, 1);
        }

        bool Night(WildlifeActor actor, double calendarDay)
        {
            double localTime = FlatSolarClock.Phase(calendarDay + actor.X / world.Width - .5);
            return localTime < .22 || localTime > .78;
        }

        void Decide(WildlifeActor actor, double calendarDay)
        {
            int tile = Tile(world, actor);
            if (tile < 0) return;
            if (actor.ResumeTravel)
            {
                if (actor.GoalTile >= 0 && actor.GoalTile < world.Count &&
                    (actor.State == WildlifeState.Wandering || Habitat(actor.GoalTile, actor.Species)))
                {
                    if (actor.GoalTile == tile) { actor.ResumeTravel = false; Arrive(actor); return; }
                    if (!CanSchedulePath(actor)) return;
                    decisionBudget--;
                    actor.ResumeTravel = false;
                    Request(actor, actor.GoalTile, actor.State);
                    return;
                }
                actor.ResumeTravel = false;
            }
            WildlifeState state;
            if (ColdRest(actor)) state = WildlifeState.SeekingRest;
            else if (actor.Hunger >= .65f) state = WildlifeState.SeekingFood;
            else if (actor.Fatigue >= .78f || Night(actor, calendarDay) && actor.Fatigue >= .18f) state = WildlifeState.SeekingRest;
            else state = WildlifeState.Wandering;
            if (state != WildlifeState.Wandering && Habitat(tile, actor.Species))
            {
                actor.State = state == WildlifeState.SeekingFood ? WildlifeState.Eating : WildlifeState.Sleeping;
                actor.GoalTile = tile;
                return;
            }
            if (!CanSchedulePath(actor)) return;
            decisionBudget--;
            int goal = FindGoal(actor, tile, state);
            if (goal < 0) { Idle(actor, 3 + Random(actor) * 5); return; }
            Request(actor, goal, state);
        }

        bool CanSchedulePath(WildlifeActor actor)
        {
            if (decisionBudget > 0 && requestBudget > 0 && pathfinder.CanRequestPath) return true;
            actor.DecisionDelay = StepSeconds;
            return false;
        }

        int FindGoal(WildlifeActor actor, int start, WildlifeState state)
        {
            if (++searchStamp == int.MaxValue) { Array.Clear(visited, 0, visited.Length); searchStamp = 1; }
            int head = 0, tail = 1, goal = -1;
            float best = float.NegativeInfinity;
            searchTiles[0] = start;
            searchDepth[0] = 0;
            visited[start] = searchStamp;
            bool flying = Flying(actor);
            while (head < tail)
            {
                int tile = searchTiles[head], depth = searchDepth[head++];
                if (tile != start && tile != actor.FailedGoal && Habitat(tile, actor.Species))
                {
                    float distanceWeight = ColdRest(actor) ? .7f : .15f;
                    float score = Random(actor) * 3 + SpeciesSuitability(world, tile, actor.Species) * 2 - depth * distanceWeight;
                    if (state == WildlifeState.SeekingRest && tile == actor.HomeTile) score += 4;
                    if (state == WildlifeState.Wandering) score += Math.Min(depth, 4) * .4f;
                    if (score > best) { best = score; goal = tile; }
                }
                if (depth >= GoalSearchRadius || tail >= SearchLimit) continue;
                int firstDirection = (int)(Random(actor) * 4);
                for (int choice = 0; choice < 4 && tail < SearchLimit; choice++)
                {
                    int next = world.SphereNeighbor(tile, (firstDirection + choice) % 4);
                    if (visited[next] == searchStamp || !CanStep(world, tile, next, flying)) continue;
                    visited[next] = searchStamp;
                    searchTiles[tail] = next;
                    searchDepth[tail++] = depth + 1;
                }
            }
            // A transient failure must still permit a later retry when it is the only reachable habitat.
            if (goal < 0 && actor.FailedGoal >= 0 && actor.FailedGoal < world.Count &&
                visited[actor.FailedGoal] == searchStamp && Habitat(actor.FailedGoal, actor.Species)) goal = actor.FailedGoal;
            return goal;
        }

        void Request(WildlifeActor actor, int goal, WildlifeState state)
        {
            actor.State = state;
            actor.GoalTile = goal;
            if (requestBudget <= 0 || !pathfinder.CanRequestPath) { actor.DecisionDelay = StepSeconds; actor.ResumeTravel = true; return; }
            int start = Tile(world, actor);
            actor.PathPending = true;
            actor.RequestAge = 0;
            int token = ++actor.RequestToken;
            requestBudget--;
            try
            {
                pathfinder.RequestPath(start, goal, Flying(actor), path =>
                {
                    if (disposed) return;
                    var copy = path == null || path.Length > world.Count ? null : (int[])path.Clone();
                    lock (completionGate)
                    {
                        if (!disposed) completions.Enqueue(new PathResult { Actor = actor, Token = token, Tiles = copy });
                    }
                });
            }
            catch (Exception)
            {
                // A failed route provider has the same bounded retry behavior as an unreachable destination.
                FailPath(actor);
            }
        }

        void ApplyCompletions()
        {
            while (true)
            {
                PathResult result;
                lock (completionGate)
                {
                    if (completions.Count == 0) return;
                    result = completions.Dequeue();
                }
                var actor = result.Actor;
                if (!actor.PathPending || result.Token != actor.RequestToken) continue;
                var route = result.Tiles;
                bool valid = route != null && route.Length > 0 && route[0] == Tile(world, actor) && route[route.Length - 1] == actor.GoalTile;
                for (int i = 1; valid && i < route.Length; i++) valid = CanStep(world, route[i - 1], route[i], Flying(actor));
                if (!valid) { FailPath(actor); continue; }
                actor.PathPending = false;
                actor.Path = route;
                actor.PathIndex = 1;
                actor.SegmentActive = false;
                if (route.Length == 1) Arrive(actor);
            }
        }

        static void CancelPath(WildlifeActor actor)
        {
            actor.RequestToken++;
            actor.PathPending = actor.SegmentActive = false;
            actor.Path = null;
            actor.MoveSpeed = 0;
        }

        static void Idle(WildlifeActor actor, float delay)
        {
            CancelPath(actor);
            actor.ResumeTravel = false;
            actor.State = WildlifeState.Idle;
            actor.DecisionDelay = delay;
        }

        static void FailPath(WildlifeActor actor)
        {
            actor.FailedGoal = actor.GoalTile;
            CancelPath(actor);
            actor.ResumeTravel = false;
            actor.DecisionDelay = 3 + Random(actor) * 4;
        }

        void Arrive(WildlifeActor actor)
        {
            var state = actor.State;
            CancelPath(actor);
            actor.FailedGoal = -1;
            int tile = Tile(world, actor);
            if (state == WildlifeState.SeekingFood && Habitat(tile, actor.Species)) actor.State = WildlifeState.Eating;
            else if (state == WildlifeState.SeekingRest && Habitat(tile, actor.Species)) actor.State = WildlifeState.Sleeping;
            else Idle(actor, 1.5f + Random(actor) * 4);
        }

        static float NominalSpeed(WildlifeActor actor)
        {
            float speed;
            switch (actor.Species)
            {
                case WildlifeSpecies.Deer: speed = .105f; break;
                case WildlifeSpecies.Bird: speed = .26f; break;
                case WildlifeSpecies.Butterfly: speed = .085f; break;
                default: speed = .13f; break;
            }
            return speed * (.9f + FMath.Unit((uint)actor.Id) * .2f) * (.5f + .5f * actor.Activity);
        }

        void BeginSegment(WildlifeActor actor, int from, int to)
        {
            int direction = Direction(world, from, to);
            double targetX = to % world.Width + .5, targetY = to / world.Width + .5;
            bool pole = direction >= 2 && from / world.Width == to / world.Width;
            if (pole)
            {
                targetX = from % world.Width + .5;
                targetY = direction == 2 ? -targetY : world.Height * 2.0 - targetY;
            }
            else targetX = actor.X + FlatScrollMath.Shortest(targetX - actor.X, world.Width);
            actor.SegmentX = actor.X;
            actor.SegmentY = actor.Y;
            actor.SegmentDX = targetX - actor.X;
            actor.SegmentDY = targetY - actor.Y;
            double latitude = ((actor.Y + targetY) * .5 / world.Height - .5) * Math.PI;
            double horizontal = actor.SegmentDX * Math.Cos(latitude);
            actor.SegmentLength = Math.Sqrt(horizontal * horizontal + actor.SegmentDY * actor.SegmentDY);
            actor.SegmentProgress = 0;
            actor.SegmentActive = true;
        }

        bool SafeSegment(WildlifeActor actor, double progress)
        {
            double change = progress - actor.SegmentProgress;
            int samples = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(actor.SegmentDX), Math.Abs(actor.SegmentDY)) * change * 10));
            for (int sample = 1; sample <= samples; sample++)
            {
                double fraction = actor.SegmentProgress + change * sample / samples;
                if (!GroundPoint(world, actor.SegmentX + actor.SegmentDX * fraction, actor.SegmentY + actor.SegmentDY * fraction)) return false;
            }
            return true;
        }

        void Move(WildlifeActor actor, float seconds)
        {
            double remaining = NominalSpeed(actor) * seconds, moved = 0;
            int budget = 16;
            bool flying = Flying(actor);
            while (actor.Path != null && remaining > 1e-9 && budget-- > 0)
            {
                int from = actor.Path[actor.PathIndex - 1], to = actor.Path[actor.PathIndex];
                if (!CanStep(world, from, to, flying)) { FailPath(actor); break; }
                if (!actor.SegmentActive) BeginSegment(actor, from, to);
                double distance = Math.Min(remaining, (1 - actor.SegmentProgress) * actor.SegmentLength);
                double progress = actor.SegmentLength < 1e-9 ? 1 : Math.Min(1, actor.SegmentProgress + distance / actor.SegmentLength);
                if (!flying && !SafeSegment(actor, progress)) { FailPath(actor); break; }
                double x = actor.SegmentX + actor.SegmentDX * progress, y = actor.SegmentY + actor.SegmentDY * progress;
                Fold(world, ref x, ref y);
                actor.X = x;
                actor.Y = y;
                if (Math.Abs(actor.SegmentDX) > 1e-8) actor.Facing = actor.SegmentDX < 0 ? -1 : 1;
                actor.SegmentProgress = progress;
                moved += distance;
                remaining -= distance;
                if (progress < 1 - 1e-9) break;
                actor.SegmentActive = false;
                actor.PathIndex++;
                if (actor.PathIndex >= actor.Path.Length) Arrive(actor);
            }
            if (actor.Path != null) actor.MoveSpeed = (float)(moved / seconds);
        }

        public void Dispose()
        {
            disposed = true;
            lock (completionGate) completions.Clear();
            foreach (var actor in world.Wildlife) CancelPath(actor);
        }
    }
}
