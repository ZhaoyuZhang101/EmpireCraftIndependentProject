using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    public enum TectonicSetting : byte { Interior, Collision, Subduction, IslandArc, Rift, Ridge, Transform }
    public enum VolcanoKind : byte { Arc, Hotspot, Rift }

    [Flags]
    public enum GlebaTectonicProvince : ushort
    {
        None = 0,
        ContinentalCrust = 1 << 0,
        PassiveShelf = 1 << 1,
        ActiveTrench = 1 << 2,
        ForelandBasin = 1 << 3,
        TibetanPlateau = 1 << 4,
        PaleoOrogen = 1 << 5,
        LargeIgneousProvince = 1 << 6,
        MantleSuperswell = 1 << 7,
        CratonShield = 1 << 8,
        VolgaUpland = 1 << 9,
        RiftGraben = 1 << 10,
        BackArcBasin = 1 << 11,
        HawaiianChain = 1 << 12,
        IcelandicPlume = 1 << 13,
        RaisedOldSeabed = 1 << 14,
    }

    public readonly struct Volcano
    {
        public readonly int Tile; public readonly VolcanoKind Kind; public readonly float Activity;
        public Volcano(int tile, VolcanoKind kind, float activity) { Tile = tile; Kind = kind; Activity = activity; }
    }

    public sealed class TectonicPlate
    {
        public float AxisX, AxisY, AxisZ, Speed;
        public bool Continental;
        public int Tiles;
    }

    // Immutable record of how a generated bed was built. Volcano sites, plate
    // margins, microplates, geological provinces and seismic belts feed erosion,
    // Gleba bedrock/aquifer synthesis, and later eruption/earthquake events.
    public sealed class FlatTectonicFacts
    {
        public readonly TectonicPlate[] Plates;
        public readonly byte[] Plate;
        public readonly TectonicSetting[] Setting;
        public readonly float[] Seismicity;
        public readonly Volcano[] Volcanoes;
        public readonly ushort[] Microplate;
        public readonly int MicroplateCount;
        public readonly GlebaTectonicProvince[] Province;
        public readonly float[] RockResistance;

        internal FlatTectonicFacts(
            TectonicPlate[] plates,
            byte[] plate,
            TectonicSetting[] setting,
            float[] seismicity,
            Volcano[] volcanoes,
            ushort[] microplate = null,
            int microplateCount = 0,
            GlebaTectonicProvince[] province = null,
            float[] rockResistance = null)
        {
            Plates = plates;
            Plate = plate;
            Setting = setting;
            Seismicity = seismicity;
            Volcanoes = volcanoes;
            int n = plate != null ? plate.Length : 0;
            Microplate = microplate ?? new ushort[n];
            MicroplateCount = microplateCount > 0 ? microplateCount : (plates != null ? plates.Length : 1);
            Province = province ?? new GlebaTectonicProvince[n];
            RockResistance = rockResistance;
        }
    }

    // Gleba-inspired multi-stage planetary tectonic & bathymetric synthesis on the unit sphere:
    //   1. Macro-plates + sub-plate Microplates/Terranes + ancient Paleo-Plates (old_plates)
    //   2. Primary & Secondary supercontinent crust clustering + domain warping + speckle cleanup
    //   3. Microplate terrane provinces (alluvial basins, raised seabeds, Volga uplands, craton shields)
    //   4. Parsons-Sclater oceanic crust thermal aging & intra-microplate variable-width passive shelves (Stage 9)
    //   5. Asymmetric boundary stamper: Himalayan wall + flat-topped Tibetan plateau + foreland troughs,
    //      Andean + Laramide/Rockies paired cordilleras, discrete Island Arcs + back-arc seas,
    //      segmented Continental Rift shoulders & deep graben troughs, Mid-Ocean Ridges & trenches
    //   6. Intraplate Ural/Appalachian paleo-orogens, stepped LIP flood-basalt traps, Mantle Superswells,
    //      Hawaiian age-progressive volcano-atoll-guyot chains & Icelandic ridge plumes
    //   7. Stage 41 coastal trench cliff beveling + post-LGM sea-level submergence calibration (land_sunkeness).
    public static class FlatTectonics
    {
        struct Margin
        {
            public TectonicSetting Setting;
            public float Convergence, Shear;
            public bool OwnContinental, OtherContinental, Overriding;
        }

        struct LipSeed
        {
            public float X, Y, Z, CosOuter, RadiusRef, HeightM;
            public int Steps;
        }

        struct SwellSeed
        {
            public float X, Y, Z, RadiusRad, LandUpliftM, SeaUpliftM;
        }

        public static FlatTectonicFacts Build(WorldData w, CancellationToken cancel)
        {
            int n = w.Count, width = w.Width, height = w.Height;
            uint seed = FMath.Seed(w.Seed) ^ 0x7ec70a1cu;
            var preset = w.Preset;
            float scale = width / 512f;
            var parallel = FlatWorkerPolicy.Options(cancel);

            var px = new float[n];
            var py = new float[n];
            var pz = new float[n];
            var rowScale = new float[height];
            var capDistance = new float[n];

            // Polar ocean prior with tilted geological pole so high-latitude coasts do not follow parallels.
            const double polarCore = .02, polarEdge = .14;
            const double capTilt = (polarEdge - polarCore) * Math.PI * .5;
            double azimuth = 2 * Math.PI * FMath.Unit(seed ^ 0x36b52b1fu);
            float capX = (float)(Math.Sin(capTilt) * Math.Cos(azimuth));
            float capY = (float)(Math.Sin(capTilt) * Math.Sin(azimuth));
            float capZ = (float)Math.Cos(capTilt);

            Parallel.For(0, height, parallel, y =>
            {
                double lat = ((y + .5) / height - .5) * Math.PI;
                rowScale[y] = (float)Math.Cos(lat);
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    double lon = (x + .5) / width * 2 * Math.PI - Math.PI;
                    int i = row + x;
                    float vx = (float)(Math.Cos(lat) * Math.Cos(lon));
                    float vy = (float)(Math.Cos(lat) * Math.Sin(lon));
                    float vz = (float)Math.Sin(lat);
                    px[i] = vx; py[i] = vy; pz[i] = vz;
                    float dot = vx * capX + vy * capY + vz * capZ;
                    capDistance[i] = (float)(Math.Acos(Math.Min(1f, Math.Abs(dot))) / Math.PI);
                }
            });

            // --- Stage 1: Macro-Plates, Sub-Plate Microplates & Ancient Paleo-Plates ---
            int plateCount = preset == TerrainPreset.Archipelago ? 18 : preset == TerrainPreset.GreatContinent ? 11 : 14;
            var plates = new TectonicPlate[plateCount];
            var sx = new float[plateCount];
            var sy = new float[plateCount];
            var sz = new float[plateCount];

            // Primary ("Old World" / Pangaea) and Secondary ("New World" / Americas) clustering poles
            RandomUnit(seed ^ 0xa11ce5u, 0, out float superX, out float superY, out float superZ);
            if (Math.Abs(superZ) > .58f) { superZ *= .58f / Math.Abs(superZ); Normalize(ref superX, ref superY, ref superZ); }
            RandomUnit(seed ^ 0xb22df7u, 1, out float secX, out float secY, out float secZ);
            // Push secondary cluster roughly 105-145 deg from primary cluster so two distinct continental masses form on Continents
            float proj = secX * superX + secY * superY + secZ * superZ;
            secX -= (proj + .42f) * superX; secY -= (proj + .42f) * superY; secZ -= (proj + .42f) * superZ;
            if (Math.Abs(secZ) > .62f) secZ *= .62f / Math.Abs(secZ);
            Normalize(ref secX, ref secY, ref secZ);

            float avgSpeedScale = .88f + .32f * FMath.Unit(seed ^ 0x998877u);
            for (int k = 0; k < plateCount; k++)
            {
                RandomUnit(seed, k * 3, out sx[k], out sy[k], out sz[k]);
                if (Math.Abs(sz[k]) > .82f) { sz[k] *= .82f / Math.Abs(sz[k]); Normalize(ref sx[k], ref sy[k], ref sz[k]); }
                var p = new TectonicPlate();
                RandomUnit(seed ^ 0x51a7e0u, k, out p.AxisX, out p.AxisY, out p.AxisZ);
                p.Speed = (.42f + 1.15f * FMath.Unit(seed ^ (uint)(k * 977 + 31))) * avgSpeedScale;
                plates[k] = p;
            }

            var plateBias = new float[plateCount];
            for (int k = 0; k < plateCount; k++)
            {
                float roll = FMath.Unit(seed ^ (uint)(k * 7919 + 5));
                float d1 = sx[k] * superX + sy[k] * superY + sz[k] * superZ;
                float d2 = sx[k] * secX + sy[k] * secY + sz[k] * secZ;
                switch (preset)
                {
                    case TerrainPreset.GreatContinent:
                        plateBias[k] = d1 > .22f ? .24f : (d1 > -.10f && roll < .35f ? .06f : -.22f);
                        break;
                    case TerrainPreset.Archipelago:
                        plateBias[k] = roll < .24f ? .04f : -.09f;
                        break;
                    case TerrainPreset.InlandSea:
                        plateBias[k] = sx[k] < -.15f ? .10f : -.04f;
                        break;
                    default: // Continents: Gleba primary + secondary crust clustering
                        if (d1 > .32f) plateBias[k] = .18f;
                        else if (d2 > .38f) plateBias[k] = .15f;
                        else plateBias[k] = roll < .28f ? .05f : -.14f;
                        break;
                }
            }

            // 4 sub-microplate seeds per macro-plate (44-72 contiguous microplates strictly nested inside macro-plates)
            const int MicroPerPlate = 4;
            int microplateCount = plateCount * MicroPerPlate;
            var mx = new float[microplateCount];
            var my = new float[microplateCount];
            var mz = new float[microplateCount];
            var microTerraneKind = new byte[microplateCount]; // 0=AlluvialBasin, 1=RaisedSeabed, 2=VolgaUpland, 3=CratonShield
            var microShelfWidthRef = new float[microplateCount];
            for (int k = 0; k < plateCount; k++)
            {
                for (int sub = 0; sub < MicroPerPlate; sub++)
                {
                    int mid = k * MicroPerPlate + sub;
                    RandomUnit(seed ^ 0x3c6ef3u, mid * 5 + 1, out float ox, out float oy, out float oz);
                    mx[mid] = sx[k] + .55f * ox;
                    my[mid] = sy[k] + .55f * oy;
                    mz[mid] = sz[k] + .55f * oz;
                    Normalize(ref mx[mid], ref my[mid], ref mz[mid]);

                    float tr = FMath.Unit(seed ^ (uint)(mid * 12289 + 19));
                    microTerraneKind[mid] = tr < .44f ? (byte)0 : tr < .64f ? (byte)1 : tr < .84f ? (byte)2 : (byte)3;
                    // Gleba Stage 9 passive shelf width: calibrated so <200 m shelf covers ~7-8.5% of ocean area (Earth P10 ~ 954 m)
                    float shelfKm = 130f + 410f * FMath.Unit(seed ^ (uint)(mid * 19391 + 73));
                    if (preset == TerrainPreset.Archipelago) shelfKm *= .65f;
                    microShelfWidthRef[mid] = shelfKm / 78.18f;
                }
            }

            // Ancient paleo-plates (`generate_old_plates`) for intracontinental Ural/Appalachian sutures
            const int OldPlateCount = 9;
            var opx = new float[OldPlateCount];
            var opy = new float[OldPlateCount];
            var opz = new float[OldPlateCount];
            var oldPlateType = new byte[OldPlateCount * OldPlateCount]; // 0=none, 1=OldMountains (Ural), 2=OldHills (Caledonian)
            for (int k = 0; k < OldPlateCount; k++)
                RandomUnit(seed ^ 0x01d91a7eu, k * 7 + 3, out opx[k], out opy[k], out opz[k]);
            for (int a = 0; a < OldPlateCount; a++)
            for (int b = a + 1; b < OldPlateCount; b++)
            {
                float r = FMath.Unit(seed ^ (uint)(a * 313 + b * 997 + 41));
                byte kind = r < .34f ? (byte)1 : r < .62f ? (byte)2 : (byte)0;
                oldPlateType[a * OldPlateCount + b] = oldPlateType[b * OldPlateCount + a] = kind;
            }

            var plate = new byte[n];
            var microplate = new ushort[n];
            var oldPlate = new byte[n];
            var crust = new float[n];

            Parallel.For(0, height, parallel, y =>
            {
                cancel.ThrowIfCancellationRequested();
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    float vx = px[i], vy = py[i], vz = pz[i];

                    // Warped macro-plate Voronoi
                    float wx = vx + .34f * Fbm(vx * 2.05f, vy * 2.05f, vz * 2.05f, seed ^ 0x11u, 3);
                    float wy = vy + .34f * Fbm(vx * 2.05f, vy * 2.05f, vz * 2.05f, seed ^ 0x22u, 3);
                    float wz = vz + .34f * Fbm(vx * 2.05f, vy * 2.05f, vz * 2.05f, seed ^ 0x33u, 3);
                    int bestP = 0; float bestPD = -9f;
                    for (int k = 0; k < plateCount; k++)
                    {
                        float d = wx * sx[k] + wy * sy[k] + wz * sz[k];
                        if (d > bestPD) { bestPD = d; bestP = k; }
                    }
                    plate[i] = (byte)bestP;

                    // Sub-plate microplate/terrane Voronoi strictly nested within bestP
                    float mwx = vx + .26f * Fbm(vx * 3.8f, vy * 3.8f, vz * 3.8f, seed ^ 0x101u, 4);
                    float mwy = vy + .26f * Fbm(vx * 3.8f, vy * 3.8f, vz * 3.8f, seed ^ 0x202u, 4);
                    float mwz = vz + .26f * Fbm(vx * 3.8f, vy * 3.8f, vz * 3.8f, seed ^ 0x303u, 4);
                    int bestM = bestP * MicroPerPlate; float bestMD = -9f;
                    for (int sub = 0; sub < MicroPerPlate; sub++)
                    {
                        int mid = bestP * MicroPerPlate + sub;
                        float d = mwx * mx[mid] + mwy * my[mid] + mwz * mz[mid];
                        if (d > bestMD) { bestMD = d; bestM = mid; }
                    }
                    microplate[i] = (ushort)bestM;

                    // Ancient paleo-plate Voronoi
                    float owx = vx + .29f * Fbm(vx * 2.7f, vy * 2.7f, vz * 2.7f, seed ^ 0x701u, 3);
                    float owy = vy + .29f * Fbm(vx * 2.7f, vy * 2.7f, vz * 2.7f, seed ^ 0x702u, 3);
                    float owz = vz + .29f * Fbm(vx * 2.7f, vy * 2.7f, vz * 2.7f, seed ^ 0x703u, 3);
                    int bestO = 0; float bestOD = -9f;
                    for (int k = 0; k < OldPlateCount; k++)
                    {
                        float d = owx * opx[k] + owy * opy[k] + owz * opz[k];
                        if (d > bestOD) { bestOD = d; bestO = k; }
                    }
                    oldPlate[i] = (byte)bestO;

                    // Domain-warped Gleba crust field (`crust_warping_strength` + primary/secondary clusters)
                    float cwx = vx + .28f * Fbm(vx * 2.3f, vy * 2.3f, vz * 2.3f, seed ^ 0x41u, 3);
                    float cwy = vy + .28f * Fbm(vy * 2.3f, vz * 2.3f, vx * 2.3f, seed ^ 0x42u, 3);
                    float cwz = vz + .28f * Fbm(vz * 2.3f, vx * 2.3f, vy * 2.3f, seed ^ 0x43u, 3);
                    Normalize(ref cwx, ref cwy, ref cwz);

                    float c = Fbm(cwx * 1.45f, cwy * 1.45f, cwz * 1.45f, seed ^ 0x44u, 5) * .52f
                            + Fbm(cwx * 3.9f, cwy * 3.9f, cwz * 3.9f, seed ^ 0x45u, 4) * .21f
                            + Fbm(vx * 8.2f, vy * 8.2f, vz * 8.2f, seed ^ 0x48u, 3) * .08f
                            + plateBias[bestP];

                    if (preset == TerrainPreset.Continents)
                    {
                        float cl1 = Smooth(-.15f, .65f, cwx * superX + cwy * superY + cwz * superZ);
                        float cl2 = Smooth(-.05f, .70f, cwx * secX + cwy * secY + cwz * secZ);
                        c += .14f * Math.Max(cl1, .85f * cl2) - .06f;
                    }
                    else if (preset == TerrainPreset.GreatContinent)
                    {
                        float cl1 = Smooth(-.20f, .62f, cwx * superX + cwy * superY + cwz * superZ);
                        c += .22f * cl1 - .08f;
                    }
                    else if (preset == TerrainPreset.Archipelago)
                    {
                        c = Fbm(cwx * 3.3f, cwy * 3.3f, cwz * 3.3f, seed ^ 0x46u, 5) * .56f
                          + Fbm(vx * 7.4f, vy * 7.4f, vz * 7.4f, seed ^ 0x49u, 3) * .16f
                          + plateBias[bestP];
                    }
                    else if (preset == TerrainPreset.InlandSea)
                    {
                        float r = vx + .40f * Fbm(vx * 2.2f, vy * 2.2f, vz * 2.2f, seed ^ 0x47u, 4);
                        c += r < .45f ? .24f : -.68f * (r - .45f) / .55f;
                    }

                    crust[i] = c - .56f * (1f - Smooth((float)polarCore, (float)polarEdge, capDistance[i]));
                }
            });

            // Guarantee every macro-plate owns at least its seed tile on ultra-coarse grids (64x32)
            for (int i = 0; i < n; i++) plates[plate[i]].Tiles++;
            for (int k = 0; k < plateCount; k++)
            {
                if (plates[k].Tiles > 0) continue;
                int anchor = TileOf(w, sx[k], sy[k], sz[k]);
                plates[plate[anchor]].Tiles--;
                plate[anchor] = (byte)k;
                microplate[anchor] = (ushort)(k * MicroPerPlate);
                plates[k].Tiles = 1;
            }

            float target = preset == TerrainPreset.Archipelago ? .12f : preset == TerrainPreset.GreatContinent ? .33f : preset == TerrainPreset.InlandSea ? .40f : .29f;
            // Slightly generous pre-sunkeness continental crust budget so shelves + coastal submergence reach target cleanly
            float crustTarget = Math.Min(.62f, target * 1.12f + .015f);
            float threshold = WeightedQuantile(crust, rowScale, width, 1f - crustTarget);
            for (int i = 0; i < n; i++) crust[i] -= threshold;

            // Gleba speckle cleanup ("Removig small oceanic crust pieces"): smooth isolated 1-tile crust pinholes inside continents
            var crustClean = new float[n];
            Array.Copy(crust, crustClean, n);
            for (int i = 0; i < n; i++)
            {
                if (crust[i] >= 0f || crust[i] < -.06f) continue;
                int posNb = 0; float sumNb = 0f;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(i, d);
                    if (m >= 0 && crust[m] > 0f) { posNb++; sumNb += crust[m]; }
                }
                if (posNb >= 3) crustClean[i] = .25f * sumNb;
            }
            crust = crustClean;

            var continentalTiles = new int[plateCount];
            for (int i = 0; i < n; i++) if (crust[i] > 0f) continentalTiles[plate[i]]++;
            for (int k = 0; k < plateCount; k++) plates[k].Continental = continentalTiles[k] * 2 > plates[k].Tiles;

            // --- Stage 2: Active Plate Margins & Geodesic Intra-Plate Reach ---
            var margin = new Margin[n];
            var source = new int[n];
            var distance = new float[n];
            var heap = new FloodHeap();
            for (int i = 0; i < n; i++) { source[i] = -1; distance[i] = float.MaxValue; }

            for (int i = 0; i < n; i++)
            {
                if ((i & 8191) == 0) cancel.ThrowIfCancellationRequested();
                int other = -1;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(i, d);
                    if (m >= 0 && plate[m] != plate[i]) { other = m; break; }
                }
                if (other < 0) continue;
                var a = plates[plate[i]];
                var b = plates[plate[other]];
                Velocity(a, px[i], py[i], pz[i], out float ax, out float ay, out float az);
                Velocity(b, px[i], py[i], pz[i], out float bx, out float by, out float bz);
                float nx = px[other] - px[i], ny = py[other] - py[i], nz = pz[other] - pz[i];
                float radial = nx * px[i] + ny * py[i] + nz * pz[i];
                nx -= radial * px[i]; ny -= radial * py[i]; nz -= radial * pz[i];
                Normalize(ref nx, ref ny, ref nz);

                float rx = ax - bx, ry = ay - by, rz = az - bz;
                float convergence = rx * nx + ry * ny + rz * nz;
                float tx = rx - convergence * nx, ty = ry - convergence * ny, tz = rz - convergence * nz;
                float shear = (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
                bool own = crust[i] > 0f, opposite = crust[other] > 0f;
                float ownApproach = ax * nx + ay * ny + az * nz, otherApproach = -(bx * nx + by * ny + bz * nz);
                var mg = new Margin { Convergence = convergence, Shear = shear, OwnContinental = own, OtherContinental = opposite };

                if (convergence > .22f)
                {
                    if (own && opposite)
                    {
                        mg.Setting = TectonicSetting.Collision;
                        mg.Overriding = ownApproach < otherApproach || (ownApproach == otherApproach && plate[i] < plate[other]);
                    }
                    else if (own != opposite)
                    {
                        mg.Setting = TectonicSetting.Subduction;
                        mg.Overriding = own;
                    }
                    else
                    {
                        mg.Setting = TectonicSetting.IslandArc;
                        mg.Overriding = ownApproach < otherApproach || (ownApproach == otherApproach && plate[i] < plate[other]);
                    }
                }
                else if (convergence < -.22f && !own && !opposite) mg.Setting = TectonicSetting.Ridge;
                else if (convergence < -.58f) mg.Setting = TectonicSetting.Rift;
                else if (shear > .42f) mg.Setting = TectonicSetting.Transform;
                else continue;

                margin[i] = mg;
                source[i] = i;
                distance[i] = 0f;
                heap.Push(i, 0);
            }

            float reach = 24f * scale;
            while (heap.Count > 0)
            {
                int c = heap.Pop(out long key);
                float dc = key / 1000f;
                if (dc > distance[c]) continue;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(c, d);
                    if (m < 0 || plate[m] != plate[c]) continue;
                    float step = d < 2 ? rowScale[c / width] : 1f, next = dc + step;
                    if (next >= distance[m] || next > reach) continue;
                    distance[m] = next;
                    source[m] = source[c];
                    heap.Push(m, (long)(next * 1000f));
                }
            }

            // --- Stage 3: Oceanic Crust Age (`determine_crust_age`) ---
            var age = new float[n];
            for (int i = 0; i < n; i++) age[i] = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (source[i] == i && (margin[i].Setting == TectonicSetting.Ridge || margin[i].Setting == TectonicSetting.Rift))
                {
                    age[i] = 0f;
                    heap.Push(i, 0);
                }
            }
            while (heap.Count > 0)
            {
                int c = heap.Pop(out long key);
                float dc = key / 1000f;
                if (dc > age[c]) continue;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(c, d);
                    if (m < 0) continue;
                    float next = dc + (d < 2 ? rowScale[c / width] : 1f);
                    if (next >= age[m] || next > 64f * scale) continue;
                    age[m] = next;
                    heap.Push(m, (long)(next * 1000f));
                }
            }

            // --- Stage 4: Intra-Microplate Passive Continental Shelves (`generate_continental_shelves`, Stage 9) ---
            var shelfDist = new float[n];
            var shelfSpan = new float[n];
            for (int i = 0; i < n; i++) shelfDist[i] = float.MaxValue;
            float maxShelfReach = 14f * scale;
            for (int i = 0; i < n; i++)
            {
                if (crust[i] > 0f) continue;
                // Active inter-plate subduction trenches do NOT get wide passive continental shelves!
                if (source[i] >= 0 && distance[i] < 3.2f * scale &&
                    (margin[source[i]].Setting == TectonicSetting.Subduction || margin[source[i]].Setting == TectonicSetting.IslandArc))
                    continue;

                bool sameMicroLand = false, diffMicroLand = false;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(i, d);
                    if (m < 0 || crust[m] <= 0f) continue;
                    if (microplate[m] == microplate[i]) sameMicroLand = true;
                    else if (plate[m] == plate[i]) diffMicroLand = true;
                }
                if (!sameMicroLand && !diffMicroLand) continue;

                float baseSpan = sameMicroLand
                    ? microShelfWidthRef[microplate[i]]
                    : 1.8f; // Narrower transitional shelf across intra-plate terrane boundary
                float noiseMod = .72f + .56f * (Fbm(px[i] * 5.2f, py[i] * 5.2f, pz[i] * 5.2f, seed ^ 0x59e1fu, 3) * .5f + .5f);
                shelfDist[i] = 0f;
                shelfSpan[i] = FMath.Clamp(baseSpan * noiseMod, 1.4f, 12.5f);
                heap.Push(i, 0);
            }
            while (heap.Count > 0)
            {
                int c = heap.Pop(out long key);
                float dc = key / 1000f;
                if (dc > shelfDist[c]) continue;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(c, d);
                    if (m < 0 || crust[m] > 0f) continue;
                    // Stop passive shelf propagation if approaching an active trench
                    if (source[m] >= 0 && distance[m] < 2.2f * scale &&
                        (margin[source[m]].Setting == TectonicSetting.Subduction || margin[source[m]].Setting == TectonicSetting.IslandArc))
                        continue;
                    float next = dc + (d < 2 ? rowScale[c / width] : 1f);
                    float limit = shelfSpan[c] * 1.35f * scale;
                    if (next >= shelfDist[m] || next > limit || next > maxShelfReach) continue;
                    shelfDist[m] = next;
                    shelfSpan[m] = shelfSpan[c];
                    heap.Push(m, (long)(next * 1000f));
                }
            }

            // --- Stage 5: Ancient Paleo-Orogen Sutures (`generate_old_mountains_and_hills`, Stages 2 & 11) ---
            var paleoDist = new float[n];
            var paleoKind = new byte[n];
            for (int i = 0; i < n; i++) paleoDist[i] = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (crust[i] <= .02f) continue;
                // Only inside stable continental interiors away from active convergent boundaries
                if (source[i] >= 0 && distance[i] < 6.5f * scale &&
                    (margin[source[i]].Setting == TectonicSetting.Collision || margin[source[i]].Setting == TectonicSetting.Subduction))
                    continue;
                byte pk = 0;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(i, d);
                    if (m >= 0 && oldPlate[m] != oldPlate[i])
                    {
                        pk = oldPlateType[oldPlate[i] * OldPlateCount + oldPlate[m]];
                        if (pk != 0) break;
                    }
                }
                if (pk == 0) continue;
                paleoDist[i] = 0f;
                paleoKind[i] = pk;
                heap.Push(i, 0);
            }
            float paleoReach = 4.2f * scale;
            while (heap.Count > 0)
            {
                int c = heap.Pop(out long key);
                float dc = key / 1000f;
                if (dc > paleoDist[c]) continue;
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(c, d);
                    if (m < 0 || crust[m] <= 0f) continue;
                    float next = dc + (d < 2 ? rowScale[c / width] : 1f);
                    if (next >= paleoDist[m] || next > paleoReach) continue;
                    paleoDist[m] = next;
                    paleoKind[m] = paleoKind[c];
                    heap.Push(m, (long)(next * 1000f));
                }
            }

            // --- Stage 6: Seed Large Igneous Provinces (`Stage 12`) & Mantle Superswells (`Stage 17`) ---
            int lipCount = preset == TerrainPreset.Archipelago ? 2 : 4;
            var lips = new LipSeed[lipCount];
            for (int k = 0; k < lipCount; k++)
            {
                RandomUnit(seed ^ 0x1197ba5au, k * 3 + 1, out float lx, out float ly, out float lz);
                if (Math.Abs(lz) > .76f) { lz *= .76f / Math.Abs(lz); Normalize(ref lx, ref ly, ref lz); }
                float radKm = 380f + 440f * FMath.Unit(seed ^ (uint)(k * 6151 + 11));
                lips[k] = new LipSeed
                {
                    X = lx, Y = ly, Z = lz,
                    CosOuter = (float)Math.Cos(radKm / 6371.0),
                    RadiusRef = radKm / 78.18f,
                    HeightM = 460f + 540f * FMath.Unit(seed ^ (uint)(k * 8191 + 29)),
                    Steps = 3 + (int)(3f * FMath.Unit(seed ^ (uint)(k * 4099 + 7)))
                };
            }

            int swellCount = preset == TerrainPreset.Archipelago ? 1 : 2;
            var swells = new SwellSeed[swellCount];
            for (int k = 0; k < swellCount; k++)
            {
                RandomUnit(seed ^ 0x509e85e1u, k * 5 + 2, out float wxS, out float wyS, out float wzS);
                if (Math.Abs(wzS) > .68f) { wzS *= .68f / Math.Abs(wzS); Normalize(ref wxS, ref wyS, ref wzS); }
                float radKm = 1650f + 950f * FMath.Unit(seed ^ (uint)(k * 13107 + 53));
                swells[k] = new SwellSeed
                {
                    X = wxS, Y = wyS, Z = wzS,
                    RadiusRad = radKm / 6371f,
                    LandUpliftM = 340f + 340f * FMath.Unit(seed ^ (uint)(k * 9973 + 17)),
                    SeaUpliftM = 240f + 180f * FMath.Unit(seed ^ (uint)(k * 7717 + 89))
                };
            }

            // --- Stage 7: Per-Tile Elevation, Province & Lithology Synthesis ---
            var elevation = new float[n];
            var setting = new TectonicSetting[n];
            var seismic = new float[n];
            var province = new GlebaTectonicProvince[n];
            var rockRes = new float[n];

            Parallel.For(0, height, parallel, y =>
            {
                cancel.ThrowIfCancellationRequested();
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    float vx = px[i], vy = py[i], vz = pz[i], s = crust[i];
                    ushort mp = microplate[i];
                    GlebaTectonicProvince prov = GlebaTectonicProvince.None;
                    float res = 1.0f;
                    float h;

                    if (s > 0f)
                    {
                        prov |= GlebaTectonicProvince.ContinentalCrust;
                        float coastalRamp = Smooth(0f, .08f, s);
                        float interior = Smooth(.02f, .26f, s);
                        float epeirogenic = (float)Math.Pow(interior, .76);
                        float basinWave = Fbm(vx * 2.3f, vy * 2.3f, vz * 2.3f, seed ^ 0x55u, 4);
                        float detail = Fbm(vx * 6.5f, vy * 6.5f, vz * 6.5f, seed ^ 0x56u, 3);

                        // Gleba continental hypsometry: coastal plains (40..220 m) + gentle epeirogenic interior (250..480 m, Earth P50 = 435 m)
                        h = 34f + 195f * coastalRamp + 235f * epeirogenic * (.58f + .42f * basinWave) + 42f * detail;

                        switch (microTerraneKind[mp])
                        {
                            case 0: // Lowland Alluvial / Sedimentary Basin (Amazon / West Siberia / North China analog)
                                h -= 55f * interior * Smooth(-.1f, .4f, -basinWave);
                                res = .66f;
                                break;
                            case 1: // Raised Old Seabed / Carbonate Platform
                                h += 85f * coastalRamp + 40f * Fbm(vx * 4.4f, vy * 4.4f, vz * 4.4f, seed ^ 0x151u, 3);
                                prov |= GlebaTectonicProvince.RaisedOldSeabed;
                                res = .82f;
                                break;
                            case 2: // Volga-style Rolling Uplands (`uplands_frequency`)
                                float upMask = Smooth(-.15f, .35f, Fbm(vx * 3.4f, vy * 3.4f, vz * 3.4f, seed ^ 0x152u, 3));
                                h += (185f + 225f * upMask) * interior;
                                prov |= GlebaTectonicProvince.VolgaUpland;
                                res = 1.08f;
                                break;
                            default: // Ancient Cratonic Shield (`Canadian / Guiana / Baltic Shield`)
                                float shield = .55f + .45f * Ridged(vx * 4.2f, vy * 4.2f, vz * 4.2f, seed ^ 0x153u, 3);
                                h += (240f + 310f * shield) * interior;
                                prov |= GlebaTectonicProvince.CratonShield;
                                res = 1.44f;
                                break;
                        }

                        // Small stochastic intraplate massifs (`uplifts_frequency`, e.g. Massif Central / Black Hills)
                        float lone = Fbm(vx * 5.8f, vy * 5.8f, vz * 5.8f, seed ^ 0x58u, 3);
                        if (lone > .42f)
                        {
                            float u = Smooth(.42f, .70f, lone) * interior;
                            h += 580f * u * (.5f + .5f * Ridged(vx * 11f, vy * 11f, vz * 11f, seed ^ 0x59u, 3));
                            res = Math.Max(res, 1.25f);
                        }
                    }
                    else
                    {
                        // Parsons-Sclater oceanic crust thermal subsidence (`model.ron`: -4000..-6000 m, aging divisor)
                        float cooled = FMath.Clamp((age[i] == float.MaxValue ? 64f * scale : age[i]) / (50f * scale), 0f, 1f);
                        float floor = -2550f - 3320f * (float)Math.Sqrt(cooled);
                        floor += 290f * cooled * Fbm(vx * 5.2f, vy * 5.2f, vz * 5.2f, seed ^ 0x69u, 3);
                        floor += 75f * Fbm(vx * 10.5f, vy * 10.5f, vz * 10.5f, seed ^ 0x6au, 3);

                        // Stage 9 Variable-Width Intra-Microplate Passive Continental Shelf & Slope Apron
                        if (shelfDist[i] < float.MaxValue)
                        {
                            float dRef = shelfDist[i] / scale;
                            float wShelf = Math.Max(1.2f, shelfSpan[i]);
                            float nearW = .15f * wShelf;
                            if (dRef <= nearW)
                            {
                                h = FMath.Lerp(-18f, -65f, dRef / Math.Max(.1f, nearW));
                                prov |= GlebaTectonicProvince.PassiveShelf;
                            }
                            else if (dRef <= .52f * wShelf)
                            {
                                float u = (float)Math.Pow((dRef - nearW) / Math.Max(.2f, .37f * wShelf), .75);
                                h = FMath.Lerp(-65f, -200f, FMath.Clamp(u, 0f, 1f));
                                prov |= GlebaTectonicProvince.PassiveShelf;
                            }
                            else
                            {
                                float u = Smooth(.52f * wShelf, 1.25f * wShelf, dRef);
                                float slope = FMath.Lerp(-200f, -1650f, Smooth(0f, .52f, u));
                                h = FMath.Lerp(slope, floor, Smooth(.34f, 1f, u));
                            }
                            h += 14f * Fbm(vx * 9f, vy * 9f, vz * 9f, seed ^ 0x6bu, 2);
                        }
                        else
                        {
                            // Narrow active-margin coastal apron where crust transitions across 0
                            float offshore = -s;
                            h = offshore < .024f
                                ? FMath.Lerp(-45f, floor, Smooth(0f, .024f, offshore))
                                : floor;
                        }
                        res = 1.0f;
                    }

                    // --- Boundary Orogenies, Plateaus, Foreland Basins, Rifts, Ridges & Trenches (`model.ron`) ---
                    float uplift = 0f, seis = 0f;
                    int src = source[i];
                    bool isRiftGraben = false;
                    if (src >= 0)
                    {
                        var mg = margin[src];
                        float d = distance[i] / scale;
                        float strength = FMath.Clamp(Math.Abs(mg.Convergence) / 1.15f, .35f, 1.45f);
                        float along = .72f + .56f * Fbm(px[src] * 6.5f, py[src] * 6.5f, pz[src] * 6.5f, seed ^ 0x60u, 3);
                        if (d < 2.6f) setting[i] = mg.Setting;

                        switch (mg.Setting)
                        {
                            case TectonicSetting.Collision:
                            {
                                // Himalayan frontal thrust wall (`4150..6150 m`, narrow steep front)
                                uplift += 3650f * strength * along * Bell(d, .6f, 2.0f);
                                if (mg.Overriding)
                                {
                                    // Tibetan flat-topped plateau (`tibetan_slope_exponent: 0.0125`, `width: 850..3200 km`)
                                    float tibetGate = Smooth(.48f, .95f, strength * (.75f + .5f * (Fbm(px[src] * 2.8f, py[src] * 2.8f, pz[src] * 2.8f, seed ^ 0x71bu, 2) * .5f + .5f)));
                                    float platW = 6.5f + 7.5f * along;
                                    float plat = Plateau(d, 1.6f, platW) * tibetGate;
                                    uplift += 3150f * plat;
                                    // Back-plateau Kunlun/Qilian bounding range
                                    uplift += 1150f * tibetGate * Bell(d, platW, 1.6f);
                                    if (plat > .25f) { prov |= GlebaTectonicProvince.TibetanPlateau; res = Math.Max(res, 1.38f); }
                                }
                                else
                                {
                                    // Indo-Gangetic / Persian Gulf flexural foreland trough guiding trunk rivers!
                                    float foreland = Bell(d, 4.2f, 2.1f);
                                    uplift -= 310f * foreland * strength;
                                    if (foreland > .25f && s > 0f) { prov |= GlebaTectonicProvince.ForelandBasin; res = .56f; }
                                }
                                seis = .92f * (float)Math.Exp(-d / 4.2f);
                                break;
                            }
                            case TectonicSetting.Subduction:
                            {
                                if (mg.OwnContinental)
                                {
                                    // Coastal Andean volcanic cordillera (`3000..5000 m`, `width 75..200 km`)
                                    uplift += 2950f * strength * along * Bell(d, 1.5f, 1.35f);
                                    // Paired Laramide / Rockies inboard fold-thrust belt + Altiplano intermontane plateau on fast slabs
                                    float rockiesGate = Smooth(.72f, 1.18f, strength * along);
                                    if (rockiesGate > .02f)
                                    {
                                        uplift += 1550f * rockiesGate * Plateau(d, 2.2f, 5.4f); // Altiplano / Great Basin
                                        uplift += 2150f * rockiesGate * Bell(d, 5.8f, 1.65f);   // Rockies / Eastern Cordillera
                                        float fore = Bell(d, 9.2f, 2.3f);
                                        uplift -= 240f * rockiesGate * fore;
                                        if (fore > .25f && s > 0f) { prov |= GlebaTectonicProvince.ForelandBasin; res = .58f; }
                                    }
                                    else
                                    {
                                        float fore = Bell(d, 4.6f, 1.9f);
                                        uplift -= 250f * fore;
                                        if (fore > .25f && s > 0f) { prov |= GlebaTectonicProvince.ForelandBasin; res = .58f; }
                                    }
                                    seis = .95f * (float)Math.Exp(-d / 4.5f);
                                }
                                else
                                {
                                    // Asymmetric forearc oceanic trench (`model.ron`: -7500..-10500 m, exponent 5) + outer rise bulge
                                    float tr = Bell(d, .65f, 1.05f);
                                    uplift -= 2650f * (.75f + .45f * strength) * tr;
                                    uplift += 210f * Bell(d, 3.1f, 1.3f);
                                    if (tr > .25f) prov |= GlebaTectonicProvince.ActiveTrench;
                                    seis = .86f * (float)Math.Exp(-d / 1.8f);
                                }
                                break;
                            }
                            case TectonicSetting.IslandArc:
                            {
                                if (mg.Overriding)
                                {
                                    // Discrete arcuate volcanic island chain (`VolcanicIsleOrigin` beaded along strike so straits separate islands!)
                                    float bead = .28f + .92f * Ridged(vx * 13.5f, vy * 13.5f, vz * 13.5f, seed ^ 0xa8c1u, 3);
                                    uplift += 4350f * (.65f + .35f * strength) * bead * Bell(d, 2.0f, 1.15f);
                                    // Back-arc basin behind the volcanic arc (`Sea of Japan / Banda Sea`)
                                    float backArc = Bell(d, 5.4f, 2.2f);
                                    if (s <= 0f) { uplift -= 420f * backArc; if (backArc > .3f) prov |= GlebaTectonicProvince.BackArcBasin; }
                                    seis = .92f * (float)Math.Exp(-d / 3.0f);
                                }
                                else
                                {
                                    // Deep Mariana/Kuril ocean-ocean trench
                                    float tr = Bell(d, .60f, 1.0f);
                                    uplift -= 2850f * (.75f + .45f * strength) * tr;
                                    uplift += 190f * Bell(d, 2.9f, 1.2f);
                                    if (tr > .25f) prov |= GlebaTectonicProvince.ActiveTrench;
                                    seis = .80f * (float)Math.Exp(-d / 1.7f);
                                }
                                break;
                            }
                            case TectonicSetting.Rift:
                            {
                                if (s > 0f)
                                {
                                    // Gleba `make_continental_rifts`: broad uplifted shoulders (+650..950m) + segmented deep axial graben (-1000m)!
                                    float shoulder = Bell(d, 2.1f, 1.65f);
                                    float grabenSeg = Smooth(-.15f, .38f, Fbm(vx * 14f, vy * 14f, vz * 14f, seed ^ 0x81f7u, 3));
                                    float graben = Bell(d, 0f, .82f) * (.45f + .75f * grabenSeg);
                                    uplift += 720f * (.7f + .3f * strength) * shoulder - 1080f * (.75f + .35f * strength) * graben;
                                    if (graben > .38f && s > .05f)
                                    {
                                        prov |= GlebaTectonicProvince.RiftGraben;
                                        setting[i] = TectonicSetting.Rift;
                                        isRiftGraben = true;
                                        res = .72f;
                                    }
                                }
                                else
                                {
                                    uplift += 1450f * Bell(d, 0f, 2.6f) - 280f * Bell(d, 0f, .45f);
                                }
                                seis = .52f * (float)Math.Exp(-d / 1.5f);
                                break;
                            }
                            case TectonicSetting.Ridge:
                            {
                                // Mid-ocean ridge (`-3000..-2000 m`) + axial rift valley + transform fracture corrugation
                                float frac = Fbm(vx * 18f, vy * 18f, vz * 18f, seed ^ 0x81d6u, 2);
                                uplift += (1550f + 320f * frac) * Bell(d, 0f, 3.1f) - 290f * Bell(d, 0f, .42f);
                                seis = .40f * (float)Math.Exp(-d / 1.2f);
                                break;
                            }
                            case TectonicSetting.Transform:
                            {
                                uplift += -260f * Bell(d, 0f, .68f) + 240f * Bell(d, 1.5f, .85f);
                                seis = .90f * (float)Math.Exp(-d / 1.3f);
                                break;
                            }
                        }
                    }

                    // Ridged alpine crests & spurs on active mountain belts (attenuated on flat Tibetan plateau interiors)
                    float belt = FMath.Clamp(uplift / 2400f, 0f, 1.35f);
                    if (belt > 0f)
                    {
                        float flatTop = (prov & GlebaTectonicProvince.TibetanPlateau) != 0 ? .42f : 1.0f;
                        uplift += 960f * belt * flatTop * (Ridged(vx * 9.5f, vy * 9.5f, vz * 9.5f, seed ^ 0x61u, 4) - .42f);
                        res = Math.Max(res, 1.15f + .35f * belt);
                    }

                    // --- Paleo-Orogens (`Stages 2 & 11`: Ural / Appalachian / Caledonian sutures) ---
                    if (s > .02f && paleoDist[i] < float.MaxValue)
                    {
                        float pd = paleoDist[i] / scale;
                        float strikeFold = .45f + .65f * Ridged(vx * 12f, vy * 12f, vz * 12f, seed ^ 0x01d1u, 4);
                        float alongSuture = Smooth(-.18f, .35f, Fbm(vx * 3.2f, vy * 3.2f, vz * 3.2f, seed ^ 0x01d2u, 3));
                        if (alongSuture > .05f)
                        {
                            float peak = paleoKind[i] == 1 ? 1180f : 620f; // Urals/Appalachians vs Caledonides
                            float widthP = paleoKind[i] == 1 ? 1.85f : 1.45f;
                            float add = peak * alongSuture * strikeFold * Bell(pd, 0f, widthP);
                            if (add > 90f)
                            {
                                uplift += add;
                                prov |= GlebaTectonicProvince.PaleoOrogen;
                                res = Math.Max(res, 1.34f);
                            }
                        }
                    }

                    // --- Large Igneous Provinces (`Stage 12`: Stepped Deccan/Siberian Flood-Basalt Traps & Oceanic Plateaus) ---
                    float lipWarp = .035f * Fbm(vx * 5.4f, vy * 5.4f, vz * 5.4f, seed ^ 0x1199u, 3);
                    for (int k = 0; k < lipCount; k++)
                    {
                        float dot = vx * lips[k].X + vy * lips[k].Y + vz * lips[k].Z + lipWarp;
                        if (dot < lips[k].CosOuter) continue;
                        float u = Smooth(lips[k].CosOuter, 1f, FMath.Clamp(dot, -1f, 1f));
                        if (s > 0f)
                        {
                            // Characteristic stepped basalt trap terraces (`quantized + smoothed`)
                            float steps = lips[k].Steps;
                            float terraced = (.68f * (float)Math.Floor(u * steps + .85f) / steps + .32f * u);
                            uplift += lips[k].HeightM * FMath.Clamp(terraced, 0f, 1.05f) * Smooth(0f, .06f, s);
                            prov |= GlebaTectonicProvince.LargeIgneousProvince;
                            res = Math.Max(res, 1.52f);
                        }
                        else if (h < -1600f)
                        {
                            // Oceanic Ontong-Java / Kerguelen basalt plateau
                            uplift += 1350f * u;
                            prov |= GlebaTectonicProvince.LargeIgneousProvince;
                        }
                        break;
                    }

                    // --- Mantle Superswells (`Stage 17`: Southern/East African broad dynamic-topography dome) ---
                    for (int k = 0; k < swellCount; k++)
                    {
                        float dot = FMath.Clamp(vx * swells[k].X + vy * swells[k].Y + vz * swells[k].Z, -1f, 1f);
                        float ang = (float)Math.Acos(dot) + .06f * Fbm(vx * 3.1f, vy * 3.1f, vz * 3.1f, seed ^ 0x57e1u, 3);
                        if (ang >= swells[k].RadiusRad) continue;
                        float dome = 1f - Smooth(.25f * swells[k].RadiusRad, swells[k].RadiusRad, ang);
                        if (s > 0f)
                        {
                            uplift += swells[k].LandUpliftM * dome * Smooth(0f, .08f, s);
                            if (dome > .30f) prov |= GlebaTectonicProvince.MantleSuperswell;
                        }
                        else
                        {
                            uplift += swells[k].SeaUpliftM * dome;
                        }
                    }

                    float total = h + uplift;
                    // Non-rift foreland sags on continental crust stay above sea level so they form river plains rather than random straits,
                    // EXCEPT genuine deep inland continental Rift Grabens (`isRiftGraben`), which carve deep Baikal/Tanganyika/Dead-Sea troughs!
                    if (s > 0f && uplift < 0f)
                    {
                        if (isRiftGraben && s > .09f)
                            total = Math.Max(total, -420f);
                        else if (total < 22f)
                            total = Math.Min(h, 22f);
                    }

                    elevation[i] = total;
                    seismic[i] = FMath.Clamp(seis, 0f, 1f);
                    province[i] = prov;
                    rockRes[i] = FMath.Clamp(res, .52f, 1.65f);
                }
            });

            // --- Stage 8: Arc & Rift Volcanoes + Dual-Plume Hotspots (Hawaiian Chains & Icelandic Ridge Plumes) ---
            var volcanoes = new List<Volcano>();
            var protectedVolcanic = new bool[n];

            for (int i = 0; i < n; i++)
            {
                int src = source[i];
                if (src < 0) continue;
                var mg = margin[src];
                float d = distance[i] / scale;
                VolcanoKind kind;
                float activity;
                if (mg.Setting == TectonicSetting.Subduction && mg.OwnContinental && d > 1.1f && d < 2.8f)
                {
                    kind = VolcanoKind.Arc;
                    activity = .6f + .4f * FMath.Clamp(mg.Convergence, 0f, 1f);
                }
                else if (mg.Setting == TectonicSetting.IslandArc && mg.Overriding && d > 1.2f && d < 2.8f)
                {
                    kind = VolcanoKind.Arc;
                    activity = .72f;
                }
                else if (mg.Setting == TectonicSetting.Rift && crust[i] > 0f && d < 1.8f)
                {
                    kind = VolcanoKind.Rift;
                    activity = .38f;
                }
                else continue;

                if (!LocalHashMaximum(w, i, seed ^ 0x62u, (int)Math.Ceiling(1.6f * scale))) continue;
                volcanoes.Add(new Volcano(i, kind, activity));
                Cone(w, elevation, i, kind == VolcanoKind.Rift ? 720f : 1320f, scale);
                MarkDisk(w, protectedVolcanic, i, 2.2f * scale);
            }

            // 8a. Hawaiian-type static mantle plumes: active shield volcano -> subsiding coral atolls -> submerged guyot seamounts
            int hotspots = preset == TerrainPreset.Archipelago ? 8 : 5;
            int placedHotspots = 0;
            for (int k = 0; k < hotspots + 4 && placedHotspots < hotspots; k++)
            {
                RandomUnit(seed ^ 0x407u, k, out float hx, out float hy, out float hz);
                if (Math.Abs(hz) > .72f) continue;
                placedHotspots++;
                int tile = TileOf(w, hx, hy, hz);
                var pl = plates[plate[tile]];
                for (int step = 0; step < 8; step++)
                {
                    int t = TileOf(w, hx, hy, hz);
                    float fade = 1f - step / 8f;
                    province[t] |= GlebaTectonicProvince.HawaiianChain;
                    if (crust[t] > 0f)
                    {
                        if (step == 0) PlateauDisk(w, elevation, t, 980f, 2.8f * scale);
                        Cone(w, elevation, t, 880f * fade, scale);
                    }
                    else
                    {
                        // Active shield island (steps 0..2) -> subsiding atoll shoal (steps 3..4) -> deep guyot seamount (steps 5..7)
                        float seaDepth = elevation[t] < 0f ? -elevation[t] : 0f;
                        float edifice = step <= 2
                            ? seaDepth + 1350f * fade - step * 240f
                            : step <= 4
                                ? seaDepth - 35f - (step - 3) * 140f
                                : seaDepth * .55f + 350f * fade;
                        Cone(w, elevation, t, Math.Max(250f, edifice), scale * (step <= 2 ? 1.05f : .90f));
                        MarkDisk(w, protectedVolcanic, t, 2.0f * scale);
                    }
                    volcanoes.Add(new Volcano(t, VolcanoKind.Hotspot, step == 0 ? 1f : Math.Max(0f, .45f - step * .09f)));

                    Velocity(pl, hx, hy, hz, out float vx, out float vy, out float vz);
                    float speed = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    if (speed < 1e-4f) break;
                    float stepLength = .033f / speed;
                    hx -= vx * stepLength; hy -= vy * stepLength; hz -= vz * stepLength;
                    Normalize(ref hx, ref hy, ref hz);
                }
            }

            // 8b. Gleba Icelandic-type ridge-centered deep plume (`icelandic_hotspot_frequency`)
            int icelandCount = preset == TerrainPreset.Archipelago ? 2 : 1;
            int placedIceland = 0;
            for (int attempt = 0; attempt < 24 && placedIceland < icelandCount; attempt++)
            {
                RandomUnit(seed ^ 0x1ce1a4du, attempt * 3, out float ix, out float iy, out float iz);
                if (Math.Abs(iz) > .74f) continue;
                int center = TileOf(w, ix, iy, iz);
                if (crust[center] > 0f || source[center] < 0 || margin[source[center]].Setting != TectonicSetting.Ridge || distance[center] > 3.5f * scale)
                    continue;
                placedIceland++;
                float baseDepth = elevation[center] < 0f ? -elevation[center] : 0f;
                PlateauDisk(w, elevation, center, baseDepth + 680f, 3.2f * scale);
                Cone(w, elevation, center, 820f, 1.3f * scale);
                MarkDisk(w, protectedVolcanic, center, 4.2f * scale);
                foreach (var p in SurfaceDisk(w, center, 4.0f * scale))
                    province[p.Tile] |= GlebaTectonicProvince.IcelandicPlume;
                volcanoes.Add(new Volcano(center, VolcanoKind.Hotspot, .92f));
            }

            // --- Stage 9: Polar Basin Depression, Stage 41 Trench Cliff Beveling & Post-LGM `land_sunkeness` Calibration ---
            for (int i = 0; i < n; i++)
            {
                float pole = 1f - Smooth(0f, .05f, capDistance[i]);
                float bed = elevation[i], headroom = FMath.Clamp((bed + 4000f) / 4000f, 0f, 1f);
                elevation[i] = bed - 1600f * pole * headroom * headroom;
            }

            // Gleba Stage 41 (`lower_coastal_land_tiles`): bevel extreme coastal cliffs directly abutting <-4200 m trenches
            var bevel = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (elevation[i] <= 450f || protectedVolcanic[i]) continue;
                float minNb = elevation[i];
                for (int d = 0; d < 4; d++)
                {
                    int m = w.SphereNeighbor(i, d);
                    if (m >= 0 && elevation[m] < minNb) minNb = elevation[m];
                }
                if (minNb < -4200f)
                {
                    float slump = 420f + 340f * FMath.Unit(seed ^ (uint)(i * 2654435761u));
                    bevel[i] = Math.Min(slump, elevation[i] - 120f);
                }
            }
            for (int i = 0; i < n; i++) if (bevel[i] > 0f) elevation[i] -= bevel[i];

            // Smooth C^1 coastal sea-level submergence calibration (`land_sunkeness`):
            // Adjusts only coastal/lowland elevations (-850..+1350 m) on non-volcanic tiles so area-weighted land fraction
            // lands accurately near each preset's target while drowning low coastal necks into realistic rias, bays & shelves!
            double totalSphereArea = 0;
            for (int y = 0; y < height; y++) totalSphereArea += rowScale[y] * width;

            double EvalLandShare(float seaShift)
            {
                double dry = 0;
                for (int y = 0; y < height; y++)
                {
                    double wRow = rowScale[y];
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int i = row + x;
                        float h = ApplyCoastalSeaShift(elevation[i], crust[i], seaShift, protectedVolcanic[i], (province[i] & GlebaTectonicProvince.RiftGraben) != 0);
                        if (h > 0f) dry += wRow;
                    }
                }
                return dry / totalSphereArea;
            }

            double rawShare = EvalLandShare(0f);
            float bestShift = 0f;
            if (Math.Abs(rawShare - target) > .008)
            {
                float lo = -220f, hi = 240f;
                for (int iter = 0; iter < 18; iter++)
                {
                    float mid = .5f * (lo + hi);
                    if (EvalLandShare(mid) > target) lo = mid;
                    else hi = mid;
                }
                bestShift = .5f * (lo + hi);
            }

            for (int i = 0; i < n; i++)
            {
                float h = ApplyCoastalSeaShift(elevation[i], crust[i], bestShift, protectedVolcanic[i], (province[i] & GlebaTectonicProvince.RiftGraben) != 0);
                w.Elevation[i] = (short)Math.Round(FMath.Clamp(h, HeightBands.MinimumBed, HeightBands.MaximumBed));
            }

            // Connect any stranded ocean-scale marginal seas (> 1.1% of globe & bed < -900 m) to the primary ocean
            // via a lowest-saddle Gibraltar/Bosporus strait so enclosed seas don't flood entire continents as freshwater lakes,
            // while preserving Caspian-scale inland seas, rift grabens, and endorheic basins untouched.
            ConnectStrandedMarginalSeas(w, seed, cancel);
            SmoothPolarApexRing(w);

            return new FlatTectonicFacts(plates, plate, setting, seismic, volcanoes.ToArray(), microplate, microplateCount, province, rockRes);
        }

        // Ensures the 0.7-deg polar apex ring (`y == 0` and `y == Height - 1`, where all meridians meet within half a tile of the pole)
        // joins smoothly across the pole without step jumps from discrete Dijkstra/shelf grid paths.
        static void SmoothPolarApexRing(WorldData w)
        {
            int width = w.Width, height = w.Height;
            var scratch = new short[width];
            foreach (int y in new[] { 0, height - 1 })
            {
                int row = y * width;
                int adjRow = (y == 0 ? 1 : height - 2) * width;
                for (int x = 0; x < width; x++)
                {
                    int self = w.Elevation[row + x];
                    int opp = w.Elevation[row + (x + width / 2) % width];
                    int adj = w.Elevation[adjRow + x];
                    int wL = w.Elevation[row + (x + width - 4) % width];
                    int eR = w.Elevation[row + (x + 4) % width];
                    float blend = .56f * self + .16f * opp + .16f * adj + .06f * wL + .06f * eR;
                    short target = (short)Math.Round(blend);
                    // Preserve land/ocean sign unless virtually at 0 m
                    if (self > 0 && target <= 0) target = 2;
                    else if (self < 0 && target >= 0) target = -2;
                    scratch[x] = target;
                }
                for (int x = 0; x < width; x++) w.Elevation[row + x] = scratch[x];
            }
        }

        static void ConnectStrandedMarginalSeas(WorldData w, uint seed, CancellationToken cancel)
        {
            int n = w.Count;
            int minSeaTiles = Math.Max(16, n / 90);
            var comp = new int[n];
            var queue = new int[n];
            var prev = new int[n];
            var dist = new long[n];
            var heap = new FloodHeap();

            for (int pass = 0; pass < 6; pass++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int i = 0; i < n; i++) comp[i] = -1;
                int compCount = 0, primaryComp = -1;
                double bestArea = -1.0;
                var compTiles = new List<int>();
                var strandedStarts = new List<int>();

                for (int s = 0; s < n; s++)
                {
                    if (comp[s] >= 0 || w.Elevation[s] >= 0) continue;
                    int cid = compCount++;
                    int head = 0, tail = 0;
                    short minBed = w.Elevation[s];
                    double area = 0;
                    comp[s] = cid;
                    queue[tail++] = s;
                    while (head < tail)
                    {
                        int u = queue[head++];
                        if (w.Elevation[u] < minBed) minBed = w.Elevation[u];
                        area += FlatRiverDischarge.RelativeArea(u / w.Width, w.Height);
                        for (int d = 0; d < 4; d++)
                        {
                            int v = w.SphereNeighbor(u, d);
                            if (v >= 0 && comp[v] < 0 && w.Elevation[v] < 0)
                            {
                                comp[v] = cid;
                                queue[tail++] = v;
                            }
                        }
                    }
                    compTiles.Add(tail);
                    if (area > bestArea) { bestArea = area; primaryComp = cid; }
                    if (tail >= minSeaTiles && minBed <= -900) strandedStarts.Add(s);
                }

                int targetCid = -1;
                int largestStranded = 0;
                for (int k = 0; k < strandedStarts.Count; k++)
                {
                    int cid = comp[strandedStarts[k]];
                    if (cid == primaryComp) continue;
                    if (compTiles[cid] > largestStranded)
                    {
                        largestStranded = compTiles[cid];
                        targetCid = cid;
                    }
                }
                if (targetCid < 0) break;

                // Dijkstra from target stranded sea to primary ocean across lowest-elevation land saddle
                for (int i = 0; i < n; i++) { dist[i] = long.MaxValue; prev[i] = -1; }
                while (heap.Count > 0) heap.Pop(out _);
                for (int i = 0; i < n; i++)
                {
                    if (comp[i] != targetCid) continue;
                    bool border = false;
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(i, d);
                        if (nb >= 0 && comp[nb] != targetCid) { border = true; break; }
                    }
                    if (border) { dist[i] = 0; heap.Push(i, 0); }
                }

                int meet = -1;
                while (heap.Count > 0)
                {
                    int u = heap.Pop(out long du);
                    if (du > dist[u]) continue;
                    if (comp[u] == primaryComp) { meet = u; break; }
                    for (int d = 0; d < 4; d++)
                    {
                        int v = w.SphereNeighbor(u, d);
                        if (v < 0) continue;
                        int vy = v / w.Width;
                        // Do not slice artificial straits across the immediate polar cap rows (86-90 deg S/N)
                        if (w.Elevation[v] >= 0 && (vy < 3 || vy >= w.Height - 3)) continue;
                        long stepCost = w.Elevation[v] < 0 ? 8L : (40L + Math.Max(0, (int)w.Elevation[v]));
                        long next = du + stepCost;
                        if (next < dist[v])
                        {
                            dist[v] = next;
                            prev[v] = u;
                            heap.Push(v, next);
                        }
                    }
                }
                if (meet < 0) break;

                for (int cur = prev[meet]; cur >= 0 && comp[cur] != targetCid; cur = prev[cur])
                {
                    uint h = FMath.Hash((uint)(cur * 9973 + pass) ^ seed);
                    short straitDepth = (short)(-55 - (int)(85f * FMath.Unit(h)));
                    if (w.Elevation[cur] >= 0 || w.Elevation[cur] > straitDepth)
                        w.Elevation[cur] = straitDepth;
                    // Gently bevel immediate towering cliff banks beside the strait
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(cur, d);
                        if (nb >= 0 && w.Elevation[nb] > 380)
                            w.Elevation[nb] = (short)(180 + (w.Elevation[nb] - 180) / 3);
                    }
                }
            }
        }

        static float ApplyCoastalSeaShift(float bed, float crustVal, float seaShift, bool keepVolcanic, bool isRiftGraben)
        {
            if (seaShift == 0f || keepVolcanic || isRiftGraben) return bed;
            // Attenuate sea-level submergence inside deep continental interiors (`crustVal > 0.08`) so `land_sunkeness`
            // drowns coastal margins/bays without ever dropping inland Pangaea river basins below 0 m!
            float wInterior = 1f - Smooth(.06f, .16f, crustVal);
            if (wInterior <= 0f) return bed;
            float wLow = Smooth(-880f, -120f, bed);
            float wHigh = 1f - Smooth(480f, 1380f, bed);
            float shifted = bed - seaShift * wLow * wHigh * wInterior;
            if (crustVal > .09f && bed > 15f && shifted < 18f) shifted = 18f;
            return shifted;
        }

        static void MarkDisk(WorldData w, bool[] mask, int tile, float radius)
        {
            foreach (var p in SurfaceDisk(w, tile, radius))
                mask[p.Tile] = true;
        }

        static float Bell(float d, float centre, float width) { float t = (d - centre) / width; return (float)Math.Exp(-t * t); }
        static float Plateau(float d, float start, float end) { return Smooth(start - 1.2f, start + .8f, d) * (1f - Smooth(end - 1.5f, end + 1.5f, d)); }
        static float Smooth(float a, float b, float x) { float t = FMath.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3f - 2f * t); }
        static void Normalize(ref float x, ref float y, ref float z) { float l = (float)Math.Sqrt(x * x + y * y + z * z); if (l < 1e-9f) { x = 1f; y = z = 0f; return; } x /= l; y /= l; z /= l; }
        static void RandomUnit(uint seed, int index, out float x, out float y, out float z)
        {
            float u = FMath.Unit(seed ^ (uint)(index * 2654435761u) ^ 0x9e37u) * 2f - 1f;
            float phi = FMath.Unit(seed ^ (uint)(index * 40503u + 17) ^ 0x7f4au) * 6.2831853f;
            float r = (float)Math.Sqrt(Math.Max(0f, 1f - u * u));
            x = r * (float)Math.Cos(phi); y = r * (float)Math.Sin(phi); z = u;
        }
        static void Velocity(TectonicPlate p, float x, float y, float z, out float vx, out float vy, out float vz)
        {
            vx = (p.AxisY * z - p.AxisZ * y) * p.Speed;
            vy = (p.AxisZ * x - p.AxisX * z) * p.Speed;
            vz = (p.AxisX * y - p.AxisY * x) * p.Speed;
        }
        static int TileOf(WorldData w, float x, float y, float z)
        {
            double lat = Math.Asin(FMath.Clamp(z, -1f, 1f)), lon = Math.Atan2(y, x);
            int tx = FMath.Wrap((int)Math.Floor((lon + Math.PI) / (2 * Math.PI) * w.Width), w.Width);
            int ty = FMath.Clamp((int)Math.Floor((lat / Math.PI + .5) * w.Height), 0, w.Height - 1);
            return ty * w.Width + tx;
        }

        // A disk measured in equatorial tile widths on the sphere. Each covered tile is visited once across the date line.
        internal static IEnumerable<(int Tile, float Distance)> SurfaceDisk(WorldData w, int tile, float radius)
        {
            int x = tile % w.Width, y = tile / w.Width, rows = (int)Math.Ceiling(radius);
            double angle = 2 * Math.PI / w.Width, latitude = (y + .5 - w.Height * .5) * angle;
            double cosCenter = Math.Cos(latitude), sinRadius = Math.Sin(radius * angle * .5);
            double limit = sinRadius * sinRadius;
            for (int row = Math.Max(0, y - rows); row <= Math.Min(w.Height - 1, y + rows); row++)
            {
                double north = Math.Sin((row - y) * angle * .5), north2 = north * north;
                if (north2 > limit) continue;
                double lonFactor = cosCenter * Math.Cos((row + .5 - w.Height * .5) * angle);
                double span = 2 * Math.Asin(Math.Sqrt(Math.Min(1.0, (limit - north2) / Math.Max(1e-12, lonFactor)))) / angle;
                int dxMax = Math.Min(w.Width / 2, (int)Math.Floor(span + 1e-8));
                for (int dx = -dxMax; dx <= Math.Min(dxMax, (w.Width - 1) / 2); dx++)
                {
                    double east = Math.Sin(dx * angle * .5);
                    float distance = (float)(2 * Math.Asin(Math.Sqrt(Math.Min(1.0, north2 + lonFactor * east * east))) / angle);
                    yield return (w.Index(x + dx, row), distance);
                }
            }
        }

        static bool LocalHashMaximum(WorldData w, int tile, uint seed, int radius)
        {
            float own = FMath.Unit((uint)tile ^ seed);
            foreach (var p in SurfaceDisk(w, tile, radius))
                if (p.Tile != tile && FMath.Unit((uint)p.Tile ^ seed) >= own) return false;
            return true;
        }

        internal static void Cone(WorldData w, float[] h, int tile, float height, float scale)
        {
            foreach (var p in SurfaceDisk(w, tile, 2f * scale))
            {
                float d = p.Distance / scale;
                h[p.Tile] += height * (float)Math.Exp(-d * d * 1.8f);
            }
        }

        static void PlateauDisk(WorldData w, float[] h, int tile, float height, float radius)
        {
            foreach (var p in SurfaceDisk(w, tile, 1.5f * radius))
                h[p.Tile] += height * (1f - Smooth(radius * .6f, radius * 1.5f, p.Distance));
        }

        static float WeightedQuantile(float[] values, float[] rowScale, int width, float q)
        {
            var order = new int[values.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => values[a] != values[b] ? values[a].CompareTo(values[b]) : a.CompareTo(b));
            double total = 0; foreach (int i in order) total += rowScale[i / width];
            double sum = 0; foreach (int i in order) { sum += rowScale[i / width]; if (sum >= total * q) return values[i]; }
            return values[order[order.Length - 1]];
        }

        // 3D lattice value noise sampled on the unit sphere: seamless at the dateline and poles.
        static float Lattice(int x, int y, int z, uint seed)
        { unchecked { return FMath.Unit((uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u ^ seed) * 2f - 1f; } }

        static float Value(float x, float y, float z, uint seed)
        {
            x += .31f; y += .37f; z += .23f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y), z0 = (int)Math.Floor(z);
            float fx = x - x0, fy = y - y0, fz = z - z0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float a = FMath.Lerp(Lattice(x0, y0, z0, seed), Lattice(x0 + 1, y0, z0, seed), fx);
            float b = FMath.Lerp(Lattice(x0, y0 + 1, z0, seed), Lattice(x0 + 1, y0 + 1, z0, seed), fx);
            float c = FMath.Lerp(Lattice(x0, y0, z0 + 1, seed), Lattice(x0 + 1, y0, z0 + 1, seed), fx);
            float d = FMath.Lerp(Lattice(x0, y0 + 1, z0 + 1, seed), Lattice(x0 + 1, y0 + 1, z0 + 1, seed), fx);
            return FMath.Lerp(FMath.Lerp(a, b, fy), FMath.Lerp(c, d, fy), fz);
        }

        internal static float Fbm(float x, float y, float z, uint seed, int octaves)
        {
            float sum = 0f, amplitude = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * Value(x, y, z, seed + (uint)o * 1013u);
                norm += amplitude; amplitude *= .5f;
                x *= 2.03f; y *= 2.03f; z *= 2.03f;
            }
            return sum / norm;
        }

        internal static float Ridged(float x, float y, float z, uint seed, int octaves)
        {
            float sum = 0f, amplitude = 1f, norm = 0f, weight = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float r = 1f - Math.Abs(Value(x, y, z, seed + (uint)o * 1013u));
                r *= r * weight; weight = FMath.Clamp(r * 1.6f, 0f, 1f);
                sum += amplitude * r; norm += amplitude; amplitude *= .5f;
                x *= 2.07f; y *= 2.07f; z *= 2.07f;
            }
            return sum / norm;
        }
    }
}
