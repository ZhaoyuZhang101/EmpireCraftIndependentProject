using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Orbis.Flat;

public static class WildlifeRegression
{
    static int checks;

    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Wildlife regression: " + message);
    }

    static WorldData Habitat()
    {
        var world = new WorldData(64, 32, "wildlife-regression", TerrainPreset.Continents);
        world.Facts = new FlatTerrainFacts(world.Count);
        for (int i = 0; i < world.Count; i++)
        {
            world.Elevation[i] = world.WaterLevel[i] = 100;
            world.Climates[i] = Koppen.Cfb;
            world.Biomes[i] = Biome.Grassland;
            world.DrainOrder[i] = i;
            world.Fertility[i] = .6f;
            world.Facts.Vegetation[i] = VegetationCover.Grassland;
            world.Facts.ShoreDistance[i] = -8;
            world.Facts.MeanTemperature[i] = 15;
            world.Facts.Wetness[i] = .55f;
            world.Facts.AnnualRain[i] = 960;
            world.Facts.Canopy[i] = .25f;
            for (int m = 0; m < 12; m++)
            {
                world.Temperature[i * 12 + m] = 15;
                world.Precipitation[i * 12 + m] = 80;
            }
        }
        return world;
    }

    static WildlifeActor Actor(WorldData world, int tile, WildlifeState state = WildlifeState.Idle)
    {
        world.WildlifeInitialized = true;
        var actor = new WildlifeActor
        {
            Id = world.Wildlife.Count + 1, HomeTile = tile, GoalTile = tile,
            Species = WildlifeSpecies.Rabbit, State = state,
            X = tile % world.Width + .5, Y = tile / world.Width + .5,
            Hunger = .3f, Fatigue = .3f, Facing = 1, RandomState = 317
        };
        world.Wildlife.Add(actor);
        return actor;
    }

    sealed class Paths : IWildlifePathfinder
    {
        readonly WorldData world;
        public bool Deny, Delay;
        public int Requests;
        public readonly List<Action<int[]>> Callbacks = new List<Action<int[]>>();
        public readonly List<int[]> Results = new List<int[]>();

        public Paths(WorldData world) { this.world = world; }

        public void RequestPath(int start, int goal, bool flying, Action<int[]> completed)
        {
            Requests++;
            int[] result = Deny ? null : Find(start, goal, flying);
            if (Delay) { Callbacks.Add(completed); Results.Add(result); }
            else completed(result);
        }

        int[] Find(int start, int goal, bool flying)
        {
            var previous = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0 && !previous.ContainsKey(goal))
            {
                int tile = queue.Dequeue();
                for (int direction = 0; direction < 4; direction++)
                {
                    int next = world.SphereNeighbor(tile, direction);
                    if (previous.ContainsKey(next) || !FlatWildlifeSimulation.CanStep(world, tile, next, flying)) continue;
                    previous[next] = tile;
                    queue.Enqueue(next);
                }
            }
            if (!previous.ContainsKey(goal)) return null;
            var route = new List<int>();
            for (int tile = goal; tile >= 0; tile = previous[tile]) route.Add(tile);
            route.Reverse();
            return route.ToArray();
        }
    }

    sealed class ThrottledPaths : IWildlifePathfinder
    {
        sealed class Pending
        {
            public int Start, Goal;
            public bool Flying;
            public Action<int[]> Complete;
        }

        readonly Paths solver;
        readonly Queue<Pending> queue = new Queue<Pending>();
        public bool Blocked;
        public int Requests, Completed, Rejected, MaximumQueue;
        public int Queued => queue.Count;
        public bool CanRequestPath => !Blocked && queue.Count < 32;

        public ThrottledPaths(WorldData world) { solver = new Paths(world); }

        public void RequestPath(int start, int goal, bool flying, Action<int[]> completed)
        {
            Requests++;
            if (!CanRequestPath)
            {
                Rejected++;
                completed(Array.Empty<int>());
                return;
            }
            queue.Enqueue(new Pending { Start = start, Goal = goal, Flying = flying, Complete = completed });
            MaximumQueue = Math.Max(MaximumQueue, queue.Count);
        }

        public void CompleteFrame()
        {
            for (int budget = 0; budget < 2 && queue.Count > 0; budget++)
            {
                var pending = queue.Dequeue();
                solver.RequestPath(pending.Start, pending.Goal, pending.Flying, pending.Complete);
                Completed++;
            }
        }
    }

    static void CapacityAndColdHabitat()
    {
        var world = Habitat();
        int home = world.Index(32, 16);
        var wanderer = Actor(world, home);
        wanderer.Hunger = wanderer.Fatigue = .1f;
        var hungry = Actor(world, home);
        hungry.Hunger = .9f;
        var tired = Actor(world, home);
        tired.Fatigue = .9f;
        var resumed = Actor(world, home, WildlifeState.Wandering);
        resumed.GoalTile = world.Index(33, 16);
        uint random = wanderer.RandomState;
        var blocked = new ThrottledPaths(world) { Blocked = true };
        using (var simulation = new FlatWildlifeSimulation(world, blocked))
        {
            for (int i = 0; i < 8; i++) simulation.Tick(.1f, .5);
            Check(blocked.Requests == 0 && blocked.Rejected == 0, "capacity wait avoids requests and failed-route retries");
            Check(wanderer.RandomState == random && wanderer.State == WildlifeState.Idle,
                "capacity wait avoids habitat search and preserves wandering randomness");
            Check(hungry.State == WildlifeState.Eating && tired.State == WildlifeState.Sleeping,
                "local food and rest transitions continue when path capacity is full");
            Check(resumed.State == WildlifeState.Wandering && resumed.GoalTile == world.Index(33, 16) && resumed.ResumeTravel,
                "capacity wait preserves loaded travel destination");
            blocked.Blocked = false;
            for (int i = 0; i < 5; i++) simulation.Tick(.1f, .5);
            Check(blocked.Requests >= 2, "released capacity resumes deferred route requests");
        }

        var cold = Habitat();
        for (int i = 0; i < cold.Count; i++)
        {
            cold.Climates[i] = Koppen.Dfd;
            cold.Biomes[i] = Biome.Taiga;
            cold.Facts.MeanTemperature[i] = -16;
            cold.Facts.Vegetation[i] = VegetationCover.BorealForest;
            for (int month = 0; month < 12; month++) cold.Temperature[i * 12 + month] = month == 6 ? 14 : -19;
        }
        using (var simulation = new FlatWildlifeSimulation(cold, new Paths(cold)))
        {
            Check(cold.Wildlife.Count == 16, "cold boreal habitat seeds suitable rabbit and deer species");
            foreach (var resident in cold.Wildlife)
                Check(resident.Species == WildlifeSpecies.Rabbit || resident.Species == WildlifeSpecies.Deer,
                    "cold boreal site selects climate-compatible species");
        }

        var legacy = Habitat();
        for (int i = 0; i < legacy.Count; i++)
        {
            legacy.Elevation[i] = legacy.WaterLevel[i] = 2800;
            legacy.Climates[i] = Koppen.Dfc;
            legacy.Biomes[i] = Biome.Snow;
            legacy.Facts.MeanTemperature[i] = -8;
            legacy.Facts.Vegetation[i] = VegetationCover.BorealForest;
        }
        using (var simulation = new FlatWildlifeSimulation(legacy, new Paths(legacy)))
            Check(legacy.Wildlife.Count == 16, "legacy snow label retains derived highland forest habitat");
    }

    static void OnlyFoodGoalRecovers()
    {
        var world = Habitat();
        for (int tile = 0; tile < world.Count; tile++) world.Facts.Vegetation[tile] = VegetationCover.Bare;
        int food = world.Index(21, 16);
        world.Facts.Vegetation[food] = VegetationCover.Grassland;
        var actor = Actor(world, world.Index(20, 16));
        actor.Hunger = .9f;
        var paths = new Paths(world) { Deny = true };
        using (var simulation = new FlatWildlifeSimulation(world, paths))
        {
            for (int frame = 0; frame < 4; frame++) simulation.Tick(.25f, .5);
            Check(paths.Requests == 1 && actor.FailedGoal == food, "single food target first records a transient path failure");
            paths.Deny = false;
            for (int frame = 0; frame < 400; frame++) simulation.Tick(.25f, .5);
            Check(paths.Requests >= 2 && FlatWildlifeSimulation.Tile(world, actor) == food && actor.Hunger < .65f,
                "only food target retries after temporary path denial and satisfies hunger");
        }
    }

    static void RealPathWaiting()
    {
        var world = Habitat();
        int home = world.Index(32, 16);
        world.Facts.Vegetation[home] = VegetationCover.Bare;
        var actor = Actor(world, home);
        actor.Hunger = .9f;
        var paths = new ThrottledPaths(world);
        using (var simulation = new FlatWildlifeSimulation(world, paths))
        {
            for (int frame = 0; frame < 450; frame++) simulation.Tick(20f / 60, .5, 1f / 60);
            Check(paths.Requests == 1 && actor.PathPending && actor.RequestAge > 7 && actor.RequestAge < 8,
                "20x speed keeps a pending route alive for eight real seconds");
            byte[] before = FlatSave.Encode(world);
            float wait = actor.RequestAge;
            simulation.Tick(0, .5, 50);
            Check(actor.RequestAge == wait && Convert.ToBase64String(before) == Convert.ToBase64String(FlatSave.Encode(world)),
                "three-argument pause freezes real route waiting and persistent data");
            for (int frame = 0; frame < 60; frame++) simulation.Tick(20f / 60, .5, 1f / 60);
            Check(paths.Requests >= 2, "genuinely expired real-time route still retries");
        }

        var slowWorld = Habitat();
        var slowActor = Actor(slowWorld, slowWorld.Index(32, 16), WildlifeState.Wandering);
        slowActor.GoalTile = slowWorld.Index(33, 16);
        var slowPaths = new ThrottledPaths(slowWorld);
        using (var simulation = new FlatWildlifeSimulation(slowWorld, slowPaths))
        {
            for (int frame = 0; frame < 7; frame++) simulation.Tick(20, .5, 1);
            Check(slowPaths.Requests == 1 && slowActor.RequestAge > 6 && slowActor.RequestAge < 8,
                "catch-up budget preserves elapsed real route waiting at one frame per second");
            for (int frame = 0; frame < 2; frame++) simulation.Tick(20, .5, 1);
            Check(slowActor.FailedGoal == slowActor.GoalTile && !slowActor.PathPending && slowActor.RequestAge >= 8,
                "catch-up backlog does not postpone an actual real-time timeout");
        }
    }

    static void PathPressure(int population, int framesPerSecond)
    {
        var world = Habitat();
        for (int i = 0; i < population; i++)
        {
            var actor = Actor(world, world.Index(32, 16));
            actor.Species = WildlifeSpecies.Bird;
            actor.Hunger = actor.Fatigue = 0;
            actor.RandomState += (uint)i;
        }
        var paths = new ThrottledPaths(world);
        var moved = new bool[population];
        int movedCount = 0, peakMoving = 0;
        // Cover two full queue rotations while food/rest needs and staggered readiness keep changing.
        int frames = population + framesPerSecond * 10;
        float realStep = 1f / framesPerSecond;
        using (var simulation = new FlatWildlifeSimulation(world, paths))
        {
            for (int frame = 0; frame < frames; frame++)
            {
                simulation.Tick(realStep * 20, .5, realStep);
                paths.CompleteFrame();
                Check(paths.Queued <= 32 && paths.Rejected == 0, "20x provider capacity is respected before submitting work");
                int moving = 0;
                foreach (var actor in world.Wildlife)
                {
                    Check(actor.FailedGoal < 0, "healthy queued routes do not expire or produce duplicate retries at 20x");
                    if (actor.MoveSpeed > 0) moving++;
                    if (!moved[actor.Id - 1] && (actor.X != 32.5 || actor.Y != 16.5))
                    {
                        moved[actor.Id - 1] = true;
                        movedCount++;
                    }
                }
                peakMoving = Math.Max(peakMoving, moving);
            }
            Console.WriteLine("20x wildlife " + population + " actors at " + framesPerSecond + " FPS: requests=" + paths.Requests +
                " completed=" + paths.Completed + " queue=" + paths.Queued + " peakQueue=" + paths.MaximumQueue + " moved=" + movedCount + " peakMoving=" + peakMoving);
            Check(movedCount == population, "round-robin capacity admission eventually moves every actor identity");
            Check(paths.MaximumQueue <= 32 && paths.Requests <= paths.Completed + 32 && peakMoving > 0,
                "20x path throughput stays bounded and animals continue moving");
        }
    }

    static void SeedAndClone()
    {
        var a = Habitat();
        var b = Habitat();
        using (var first = new FlatWildlifeSimulation(a, new Paths(a)))
        using (var second = new FlatWildlifeSimulation(b, new Paths(b)))
        {
            Check(a.WildlifeInitialized && a.Wildlife.Count == 16, "bounded initial population");
            Check(a.Wildlife.Count == b.Wildlife.Count, "deterministic population count");
            var ids = new HashSet<int>();
            for (int i = 0; i < a.Wildlife.Count; i++)
            {
                var actor = a.Wildlife[i];
                var other = b.Wildlife[i];
                Check(ids.Add(actor.Id) && actor.Id > 0, "unique persistent identities");
                Check(actor.X == other.X && actor.Y == other.Y && actor.Species == other.Species && actor.RandomState == other.RandomState,
                    "deterministic species and positions");
                Check(FlatWildlifeSimulation.IsGroundTile(a, FlatWildlifeSimulation.Tile(a, actor)), "valid initial habitat");
            }
            var clone = a.Clone();
            Check(clone.WildlifeInitialized && clone.Wildlife.Count == a.Wildlife.Count, "clone actor population");
            Check(!ReferenceEquals(clone.Wildlife[0], a.Wildlife[0]), "clone deep-copies actors");
            clone.Wildlife[0].Hunger = .99f;
            Check(a.Wildlife[0].Hunger != .99f, "clone needs are isolated");
        }
        var ice = Habitat();
        for (int i = 0; i < ice.Count; i++) ice.Facts.Vegetation[i] = VegetationCover.Ice;
        using (var simulation = new FlatWildlifeSimulation(ice, new Paths(ice)))
            Check(ice.WildlifeInitialized && ice.Wildlife.Count == 0, "ice has no unsuitable spawns");
    }

    static void ClimateAndSeason()
    {
        var world = Habitat();
        int tile = world.Index(32, 16);
        world.Climates[tile] = Koppen.BWh;
        world.Facts.MeanTemperature[tile] = 42;
        world.Facts.Wetness[tile] = .01f;
        world.Facts.AnnualRain[tile] = 30;
        foreach (WildlifeSpecies species in Enum.GetValues(typeof(WildlifeSpecies)))
            Check(FlatWildlifeSimulation.HabitatSuitability(world, tile, species) == 0,
                "extreme hot arid climate cannot gain random animals from a stale green cover");

        world.Climates[tile] = Koppen.BSk;
        world.Facts.MeanTemperature[tile] = 10;
        world.Facts.Wetness[tile] = .08f;
        world.Facts.AnnualRain[tile] = 180;
        world.Facts.Vegetation[tile] = VegetationCover.DrySteppe;
        Check(FlatWildlifeSimulation.HabitatSuitability(world, tile, WildlifeSpecies.Rabbit) > 0,
            "dry open steppe supports adapted rabbits");
        Check(FlatWildlifeSimulation.HabitatSuitability(world, tile, WildlifeSpecies.Deer) == 0 &&
            FlatWildlifeSimulation.HabitatSuitability(world, tile, WildlifeSpecies.Butterfly) == 0,
            "arid steppe excludes moisture-dependent deer and butterflies");

        world.Climates[tile] = Koppen.Cfb;
        world.Facts.MeanTemperature[tile] = 15;
        world.Facts.Wetness[tile] = .65f;
        world.Facts.AnnualRain[tile] = 1000;
        world.Facts.Vegetation[tile] = VegetationCover.DeciduousForest;
        Check(FlatWildlifeSimulation.HabitatSuitability(world, tile, WildlifeSpecies.Deer) >
            FlatWildlifeSimulation.HabitatSuitability(world, tile, WildlifeSpecies.Rabbit), "deer prefer moist forest to rabbit habitat");
        world.Facts.Vegetation[tile] = VegetationCover.Marsh;
        Check(FlatWildlifeSimulation.HabitatSuitability(world, tile, WildlifeSpecies.Bird) > 0 &&
            FlatWildlifeSimulation.HabitatSuitability(world, tile, WildlifeSpecies.Rabbit) == 0,
            "wetland birds and rabbits use distinct vegetation habitats");

        var hot = Habitat();
        for (int i = 0; i < hot.Count; i++)
        {
            hot.Climates[i] = Koppen.BWh;
            hot.Facts.MeanTemperature[i] = 42;
            hot.Facts.Wetness[i] = .01f;
            hot.Facts.AnnualRain[i] = 30;
        }
        using (var simulation = new FlatWildlifeSimulation(hot, new Paths(hot)))
            Check(hot.Wildlife.Count == 0, "unsuitable hot arid world remains empty despite stale green cover");

        var tropical = Habitat();
        for (int i = 0; i < tropical.Count; i++)
        {
            tropical.Climates[i] = Koppen.Af;
            tropical.Facts.MeanTemperature[i] = 26;
            tropical.Facts.Wetness[i] = .8f;
            tropical.Facts.AnnualRain[i] = 2100;
            tropical.Facts.Vegetation[i] = VegetationCover.TropicalRainforest;
        }
        using (var simulation = new FlatWildlifeSimulation(tropical, new Paths(tropical)))
        {
            Check(tropical.Wildlife.Count == 16, "humid tropical forest retains suitable wildlife");
            foreach (var resident in tropical.Wildlife)
                Check(resident.Species != WildlifeSpecies.Rabbit && FlatWildlifeSimulation.HabitatSuitability(tropical,
                    FlatWildlifeSimulation.Tile(tropical, resident), resident.Species) > 0, "weighted tropical population uses suitable species");
        }

        var seasonal = Habitat();
        float[] temperatures = { -4, -3, 4, 11, 18, 23, 24, 22, 16, 9, 2, -3 };
        for (int i = 0; i < seasonal.Count; i++)
        {
            seasonal.Facts.MeanTemperature[i] = 119f / 12;
            for (int month = 0; month < 12; month++) seasonal.Temperature[i * 12 + month] = temperatures[month];
        }
        var butterfly = Actor(seasonal, seasonal.Index(32, 16));
        butterfly.Species = WildlifeSpecies.Butterfly;
        butterfly.Hunger = .2f;
        int identity = butterfly.Id;
        Check(FlatWildlifeSimulation.SeasonalActivity(seasonal, butterfly, 15.5) == 0 &&
            FlatWildlifeSimulation.SeasonalActivity(seasonal, butterfly, 195.5) > .7f,
            "local monthly temperature drives cold-season butterfly activity");
        var paths = new Paths(seasonal);
        using (var simulation = new FlatWildlifeSimulation(seasonal, paths))
        {
            for (int i = 0; i < 80; i++) simulation.Tick(.25f, 15.5);
            Check(butterfly.State == WildlifeState.Sleeping && butterfly.MoveSpeed == 0 && paths.Requests == 0,
                "cold-season butterfly rests with folded wings without churning paths");
            Check(butterfly.Hunger < .21f, "cold-season torpor slows butterfly food demand");
            byte[] paused = FlatSave.Encode(seasonal);
            simulation.Tick(0, 195.5);
            Check(Convert.ToBase64String(paused) == Convert.ToBase64String(FlatSave.Encode(seasonal)),
                "paused season change leaves animal state and animation frozen");
            for (int i = 0; i < 80; i++) simulation.Tick(.25f, 195.5);
            Check(paths.Requests > 0 && butterfly.State != WildlifeState.Sleeping &&
                (butterfly.X != 32.5 || butterfly.Y != 16.5), "warm season wakes butterfly and resumes actual path movement");
            Check(butterfly.Id == identity && butterfly.Species == WildlifeSpecies.Butterfly && seasonal.Wildlife.Count == 1,
                "season changes preserve existing animal identities and species");
        }

        var flyingWorld = Habitat();
        int water = flyingWorld.Index(31, 16);
        flyingWorld.Water[water] = WaterKind.Lake;
        flyingWorld.Facts.ShoreDistance[water] = .5f;
        for (int i = 0; i < flyingWorld.Count; i++) flyingWorld.Temperature[i * 12] = flyingWorld.Temperature[i * 12 + 1] = -4;
        var airborne = Actor(flyingWorld, water, WildlifeState.Wandering);
        airborne.Species = WildlifeSpecies.Butterfly;
        airborne.GoalTile = flyingWorld.Index(35, 16);
        var returnPaths = new Paths(flyingWorld);
        using (var simulation = new FlatWildlifeSimulation(flyingWorld, returnPaths))
        {
            for (int i = 0; i < 200; i++) simulation.Tick(.25f, 15.5);
            Check(returnPaths.Requests > 0 && airborne.State == WildlifeState.Sleeping &&
                FlatWildlifeSimulation.IsGroundTile(flyingWorld, FlatWildlifeSimulation.Tile(flyingWorld, airborne)),
                "cold airborne butterfly requests a local feasible landing before resting");
        }
    }

    static void AtlasPopulation()
    {
        var world = FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"), "ORBIS-2D-2026", default);
        var species = new int[5];
        using (var simulation = new FlatWildlifeSimulation(world, new Paths(world)))
        {
            Check(world.Wildlife.Count == Math.Min(512, Math.Max(16, world.Count / 512)), "default atlas reaches bounded population target");
            foreach (var actor in world.Wildlife)
            {
                int tile = FlatWildlifeSimulation.Tile(world, actor);
                species[(int)actor.Species]++;
                Check(FlatWildlifeSimulation.IsGroundTile(world, tile), "default atlas spawns on dry walkable ground");
                Check(FlatWildlifeSimulation.HabitatSuitability(world, tile, actor.Species) > 0, "default atlas species matches actual climate and moisture");
                Check(world.Facts.Vegetation[tile] != VegetationCover.Bare && world.Facts.Vegetation[tile] != VegetationCover.Ice,
                    "default atlas spawns match vegetation");
                if (actor.Species == WildlifeSpecies.Butterfly) Check(world.Facts.MeanTemperature[tile] >= 8, "default atlas butterfly climate");
            }
            for (int i = 1; i <= 4; i++) Check(species[i] > 0, "default atlas includes species " + (WildlifeSpecies)i);
            FlatSave.Validate(world);
            Console.WriteLine("Default atlas wildlife " + world.Wildlife.Count + " rabbits=" + species[1] + " deer=" + species[2] + " birds=" + species[3] + " butterflies=" + species[4]);
        }
    }

    static void BehaviorAndPause()
    {
        var world = Habitat();
        int home = world.Index(31, 16);
        world.Facts.Vegetation[home] = VegetationCover.Bare;
        var actor = Actor(world, home);
        actor.Hunger = .92f;
        var paths = new Paths(world);
        var visited = new HashSet<WildlifeState>();
        bool ate = false, rested = false, moved = false;
        using (var simulation = new FlatWildlifeSimulation(world, paths))
        {
            for (int step = 0; step < 6000; step++)
            {
                float beforeHunger = actor.Hunger, beforeFatigue = actor.Fatigue;
                double beforeX = actor.X, beforeY = actor.Y;
                simulation.Tick(.25f, step / 480.0);
                visited.Add(actor.State);
                ate |= actor.Hunger < beforeHunger;
                rested |= actor.Fatigue < beforeFatigue;
                moved |= actor.X != beforeX || actor.Y != beforeY;
                Check(actor.Hunger >= 0 && actor.Hunger <= 1 && actor.Fatigue >= 0 && actor.Fatigue <= 1, "bounded needs");
                Check(actor.X >= 0 && actor.X < world.Width && actor.Y >= 0 && actor.Y < world.Height, "canonical actor coordinates");
                Check(FlatWildlifeSimulation.IsGroundTile(world, FlatWildlifeSimulation.Tile(world, actor)), "ground actor stays dry");
            }
            Check(paths.Requests > 0 && moved, "movement follows requested paths");
            Check(ate && rested && visited.Contains(WildlifeState.Eating) && visited.Contains(WildlifeState.Sleeping), "food and rest satisfy needs");
            Check(visited.Contains(WildlifeState.Wandering) && visited.Contains(WildlifeState.SeekingFood), "wandering and feeding decisions");
            Check(world.Day == 0 && world.Nations.Count == 0 && world.Population[home] == 0 && world.Fertility[home] == .6f,
                "wildlife preserves civilization and terrain authority");
            byte[] before = FlatSave.Encode(world);
            simulation.Tick(0, 400);
            Check(Convert.ToBase64String(before) == Convert.ToBase64String(FlatSave.Encode(world)), "pause freezes persistent data and animation clock");
        }
    }

    static void FailedAndStalePaths()
    {
        var world = Habitat();
        int home = world.Index(31, 16);
        world.Facts.Vegetation[home] = VegetationCover.Bare;
        var actor = Actor(world, home);
        actor.Hunger = .95f;
        var denied = new Paths(world) { Deny = true };
        using (var simulation = new FlatWildlifeSimulation(world, denied))
        {
            for (int i = 0; i < 400; i++) simulation.Tick(.25f, .25);
            Check(actor.X == 31.5 && actor.Y == 16.5, "denied paths never teleport");
            Check(denied.Requests > 1 && denied.Requests < 45 && actor.MoveSpeed == 0, "denied paths use finite retry cooldown");
        }

        var delayed = new Paths(world) { Delay = true };
        var live = new FlatWildlifeSimulation(world, delayed);
        for (int i = 0; i < 12 && delayed.Callbacks.Count == 0; i++) live.Tick(.25f, .25);
        Check(delayed.Callbacks.Count > 0, "asynchronous route requested");
        delayed.Callbacks[0](delayed.Results[0]);
        byte[] paused = FlatSave.Encode(world);
        live.Tick(0, .25);
        Check(Convert.ToBase64String(paused) == Convert.ToBase64String(FlatSave.Encode(world)), "pause defers asynchronous route application");
        live.Dispose();
        delayed.Callbacks[0](delayed.Results[0]);
        live.Tick(4, .25);
        Check(Convert.ToBase64String(paused) == Convert.ToBase64String(FlatSave.Encode(world)), "disposed simulation rejects stale callbacks");

        var timeoutWorld = Habitat();
        int start = timeoutWorld.Index(31, 16);
        timeoutWorld.Facts.Vegetation[start] = VegetationCover.Bare;
        var waiting = Actor(timeoutWorld, start);
        waiting.Hunger = .9f;
        var timeoutPaths = new Paths(timeoutWorld) { Delay = true };
        using (var simulation = new FlatWildlifeSimulation(timeoutWorld, timeoutPaths))
        {
            for (int i = 0; i < 120 && timeoutPaths.Callbacks.Count < 2; i++) simulation.Tick(.25f, .5);
            Check(timeoutPaths.Callbacks.Count >= 2, "missing callbacks time out and retry");
            timeoutPaths.Callbacks[0](timeoutPaths.Results[0]);
            simulation.Tick(.25f, .5);
            Check(waiting.X == 31.5 && waiting.Y == 16.5 && waiting.MoveSpeed == 0, "timed-out callback cannot replace newer pending path");
            int[] latest = timeoutPaths.Results[timeoutPaths.Results.Count - 1];
            timeoutWorld.Buildings[latest[latest.Length - 1]] = Structure.Farm;
            timeoutPaths.Callbacks[timeoutPaths.Callbacks.Count - 1](latest);
            simulation.Tick(.25f, .5);
            Check(waiting.X == 31.5 && waiting.Y == 16.5 && waiting.MoveSpeed == 0, "completed route revalidates edited terrain");
        }
    }

    static void HabitatChangesAndLocalNight()
    {
        var world = Habitat();
        var actor = Actor(world, world.Index(32, 16), WildlifeState.Eating);
        actor.Hunger = .8f;
        using (var simulation = new FlatWildlifeSimulation(world, new Paths(world)))
        {
            world.Facts.Vegetation[actor.HomeTile] = VegetationCover.Bare;
            simulation.Tick(.1f, .5);
            Check(actor.Hunger >= .8f && actor.State != WildlifeState.Eating, "removed food cannot replenish hunger for an extra tick");
        }

        var nightWorld = Habitat();
        var nightActor = Actor(nightWorld, nightWorld.Index(32, 16));
        var dayWorld = Habitat();
        var dayActor = Actor(dayWorld, dayWorld.Index(0, 16));
        using (var nightSimulation = new FlatWildlifeSimulation(nightWorld, new Paths(nightWorld)))
        using (var daySimulation = new FlatWildlifeSimulation(dayWorld, new Paths(dayWorld)))
        {
            for (int i = 0; i < 8; i++)
            {
                nightSimulation.Tick(.25f, 0);
                daySimulation.Tick(.25f, 0);
            }
            Check(nightActor.State == WildlifeState.Sleeping && dayActor.State != WildlifeState.Sleeping,
                "longitude and calendar phase determine local sleep schedule");
        }

        var crowded = Habitat();
        for (int i = 0; i < 16; i++)
        {
            var traveller = Actor(crowded, crowded.Index(20, 16), WildlifeState.Wandering);
            traveller.GoalTile = crowded.Index(21, 16);
        }
        using (var simulation = new FlatWildlifeSimulation(crowded, new Paths(crowded)))
        {
            for (int i = 0; i < 6; i++) simulation.Tick(.25f, .5);
            foreach (var traveller in crowded.Wildlife)
                Check(traveller.State == WildlifeState.Wandering && traveller.GoalTile == crowded.Index(21, 16),
                    "route request budget preserves deferred actor state and goal");
        }

        var deciding = Habitat();
        for (int i = 0; i < 16; i++)
        {
            var idle = Actor(deciding, deciding.Index(20, 16));
            idle.Id = 1 + i * 13;
        }
        using (var simulation = new FlatWildlifeSimulation(deciding, new Paths(deciding)))
        {
            for (int i = 0; i < 6; i++) simulation.Tick(.25f, .5);
            foreach (var traveller in deciding.Wildlife)
                Check(traveller.State == WildlifeState.Wandering && traveller.GoalTile != traveller.HomeTile,
                    "saturated idle decisions preserve newly chosen destinations");
        }
    }

    static void FlyingAndPhysicalSpeed()
    {
        foreach (WildlifeSpecies species in new[] { WildlifeSpecies.Bird, WildlifeSpecies.Butterfly })
        {
            var world = Habitat();
            int start = world.Index(30, 16), water = world.Index(31, 16), goal = world.Index(32, 16);
            world.Water[water] = WaterKind.Lake;
            world.Facts.ShoreDistance[water] = .5f;
            var actor = Actor(world, start, WildlifeState.Wandering);
            actor.Species = species;
            actor.GoalTile = goal;
            actor.Fatigue = .9f;
            bool crossedWater = false, slept = false;
            using (var simulation = new FlatWildlifeSimulation(world, new Paths(world)))
            {
                for (int i = 0; i < 1600; i++)
                {
                    simulation.Tick(.25f, 0);
                    int tile = FlatWildlifeSimulation.Tile(world, actor);
                    crossedWater |= tile == water;
                    if (actor.State == WildlifeState.Sleeping || actor.State == WildlifeState.Eating)
                        Check(FlatWildlifeSimulation.IsGroundTile(world, tile), "flying actor lands in safe habitat before feeding or sleeping");
                    slept |= actor.State == WildlifeState.Sleeping;
                }
                Check(crossedWater && slept, species + " can fly across water and rest on land");
            }
        }

        var equator = Habitat();
        var polar = Habitat();
        var equatorialActor = Actor(equator, equator.Index(20, 16), WildlifeState.Wandering);
        var polarActor = Actor(polar, polar.Index(20, 2), WildlifeState.Wandering);
        equatorialActor.GoalTile = equator.Index(21, 16);
        polarActor.GoalTile = polar.Index(21, 2);
        using (var equatorialSimulation = new FlatWildlifeSimulation(equator, new Paths(equator)))
        using (var polarSimulation = new FlatWildlifeSimulation(polar, new Paths(polar)))
        {
            for (int i = 0; i < 4; i++)
            {
                equatorialSimulation.Tick(.25f, .5);
                polarSimulation.Tick(.25f, .5);
            }
            double equatorialDistance = (equatorialActor.X - 20.5) * Math.Cos((16.5 / equator.Height - .5) * Math.PI);
            double polarDistance = (polarActor.X - 20.5) * Math.Cos((2.5 / polar.Height - .5) * Math.PI);
            Check(equatorialDistance > 0 && Math.Abs(equatorialDistance - polarDistance) < 1e-8,
                "longitude movement preserves physical speed at high latitude");
        }
    }

    static void TopologyAndTerrainChanges()
    {
        var world = Habitat();
        int seam = world.Index(63, 16), across = world.Index(0, 16), pole = world.Index(8, 0);
        Check(FlatWildlifeSimulation.CanStep(world, seam, across, false), "ground date-line adjacency");
        Check(FlatWildlifeSimulation.CanStep(world, pole, world.SphereNeighbor(pole, 2), false), "folded pole adjacency");
        Check(!FlatWildlifeSimulation.CanStep(world, seam, world.Index(20, 16), true), "flying also requires neighbor edges");
        world.Water[across] = WaterKind.Lake;
        Check(!FlatWildlifeSimulation.CanStep(world, seam, across, false) && FlatWildlifeSimulation.CanStep(world, seam, across, true), "flying crosses water while ground avoids it");
        world.Water[across] = WaterKind.Land;
        world.Buildings[across] = Structure.Farm;
        Check(!FlatWildlifeSimulation.CanStep(world, seam, across, false), "ground avoids buildings");
        world.Buildings[across] = Structure.None;
        world.Elevation[across] = 700;
        Check(!FlatWildlifeSimulation.CanStep(world, seam, across, false), "ground avoids cliffs");
        world.Elevation[across] = 100;

        int bend = world.Index(10, 10), next = world.Index(10, 11);
        world.Facts.HalfWidth[bend] = world.Facts.HalfWidth[next] = .05f;
        world.Facts.Connections[bend] = (1 << 1) | (1 << 3);
        world.Facts.Connections[next] = (1 << 1) | (1 << 2);
        world.Facts.FlowY[bend] = 1;
        world.Facts.FlowY[next] = -1;
        Check(FlatWildlifeSimulation.IsGroundTile(world, bend) && FlatWildlifeSimulation.IsGroundTile(world, next), "curved river leaves dry node centers");
        Check(!FlatWildlifeSimulation.CanStep(world, bend, next, false), "real river geometry blocks an otherwise dry edge");

        var actor = Actor(world, seam, WildlifeState.Wandering);
        actor.GoalTile = across;
        using (var simulation = new FlatWildlifeSimulation(world, new Paths(world)))
        {
            for (int i = 0; i < 100 && FlatWildlifeSimulation.Tile(world, actor) != across; i++) simulation.Tick(.25f, .25);
            Check(FlatWildlifeSimulation.Tile(world, actor) == across, "continuous date-line movement reaches wrapped tile");
            int current = FlatWildlifeSimulation.Tile(world, actor);
            world.Buildings[current] = Structure.Settlement;
            world.Revision++;
            double oldX = actor.X, oldY = actor.Y;
            simulation.Tick(.25f, .25);
            Check(actor.MoveSpeed == 0 || FlatWildlifeSimulation.IsGroundTile(world, FlatWildlifeSimulation.Tile(world, actor)), "terrain edit stops or safely recovers actor");
            Check(Math.Abs(FlatScrollMath.Shortest(actor.X - oldX, world.Width)) < 2 && Math.Abs(actor.Y - oldY) < 2,
                "terrain recovery stays local");
        }

        var polar = Habitat();
        var polarActor = Actor(polar, pole, WildlifeState.Wandering);
        polarActor.GoalTile = polar.SphereNeighbor(pole, 2);
        using (var simulation = new FlatWildlifeSimulation(polar, new Paths(polar)))
        {
            for (int i = 0; i < 160 && FlatWildlifeSimulation.Tile(polar, polarActor) != polarActor.GoalTile; i++)
            {
                simulation.Tick(.25f, .25);
                Check(polarActor.Y >= 0 && polarActor.Y < 1, "polar crossing follows pole instead of interpolating through world");
            }
            Check(FlatWildlifeSimulation.Tile(polar, polarActor) == polar.SphereNeighbor(pole, 2), "continuous folded pole movement");
        }
    }

    static byte[] Repack(byte[] body)
    {
        using (var output = new MemoryStream())
        using (var writer = new BinaryWriter(output))
        using (var sha = SHA256.Create())
        {
            writer.Write(Encoding.ASCII.GetBytes("ORBIS-FLAT-01"));
            writer.Write(body.Length);
            writer.Write(sha.ComputeHash(body));
            writer.Write(body);
            return output.ToArray();
        }
    }

    static void Invalid(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, message);
    }

    static void Persistence()
    {
        var world = Habitat();
        var actor = Actor(world, world.Index(27, 16), WildlifeState.SeekingFood);
        actor.GoalTile = world.Index(28, 16);
        actor.Hunger = .89f;
        actor.Fatigue = .67f;
        actor.Facing = -1;
        actor.AnimationTime = 21.5f;
        actor.MoveSpeed = .1f;
        byte[] encoded = FlatSave.Encode(world);
        var loaded = FlatSave.Decode(encoded);
        var other = loaded.Wildlife[0];
        Check(FlatSave.Version == 3 && loaded.WildlifeInitialized && loaded.Wildlife.Count == 1 && loaded.Cyclones == null,
            "v3 saves preserve wildlife and leave uninitialized weather ready for spin-up");
        Check(other.Id == actor.Id && other.Species == actor.Species && other.State == actor.State && other.HomeTile == actor.HomeTile && other.GoalTile == actor.GoalTile,
            "persistent identity, species, state and destination");
        Check(other.X == actor.X && other.Y == actor.Y && other.Hunger == actor.Hunger && other.Fatigue == actor.Fatigue && other.RandomState == actor.RandomState &&
            other.Facing == actor.Facing && other.AnimationTime == actor.AnimationTime && other.MoveSpeed == actor.MoveSpeed,
            "persistent position, needs, animation and randomness");
        var paths = new Paths(loaded);
        using (var simulation = new FlatWildlifeSimulation(loaded, paths))
        {
            for (int i = 0; i < 8; i++) simulation.Tick(.25f, .25);
            Check(loaded.Wildlife.Count == 1 && paths.Requests > 0, "loaded travelling actor safely replans without reseeding");
        }

        const int header = 13 + 4 + 32, actorBytes = 54;
        var version2Body = encoded.AsSpan(header).ToArray();
        version2Body[0] = 2;
        byte[] version2File = Repack(version2Body);
        // Captured from the v2 writer with the fixture above.
        Check(Convert.ToHexString(SHA256.HashData(version2File)) == "84D939FE5C88A7DBF22811B0C1A966D63F071AD1C12CDC34889B1EB420BDF1FB",
            "historical v2 fixture retains its original bytes");
        var version2 = FlatSave.Decode(version2File);
        Check(version2.Cyclones == null && encoded.AsSpan().SequenceEqual(FlatSave.Encode(version2)),
            "v2 load preserves every field and awaits cyclone initialization");
        new FlatSimulation(version2);
        Check(version2.Cyclones != null && version2.Wildlife.Count == 1, "v2 world starts cyclone simulation with the saved wildlife");
        var calm = FlatSave.Decode(FlatSave.Encode(version2)).Cyclones;
        Check(calm != null && !calm.North.Tracked && !calm.South.Tracked && calm.StepIndex == version2.Cyclones.StepIndex,
            "a calm initialized world retains its cyclone clock across saves");
        var legacyBody = new byte[encoded.Length - header - 5 - actorBytes];
        Array.Copy(encoded, header, legacyBody, 0, legacyBody.Length);
        Array.Copy(BitConverter.GetBytes(1), 0, legacyBody, 0, 4);
        var legacy = FlatSave.Decode(Repack(legacyBody));
        Check(!legacy.WildlifeInitialized && legacy.Wildlife.Count == 0 && legacy.Cyclones == null,
            "v1 loads with wildlife and cyclones ready for initialization");
        new FlatSimulation(legacy);
        Check(legacy.Cyclones != null, "v1 world starts cyclone simulation");
        using (var simulation = new FlatWildlifeSimulation(legacy, new Paths(legacy)))
            Check(legacy.Wildlife.Count > 0, "v1 world receives compatible wildlife initialization");

        var invalidBody = new byte[encoded.Length - header];
        Array.Copy(encoded, header, invalidBody, 0, invalidBody.Length);
        int actorOffset = invalidBody.Length - actorBytes;
        invalidBody[actorOffset + 12] = 0;
        Invalid(() => FlatSave.Decode(Repack(invalidBody)), "invalid species rejected even with valid checksum");
        Array.Copy(encoded, header, invalidBody, 0, invalidBody.Length);
        invalidBody[actorOffset + 13] = 6;
        Invalid(() => FlatSave.Decode(Repack(invalidBody)), "invalid state rejected even with valid checksum");
        Array.Copy(encoded, header, invalidBody, 0, invalidBody.Length);
        Array.Copy(BitConverter.GetBytes(double.NaN), 0, invalidBody, actorOffset + 14, 8);
        Invalid(() => FlatSave.Decode(Repack(invalidBody)), "nonfinite saved position rejected even with valid checksum");
        Array.Copy(encoded, header, invalidBody, 0, invalidBody.Length);
        invalidBody[actorOffset - 5] = 2;
        Invalid(() => FlatSave.Decode(Repack(invalidBody)), "invalid initialization flag rejected");
        Array.Copy(encoded, header, invalidBody, 0, invalidBody.Length);
        Array.Copy(BitConverter.GetBytes(513), 0, invalidBody, actorOffset - 4, 4);
        Invalid(() => FlatSave.Decode(Repack(invalidBody)), "oversized saved population rejected before allocation");
        actor.X = double.NaN;
        Invalid(() => FlatSave.Encode(world), "nonfinite actor coordinate rejected");
        actor.X = 27.5;
        world.Wildlife.Add(actor);
        Invalid(() => FlatSave.Encode(world), "duplicate actor identity rejected");
        world.Wildlife.RemoveAt(1);
        actor.Hunger = 2;
        Invalid(() => FlatSave.Encode(world), "out-of-range needs rejected");
        actor.Hunger = .3f;
        for (int i = 1; i <= 512; i++)
        {
            var extra = new WildlifeActor { Id = i + 1, Species = WildlifeSpecies.Bird, X = .5, Y = .5, Facing = 1, RandomState = 1 };
            world.Wildlife.Add(extra);
        }
        Invalid(() => FlatSave.Encode(world), "unbounded actor population rejected");
    }

    static void CyclonePersistence()
    {
        // Start with the same derived climate and moisture fields as a newly loaded world.
        var world = FlatSave.Decode(FlatSave.Encode(Habitat()));
        world.Day = 37.625;
        byte[] empty = FlatSave.Encode(world);
        var north = new FlatCycloneTrack
        {
            Tracked = true, U = .0025, V = .625, PressureDeficit = 26, WindSpeed = 31.5,
            Phase = 8.0625, AgeDays = 3, OceanCooling = .75
        };
        var previousNorth = north;
        previousNorth.U = .9975;
        previousNorth.Phase -= .5;
        previousNorth.AgeDays -= .25;
        var south = new FlatCycloneTrack
        {
            Tracked = true, U = .83, V = .39, PressureDeficit = 12, WindSpeed = 21.5,
            Phase = -2.75, AgeDays = 1.25, OceanCooling = .18
        };
        world.Cyclones = new FlatCycloneWeather
        {
            StepIndex = 150, North = north, PreviousNorth = previousNorth, South = south, PreviousSouth = default
        };
        byte[] saved = FlatSave.Encode(world);
        var loaded = FlatSave.Decode(saved);
        var copy = loaded.Cyclones;
        Check(copy != null && copy.StepIndex == 150 && copy.North.Equals(north) && copy.PreviousNorth.Equals(previousNorth)
            && copy.South.Equals(south) && copy.PreviousSouth.Equals(default(FlatCycloneTrack)),
            "v3 restores both hemisphere tracks and both previous interpolation endpoints");
        Check(saved.AsSpan().SequenceEqual(FlatSave.Encode(loaded)), "cyclone save round-trip is bit-for-bit identical");

        var continuous = new FlatSimulation(world);
        var resumed = new FlatSimulation(loaded);
        Check(saved.AsSpan().SequenceEqual(FlatSave.Encode(loaded)), "loading the simulation retains fractional-day track history");
        foreach (float days in new[] { .0625f, .1875f, 1.25f })
        {
            continuous.Step(days);
            resumed.Step(days);
            Check(FlatSave.Encode(world).AsSpan().SequenceEqual(FlatSave.Encode(loaded)),
                "saved cyclone evolution matches uninterrupted simulation through fixed-step boundaries");
        }

        const int header = 13 + 4 + 32, trackBytes = 1 + 7 * sizeof(double);
        int tail = empty.Length - header;
        var body = saved.AsSpan(header).ToArray();
        Check(body.Length - tail == sizeof(long) + 4 * trackBytes, "v3 appends only a fixed-size initialized cyclone state");
        void Damaged(int offset, byte[] replacement, string message)
        {
            var changed = (byte[])body.Clone();
            replacement.CopyTo(changed, offset);
            Invalid(() => FlatSave.Decode(Repack(changed)), message);
        }
        int firstTrack = tail + sizeof(long);
        Damaged(0, BitConverter.GetBytes(2), "v2 rejects an unexpected cyclone tail");
        Damaged(tail, BitConverter.GetBytes(149L), "cyclone tick must agree with the saved calendar");
        Damaged(firstTrack, new byte[] { 2 }, "noncanonical cyclone tracking flag rejected");
        Damaged(firstTrack + 1, BitConverter.GetBytes(1.0), "unwrapped cyclone longitude rejected");
        Damaged(firstTrack + 9, BitConverter.GetBytes(-.01), "out-of-range cyclone latitude rejected");
        Damaged(firstTrack + 17, BitConverter.GetBytes(999.0), "impossible cyclone pressure deficit rejected");
        Damaged(firstTrack + 25, BitConverter.GetBytes(double.PositiveInfinity), "nonfinite cyclone wind rejected");
        Damaged(firstTrack + 33, BitConverter.GetBytes(double.MaxValue), "unbounded cyclone phase rejected");
        Damaged(firstTrack + 41, BitConverter.GetBytes(-.1), "negative cyclone age rejected");
        Damaged(firstTrack + 3 * trackBytes + 49, BitConverter.GetBytes(double.NaN), "invalid cooling in inactive history rejected");
        Invalid(() => FlatSave.Decode(Repack(body.AsSpan(0, body.Length - 1).ToArray())), "truncated cyclone history rejected");
        var extra = new byte[body.Length + 1];
        body.CopyTo(extra, 0);
        Invalid(() => FlatSave.Decode(Repack(extra)), "oversized cyclone history rejected");
        world.Cyclones.PreviousNorth.OceanCooling = -1;
        Invalid(() => FlatSave.Encode(world), "save validation rejects invalid historical cyclone state before writing");
    }

    public static void Run()
    {
        SeedAndClone();
        ClimateAndSeason();
        CapacityAndColdHabitat();
        OnlyFoodGoalRecovers();
        RealPathWaiting();
        PathPressure(256, 60);
        PathPressure(512, 60);
        PathPressure(256, 5);
        PathPressure(512, 5);
        AtlasPopulation();
        BehaviorAndPause();
        FailedAndStalePaths();
        HabitatChangesAndLocalNight();
        FlyingAndPhysicalSpeed();
        TopologyAndTerrainChanges();
        Persistence();
        CyclonePersistence();
        Console.WriteLine("PASS wildlife actor AI, habitat paths, pause, topology, terrain changes, clone, v1/v2/v3 saves and cyclone resume (" + checks + " checks)");
    }
}
