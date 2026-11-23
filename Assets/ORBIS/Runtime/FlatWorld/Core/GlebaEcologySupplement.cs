using System;

namespace Orbis.Flat
{
    /// <summary>
    /// Gleba v0.3.6 vegetation fractions, biome rules, glacier/sea-ice curves, and 6-pass soil
    /// synthesis reverse-engineered from 06_ecology_glacier_soil_stages.asm and biomes.lua.
    /// </summary>
    public struct GlebaPlantCover
    {
        public float ConifersVsBroadleaf; // 0 = 100% conifer, 1 = 100% broadleaf
        public float Trees;               // Fraction of vegetated cover [0, 1]
        public float Shrubs;              // Fraction of vegetated cover [0, 1]
        public float Grass;               // Fraction of vegetated cover [0, 1]
        public float TotalPlants;         // Total ground cover [0, 1]
        public float DeadLand => FMath.Clamp(1f - TotalPlants, 0f, 1f);
        public float ConiferFraction => FMath.Clamp(1f - ConifersVsBroadleaf, 0f, 1f);
    }

    public enum GlebaBiomeKind : byte
    {
        RockyWasteland = 0,
        DesertsAndXericShrublands = 1,
        Sea = 2,
        GlaciatedSea = 3,
        SweetwaterLake = 4,
        SaltwaterLake = 5,
        GlaciatedSaltwaterLake = 6,
        Glacier = 7,
        TemperateConiferousForest = 8,
        TemperateBroadleafAndMixedForest = 9,
        DryJungle = 10,
        TemperateGrasslandsSavannasAndShrublands = 11,
        TropicalGrasslandsSavannasAndShrublands = 12,
        MediterraneanForestWoodlandAndScrub = 13,
        MontaneGrasslandsAndShrublands = 14,
        WetJungle = 15,
        Tundra = 16,
        Taiga = 17,
        ArcticWasteland = 18,
        GlaciatedSweetwaterLake = 19,
        Bog = 20,
        Marsh = 21,
        Swamp = 22,
        HumidTropicalGrassland = 23,
        SemiAridSteppe = 24
    }

    /// <summary>
    /// Domesticable wild founder crops & nitrogen-fixing companion species from Gleba v0.3.6 (mods/base/data/plants.lua).
    /// </summary>
    public enum GlebaFounderCrop : byte
    {
        None = 0,
        WildPea = 1,          // Pisum sativum elatius -> Common pea (nitrogen_fixing = 0.70 -> 1.0)
        WildBitterVetch = 2,  // Vicia ervilia -> Common bitter vetch (nitrogen_fixing = 0.15 -> 0.70)
        WildRye = 3,          // Secale segetale -> Common rye
        WildRice = 4,         // Oryza rufipogon -> Short-grain & Long-grain rice
        AzollaCompanion = 5   // Azolla (Fairy moss) paddy nitrogen-fixing companion (nitrogen_fixing = 1.0)
    }

    public struct GlebaCropEvaluation
    {
        public GlebaFounderCrop DominantCrop;
        public float MaxSuitability;
        public float PeaScore;
        public float BitterVetchScore;
        public float RyeScore;
        public float RiceScore;
        public float AzollaScore;
        public float NitrogenFixing;
        public float CarryingCapacity;
    }

    public struct GlebaSoilState
    {
        public float Sand;
        public float Clay;
        public float Silt;
        public float Depth;
        public float MineralRichness;
        public float Organics;

        public float GetPermeability()
        {
            float p = 1f;
            if (Sand > 0.15f) p = 1f - 0.8f * ((Sand - 0.15f) / 0.85f);
            if (Silt > 0.85f) p -= 0.1f * ((Silt - 0.85f) / 0.15f);
            if (Clay > 0.20f) p -= 0.5f * ((Clay - 0.20f) / 0.80f);
            return p;
        }

        public void NormalizeTexture()
        {
            float sum = Math.Max(Sand + Clay + Silt, 1e-5f);
            Sand /= sum;
            Clay /= sum;
            Silt /= sum;
        }
    }

    public static class GlebaEcologySupplement
    {
        static GlebaClimateConfig.CurvePoint P(float x, float y) => new GlebaClimateConfig.CurvePoint(x, y);

        // 17 static .rdata Holdridge lookup tables from initialize_flora_from_holdridge_and_koppen (0x1808efa90)
        static readonly GlebaClimateConfig.CurvePoint[] CurveTreelineHigh = {
            P(0f, 1000f), P(1.5f, 1500f), P(3f, 2000f), P(6f, 2500f),
            P(12f, 3000f), P(17f, 4000f), P(24f, 5500f), P(30f, 4500f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveTreeShrubTemp = { P(0f, 0f), P(1.5f, 0f), P(3f, 1f) };
        static readonly GlebaClimateConfig.CurvePoint[] CurveRTreeLow = {
            P(0f, 0f), P(1.5f, 0f), P(3f, 200f), P(6f, 200f), P(12f, 450f), P(17f, 450f), P(24f, 650f), P(30f, 750f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveRTreeMid = {
            P(0f, 150f), P(1.5f, 200f), P(3f, 250f), P(6f, 500f), P(12f, 500f), P(17f, 900f), P(24f, 900f), P(30f, 900f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveRTreeHigh = {
            P(0f, 200f), P(1.5f, 250f), P(3f, 300f), P(6f, 600f), P(12f, 600f), P(17f, 1200f), P(24f, 1200f), P(30f, 1200f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveGrassTemp = { P(0f, 0f), P(1.5f, 0f), P(2f, 1f) };
        static readonly GlebaClimateConfig.CurvePoint[] CurveRGrassLow = {
            P(0f, 0f), P(1.5f, 0f), P(3f, 0f), P(6f, 150f), P(12f, 200f), P(17f, 200f), P(24f, 200f), P(30f, 500f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveRGrassMid = {
            P(0f, 250f), P(1.5f, 250f), P(3f, 250f), P(6f, 400f), P(12f, 500f), P(17f, 400f), P(24f, 300f), P(30f, 550f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveRGrassHigh = {
            P(0f, 2000f), P(1.5f, 1000f), P(3f, 300f), P(6f, 450f), P(12f, 700f), P(17f, 550f), P(24f, 650f), P(30f, 600f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveRShrubLow = {
            P(0f, 0f), P(1.5f, 0f), P(3f, 0f), P(6f, 200f), P(12f, 200f), P(17f, 200f), P(24f, 200f), P(30f, 200f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveRShrubHigh = {
            P(0f, 50f), P(1.5f, 50f), P(3f, 50f), P(6f, 300f), P(12f, 275f), P(17f, 550f), P(24f, 500f), P(30f, 500f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveTreeLat = {
            P(0f, 1f), P(60f, 1f), P(66f, 0.9f), P(70f, 0.75f), P(80f, 0f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveShrubLat = {
            P(0f, 1f), P(70f, 1f), P(80f, 0.5f), P(1e9f, 0f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveTotalRain = {
            P(0f, 0f), P(30f, 0.005f), P(62.5f, 0.015f), P(100f, 0.05f), P(250f, 0.5f),
            P(350f, 0.9f), P(400f, 0.95f), P(500f, 0.975f), P(1000f, 1f), P(2500f, 1f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveTotalLat = {
            P(0f, 1f), P(60f, 1f), P(66f, 1f), P(70f, 0.7f), P(80f, 0.2f), P(1e9f, 0f)
        };
        static readonly GlebaClimateConfig.CurvePoint[] CurveTotalTemp = { P(0f, 0.1f), P(1.5f, 0.75f), P(3f, 1f) };
        static readonly GlebaClimateConfig.CurvePoint[] CurveBroadleafTemp = { P(3f, 0f), P(6f, 0.4f), P(10f, 0.8f), P(14f, 1f) };
        static readonly GlebaClimateConfig.CurvePoint[] SeaIceCurve = {
            P(-100f, 30f), P(-30f, 20f), P(-10f, 10f), P(-1f, 3f), P(0f, 0f)
        };

        // Wild founder crop preference curves from mods/base/data/plants.lua & PlantSpecies::get_preference_score_in (0x180a67c20)
        static readonly GlebaClimateConfig.CurvePoint[] PeaLatCurve = { P(20f, 0f), P(30f, 1f), P(40f, 1f), P(50f, 0.1f), P(66f, 0f) };
        static readonly GlebaClimateConfig.CurvePoint[] VetchLatCurve = { P(30f, 0f), P(35f, 0.1f), P(40f, 1f), P(45f, 0.1f), P(50f, 0.1f), P(62f, 0f) };
        static readonly GlebaClimateConfig.CurvePoint[] PeaVetchTempCurve = { P(0f, 0f), P(10f, 0.5f), P(14f, 1f), P(20f, 0.5f), P(30f, 0.25f), P(38f, 0f) };
        static readonly GlebaClimateConfig.CurvePoint[] TemperateCropRainCurve = { P(0f, 0f), P(20f, 0.1f), P(40f, 0.5f), P(70f, 1f), P(115f, 0.85f), P(165f, 0.40f) };
        static readonly GlebaClimateConfig.CurvePoint[] RyeLatCurve = { P(0f, 0f), P(24f, 0f), P(33f, 1f), P(54f, 1f), P(70f, 0f) };
        static readonly GlebaClimateConfig.CurvePoint[] RyeTempCurve = { P(0f, 0f), P(10f, 1f), P(19f, 1f), P(28f, 0.25f), P(36f, 0f) };
        static readonly GlebaClimateConfig.CurvePoint[] RiceLatCurve = { P(0f, 1f), P(29f, 1f), P(42f, 0f) };
        static readonly GlebaClimateConfig.CurvePoint[] RiceTempCurve = { P(0f, 0f), P(10f, 0.75f), P(30f, 1f), P(35f, 1f), P(50f, 0f) };
        static readonly GlebaClimateConfig.CurvePoint[] RiceRainCurve = { P(0f, 0f), P(40f, 0.1f), P(70f, 0.9f), P(100f, 1f) };
        static readonly GlebaClimateConfig.CurvePoint[] RicePermClayCurve = { P(0f, 0.75f), P(0.5f, 1f), P(1f, 0.75f) };

        static float Eval3(float x, float x0, float y0, float x1, float y1, float x2, float y2)
        {
            if (x <= x0) return y0;
            if (x < x1) return y0 + (y1 - y0) * FMath.Clamp((x - x0) / Math.Max(1e-6f, x1 - x0), 0f, 1f);
            if (x < x2) return y1 + (y2 - y1) * FMath.Clamp((x - x1) / Math.Max(1e-6f, x2 - x1), 0f, 1f);
            return y2;
        }

        static float Eval2(float x, float x0, float y0, float x1, float y1)
        {
            if (x <= x0) return y0;
            if (x >= x1) return y1;
            return y0 + (y1 - y0) * FMath.Clamp((x - x0) / Math.Max(1e-6f, x1 - x0), 0f, 1f);
        }

        /// <summary>
        /// Holdridge physiological biotemperature (TileData::get_biotemperature, 0x18097ca10):
        /// mean of monthly temperatures strictly inside [0, 30] C (months outside [0, 30] contribute 0,
        /// or clamp at 30 C when clampAbove30 is enabled).
        /// </summary>
        public static float ComputeBiotemperature(float[] monthlyTemperatures, int offset12, bool clampAbove30 = false)
        {
            float sum = 0f;
            for (int m = 0; m < 12; m++)
            {
                float t = monthlyTemperatures[offset12 + m];
                if (t < 0f) continue;
                if (t > 30f)
                {
                    if (clampAbove30) sum += 30f;
                    continue;
                }
                sum += t;
            }
            return sum / 12f;
        }

        /// <summary>
        /// Holdridge latitude- and warmth-dependent alpine treeline canopy multiplier [0.05, 1.0]
        /// from CurveTreelineHigh (0x1822d9c90). Equals 1.0 below (eHigh - 1400 m).
        /// </summary>
        public static float ComputeTreelineMultiplier(float biotemperatureC, float elevationMeters)
        {
            float eHigh = GlebaClimateConfig.EvaluateCurve(CurveTreelineHigh, biotemperatureC);
            float eMid = biotemperatureC < 0f ? 350f : (eHigh - 650f);
            return Eval3(elevationMeters, eMid - 750f, 1f, eMid, 0.5f, eMid + 500f, 0.05f);
        }

        /// <summary>
        /// Evaluates Gleba's Holdridge + savanna + alpine treeline plant cover fractions.
        /// Noise inputs are optional unit perturbations in [-1, 1] (pass 0 for deterministic mean).
        /// </summary>
        public static GlebaPlantCover EvaluateFlora(
            bool isLand, float iceThickness, float elevationMeters, float absLatitudeDegrees,
            float biotemperatureC, float annualRainMm, float minMonthlyRainMm, float minMonthlyTempC,
            float fbmTrees = 0f, float fbmGrass = 0f, float fbmShrubs = 0f, float fbmTotal = 0f, float fbmBroadleaf = 0f)
        {
            var result = new GlebaPlantCover();
            if (!isLand || iceThickness > 0f) return result;

            float eHigh = GlebaClimateConfig.EvaluateCurve(CurveTreelineHigh, biotemperatureC);
            float eMid = biotemperatureC < 0f ? 350f : (eHigh - 650f);

            float treeTempMult = GlebaClimateConfig.EvaluateCurve(CurveTreeShrubTemp, biotemperatureC);
            float r1 = GlebaClimateConfig.EvaluateCurve(CurveRTreeLow, biotemperatureC);
            float r2 = GlebaClimateConfig.EvaluateCurve(CurveRTreeMid, biotemperatureC);
            if (r2 < r1) r2 = r1 + 50f;
            float r3 = GlebaClimateConfig.EvaluateCurve(CurveRTreeHigh, biotemperatureC);
            if (r3 < r2) r3 = r2 + 50f;
            float treeRain = Eval3(annualRainMm, r1, 0.01f, r2, 0.55f, r3, 0.99f);

            float grassTempMult = GlebaClimateConfig.EvaluateCurve(CurveGrassTemp, biotemperatureC);
            float g1 = GlebaClimateConfig.EvaluateCurve(CurveRGrassLow, biotemperatureC);
            float g2 = GlebaClimateConfig.EvaluateCurve(CurveRGrassMid, biotemperatureC);
            if (g2 < g1) g2 = g1 + 50f;
            float g3 = GlebaClimateConfig.EvaluateCurve(CurveRGrassHigh, biotemperatureC);
            if (g3 < g2) g3 = g2 + 50f;
            float grassRain = Eval3(annualRainMm, g1, 0.20f, g2, 1.00f, g3, 0.10f);

            float shrubTempMult = treeTempMult;
            float s1 = GlebaClimateConfig.EvaluateCurve(CurveRShrubLow, biotemperatureC);
            float s2 = GlebaClimateConfig.EvaluateCurve(CurveRShrubHigh, biotemperatureC);
            if (s2 < s1) s2 = s1 + 50f;
            float shrubRain = Eval2(annualRainMm, s1, 0.99f, s2, 0.05f);

            float treeLatMult = GlebaClimateConfig.EvaluateCurve(CurveTreeLat, absLatitudeDegrees);
            float shrubLatMult = GlebaClimateConfig.EvaluateCurve(CurveShrubLat, absLatitudeDegrees);
            float treeElevMult = Eval3(elevationMeters, eMid - 750f, 1f, eMid, 0.5f, eMid + 500f, 0.05f);

            float totalRainBase = GlebaClimateConfig.EvaluateCurve(CurveTotalRain, annualRainMm);
            float totalLatMult = GlebaClimateConfig.EvaluateCurve(CurveTotalLat, absLatitudeDegrees);
            float totalTempMult = GlebaClimateConfig.EvaluateCurve(CurveTotalTemp, biotemperatureC);
            float totalElevMult;
            if (elevationMeters <= eHigh - 600f) totalElevMult = 1f;
            else if (elevationMeters < eHigh) totalElevMult = Eval2(elevationMeters, eHigh - 600f, 1f, eHigh, 0.5f);
            else if (elevationMeters < eHigh + 300f) totalElevMult = Eval2(elevationMeters, eHigh, 0.5f, eHigh + 300f, 0.4f);
            else if (elevationMeters < eHigh + 1000f) totalElevMult = Eval2(elevationMeters, eHigh + 300f, 0.4f, eHigh + 1000f, 0.3f);
            else totalElevMult = Eval2(elevationMeters, eHigh + 1000f, 0.3f, eHigh + 2000f, 0.2f);

            float treesBase = FMath.Clamp(treeTempMult * (treeRain + 0.1f * fbmTrees), 0.015f, 1f) * treeLatMult;
            float grassBase = FMath.Clamp(grassTempMult * (grassRain + 0.1f * fbmGrass), 0.075f, 1f);
            float shrubsBase = FMath.Clamp(shrubTempMult * (shrubRain + 0.1f * fbmShrubs), 0.05f, 1f) * shrubLatMult;

            // Alpine treeline suppresses trees and converts lost tree share into alpine meadow grass
            float treesElev = treeElevMult * treesBase;
            float grassAlpine = FMath.Clamp(grassBase + (treesBase - treesElev), 0f, 1f);

            // Tropical dry-season savanna transfer (P_min < 60 mm & T_min > 15.5 C)
            float dryRatio = Math.Max(minMonthlyRainMm / 60f, 0f);
            float droughtStress = dryRatio > 1f ? 0f : (1f - Math.Max(dryRatio, 0.25f));
            float tropicalWarmth = 1f - FMath.Clamp((19f - minMonthlyTempC) / 3.5f, 0f, 1f);
            float savannaFrac = Math.Min(droughtStress, tropicalWarmth);

            float treesClamped = FMath.Clamp(treesElev, 0f, 1f);
            float shrubsClamped = FMath.Clamp(shrubsBase, 0f, 1f);
            float savannaLoss = savannaFrac * treesClamped;

            float treesRaw = treesClamped - savannaLoss;
            float shrubsRaw = shrubsClamped + 0.5f * savannaLoss;
            float grassRaw = grassAlpine + 0.5f * savannaLoss;

            float sum = treesRaw + shrubsRaw + grassRaw;
            if (sum > 1e-8f)
            {
                result.Trees = treesRaw / sum;
                result.Shrubs = shrubsRaw / sum;
                result.Grass = grassRaw / sum;
            }

            result.TotalPlants = FMath.Clamp(totalRainBase + 0.1f * fbmTotal, 0f, 1f) * totalLatMult * totalTempMult * totalElevMult;

            float elevBroadleaf = Eval2(elevationMeters, eMid - 1200f, 1f, eMid, 0f);
            float tempBroadleaf = GlebaClimateConfig.EvaluateCurve(CurveBroadleafTemp, biotemperatureC);
            float broadMult = 0.9f + 0.1f * fbmBroadleaf;
            result.ConifersVsBroadleaf = FMath.Clamp(Math.Min(elevBroadleaf, tempBroadleaf) * broadMult, 0f, 1f);
            return result;
        }

        /// <summary>
        /// Evaluates Gleba's priority-sorted biome rules (mods/base/data/biomes.lua).
        /// </summary>
        public static GlebaBiomeKind ClassifyGlebaBiome(
            bool aquatic, bool lake, bool freshwater, float iceThickness,
            float elevationMeters, float janTempC, float julTempC, float janRainMm, float julRainMm,
            Koppen koppen, in GlebaPlantCover flora, bool marsh = false)
            => ClassifyGlebaBiome(aquatic, lake, freshwater, iceThickness, elevationMeters,
                Math.Min(janTempC, julTempC), Math.Max(janTempC, julTempC), (janTempC + julTempC) * .5f,
                koppen, in flora, marsh);

        // Köppen already integrates all twelve months, including the dry season and aridity threshold.
        public static GlebaBiomeKind ClassifyGlebaBiome(
            bool aquatic, bool lake, bool freshwater, float iceThickness, float elevationMeters,
            float coldest, float warmest, float meanTemperature, Koppen koppen, in GlebaPlantCover flora, bool marsh = false)
        {
            bool icy = iceThickness > 0.001f;
            if (aquatic)
            {
                if (!lake) return icy ? GlebaBiomeKind.GlaciatedSea : GlebaBiomeKind.Sea;
                if (freshwater) return icy ? GlebaBiomeKind.GlaciatedSweetwaterLake : GlebaBiomeKind.SweetwaterLake;
                return icy ? GlebaBiomeKind.GlaciatedSaltwaterLake : GlebaBiomeKind.SaltwaterLake;
            }
            if (icy) return GlebaBiomeKind.Glacier;

            float dead = flora.DeadLand;
            float trees = flora.Trees;
            float conifer = flora.ConiferFraction;

            // Priority 0..301 ascending (last match wins)
            GlebaBiomeKind chosen = GlebaBiomeKind.RockyWasteland;
            if (coldest <= 0f && warmest <= 15f && meanTemperature <= 0f)
                chosen = GlebaBiomeKind.ArcticWasteland; // p=10
            if (dead >= 0.50f && coldest >= 0f) chosen = GlebaBiomeKind.DesertsAndXericShrublands; // p=20

            if (dead <= 0.50f)
            {
                bool wooded = trees >= .55f;
                switch (koppen)
                {
                    case Koppen.Csa: case Koppen.Csb: case Koppen.Csc:
                        chosen = GlebaBiomeKind.MediterraneanForestWoodlandAndScrub; break;
                    case Koppen.Aw:
                        chosen = wooded ? GlebaBiomeKind.DryJungle : GlebaBiomeKind.TropicalGrasslandsSavannasAndShrublands; break;
                    case Koppen.Af: case Koppen.Am:
                        chosen = !wooded ? GlebaBiomeKind.HumidTropicalGrassland :
                            koppen == Koppen.Af ? GlebaBiomeKind.WetJungle : GlebaBiomeKind.DryJungle; break;
                    case Koppen.BSh: case Koppen.BSk:
                        chosen = GlebaBiomeKind.SemiAridSteppe; break;
                    case Koppen.BWh: case Koppen.BWk:
                        chosen = GlebaBiomeKind.DesertsAndXericShrublands; break;
                    default:
                        chosen = !wooded ? GlebaBiomeKind.TemperateGrasslandsSavannasAndShrublands :
                            conifer >= .65f ? GlebaBiomeKind.TemperateConiferousForest : GlebaBiomeKind.TemperateBroadleafAndMixedForest; break;
                }
            }

            if (elevationMeters >= 2500f && trees <= 0.55f && dead <= 0.90f)
                chosen = GlebaBiomeKind.MontaneGrasslandsAndShrublands; // p=50

            // Wetland biomes (biomes.lua: bog / marsh / swamp, priority 50)
            // A closed perhumid rainforest can have saturated soil without losing its canopy.
            if (marsh && dead <= 0.65f && chosen != GlebaBiomeKind.WetJungle)
            {
                if (meanTemperature < 6f) chosen = GlebaBiomeKind.Bog;
                else if (flora.Grass >= 0.50f) chosen = GlebaBiomeKind.Marsh;
                else chosen = GlebaBiomeKind.Swamp;
            }

            if (coldest <= 0f && warmest <= 15f && meanTemperature <= 0f && dead < 0.72f)
                chosen = GlebaBiomeKind.Tundra; // p=300

            if (trees >= 0.55f && dead <= 0.50f &&
                (koppen == Koppen.ET || koppen == Koppen.Dfc || koppen == Koppen.Dwc || koppen == Koppen.Dsc))
                chosen = GlebaBiomeKind.Taiga; // p=301

            return chosen;
        }

        public static void GetGlebaBiomeMapColor(GlebaBiomeKind kind, out byte r, out byte g, out byte b)
        {
            switch (kind)
            {
                case GlebaBiomeKind.DesertsAndXericShrublands:             r = 247; g = 232; b = 186; return;
                case GlebaBiomeKind.RockyWasteland:                        r = 148; g = 132; b = 116; return;
                case GlebaBiomeKind.ArcticWasteland:                       r = 108; g = 112; b = 118; return;
                case GlebaBiomeKind.Sea:                                   r =  28; g =  64; b =  98; return;
                case GlebaBiomeKind.GlaciatedSea:                          r = 196; g = 228; b = 242; return;
                case GlebaBiomeKind.SweetwaterLake:                        r =  74; g = 158; b = 204; return;
                case GlebaBiomeKind.GlaciatedSweetwaterLake:               r = 178; g = 218; b = 238; return;
                case GlebaBiomeKind.SaltwaterLake:                         r =  58; g = 128; b = 148; return;
                case GlebaBiomeKind.GlaciatedSaltwaterLake:                r = 160; g = 204; b = 216; return;
                case GlebaBiomeKind.Glacier:                               r = 236; g = 240; b = 245; return;
                case GlebaBiomeKind.TemperateConiferousForest:             r =  18; g =  96; b =  42; return;
                case GlebaBiomeKind.TemperateBroadleafAndMixedForest:      r = 113; g = 200; b =  55; return;
                case GlebaBiomeKind.DryJungle:                             r = 212; g = 170; b =  24; return;
                case GlebaBiomeKind.WetJungle:                             r =  48; g = 124; b =  38; return;
                case GlebaBiomeKind.Taiga:                                 r =  44; g = 160; b =  90; return;
                case GlebaBiomeKind.TemperateGrasslandsSavannasAndShrublands: r = 205; g = 222; b = 135; return;
                case GlebaBiomeKind.TropicalGrasslandsSavannasAndShrublands:  r = 255; g = 216; b =  82; return;
                case GlebaBiomeKind.MediterraneanForestWoodlandAndScrub:   r = 200; g = 113; b =  55; return;
                case GlebaBiomeKind.HumidTropicalGrassland:                r = 113; g = 185; b = 102; return;
                case GlebaBiomeKind.SemiAridSteppe:                        r = 187; g = 172; b = 112; return;
                case GlebaBiomeKind.MontaneGrasslandsAndShrublands:        r = 192; g = 168; b = 228; return;
                case GlebaBiomeKind.Tundra:                                r = 135; g = 218; b = 202; return;
                case GlebaBiomeKind.Bog:                                   r = 178; g = 122; b = 196; return;
                case GlebaBiomeKind.Marsh:                                 r = 206; g = 138; b = 222; return;
                case GlebaBiomeKind.Swamp:                                 r = 138; g = 104; b = 172; return;
                default:                                                   r = 128; g = 128; b = 128; return;
            }
        }

        public static string GetGlebaBiomeLabel(GlebaBiomeKind kind)
        {
            switch (kind)
            {
                case GlebaBiomeKind.RockyWasteland: return "寒旱裸岩荒原 (Rocky Wasteland)";
                case GlebaBiomeKind.DesertsAndXericShrublands: return "荒漠与旱生灌丛 (Desert)";
                case GlebaBiomeKind.Sea: return "海洋 (Sea)";
                case GlebaBiomeKind.GlaciatedSea: return "海冰封冻海域 (Glaciated Sea)";
                case GlebaBiomeKind.SweetwaterLake: return "外流淡水湖 (Sweetwater Lake)";
                case GlebaBiomeKind.GlaciatedSweetwaterLake: return "冰封淡水湖 (Glaciated Lake)";
                case GlebaBiomeKind.SaltwaterLake: return "内流咸水湖 (Saltwater Lake)";
                case GlebaBiomeKind.GlaciatedSaltwaterLake: return "冰封咸水湖 (Glaciated Salt Lake)";
                case GlebaBiomeKind.Glacier: return "大陆冰川/冰盖 (Glacier)";
                case GlebaBiomeKind.TemperateConiferousForest: return "温带针叶林 (Temperate Conifer)";
                case GlebaBiomeKind.TemperateBroadleafAndMixedForest: return "温带阔叶混交林 (Temperate Forest)";
                case GlebaBiomeKind.DryJungle: return "热带季雨林/旱林 (Dry Jungle)";
                case GlebaBiomeKind.TemperateGrasslandsSavannasAndShrublands: return "温带草原 (Steppe)";
                case GlebaBiomeKind.TropicalGrasslandsSavannasAndShrublands: return "热带稀树草原 (Savanna)";
                case GlebaBiomeKind.MediterraneanForestWoodlandAndScrub: return "地中海硬叶林灌丛 (Mediterranean)";
                case GlebaBiomeKind.HumidTropicalGrassland: return "湿热草地与灌丛";
                case GlebaBiomeKind.SemiAridSteppe: return "半干旱草原与灌丛";
                case GlebaBiomeKind.MontaneGrasslandsAndShrublands: return "高山草甸灌丛 (Montane)";
                case GlebaBiomeKind.WetJungle: return "热带湿润雨林 (Moist Jungle)";
                case GlebaBiomeKind.Tundra: return "极地苔原 (Tundra)";
                case GlebaBiomeKind.Taiga: return "北方针叶林/泰加林 (Taiga)";
                case GlebaBiomeKind.ArcticWasteland: return "极地冰缘寒漠 (Arctic Wasteland)";
                case GlebaBiomeKind.Bog: return "寒温带泥炭沼泽 (Bog)";
                case GlebaBiomeKind.Marsh: return "草本沼泽湿地 (Marsh)";
                case GlebaBiomeKind.Swamp: return "林木沼泽湿地 (Swamp)";
                default: return kind.ToString();
            }
        }

        /// <summary>
        /// Sea-ice thickness in meters (0..30 m) from annual mean water temperature (glacier::assign_sea_ice).
        /// </summary>
        public static float ComputeSeaIceMeters(bool isWater, float meanTemperatureC)
            => isWater ? GlebaClimateConfig.EvaluateCurve(SeaIceCurve, meanTemperatureC) : 0f;

        /// <summary>
        /// Present-day or LGM grounded glacier thickness in meters (glacier::assign_grounded_ice).
        /// When bedrockElevation is false (default for surface DEMs whose ice domes already include ice height),
        /// subglacial bedrock is estimated so polar ice sheets (Antarctica, Greenland) retain realistic thickness.
        /// </summary>
        public static float ComputeGroundedIceMeters(
            bool isLand, float elevationMeters, float warmestMonthTempC, float meanMonthlyRainMm,
            float tempOffsetC = 0f, float noiseCoolingC = 0f, bool bedrockElevation = false)
        {
            if (elevationMeters <= -500f) return 0f;
            float tEff = warmestMonthTempC + tempOffsetC - Math.Max(0f, noiseCoolingC);
            if (tEff >= 0f) return 0f;

            float c = FMath.Clamp(tEff, -30f, 0f) / -30f;
            float rainMult = isLand ? 75f : 10f;
            float baseIce = isLand ? 500f : 100f;
            float bias = isLand ? -2.5f : -0.25f;
            float minIce = isLand ? 2.5f : 0.25f;

            float ice = (1f + 4f * c) * (((meanMonthlyRainMm * rainMult + baseIce + bias) * c) + minIce);
            if (ice > 4500f) ice = 4500f + (float)Math.Sqrt(ice - 4500f);

            if (!isLand)
            {
                float minH = Math.Max(-0.11870129f * elevationMeters, 0f);
                float maxCurve;
                if (elevationMeters <= -3000f) maxCurve = minH + 20f;
                else if (elevationMeters < -800f) maxCurve = Eval2(elevationMeters, -3000f, minH + 20f, -800f, 120f);
                else if (elevationMeters < -400f) maxCurve = Eval2(elevationMeters, -800f, 120f, -400f, 115f);
                else if (elevationMeters < -200f) maxCurve = Eval2(elevationMeters, -400f, 115f, -200f, 110f);
                else if (elevationMeters < -100f) maxCurve = Eval2(elevationMeters, -200f, 110f, -100f, 120f);
                else if (elevationMeters < -50f) maxCurve = Eval2(elevationMeters, -100f, 120f, -50f, 140f);
                else maxCurve = Eval2(elevationMeters, -50f, 140f, 0f, 175f);

                float maxH = Math.Max(minH + 1f, maxCurve);
                ice = FMath.Clamp(ice, minH, maxH);
            }

            if (ice <= 0f) return 0f;
            float posElev = Math.Max(elevationMeters, 0f);
            if (bedrockElevation || !isLand)
            {
                float low = ice - 0.75f * posElev;
                return Math.Max(low + 0.05f * (ice - low), 0f);
            }
            float effectiveBed = Math.Min(posElev, 600f + 0.28f * Math.Max(0f, posElev - 600f));
            float lowSurf = ice - 0.75f * effectiveBed;
            float damped = Math.Max(lowSurf + 0.05f * (ice - lowSurf), 0f);
            float domeThickness = 0.72f * Math.Max(0f, posElev - 600f) * FMath.Clamp(-tEff / 10f, 0f, 1f);
            return Math.Max(Math.Max(damped, domeThickness), 25f + 300f * c);
        }

        // --- Stage 49 6-Pass Soil Synthesis (soil::initialize_soils, glacier_loess, aeolian_deposits, thin_mountain_soils, transport_sediments) ---

        public static GlebaSoilState InitializeTileSoil(
            float rockSand, float rockClay, float rockSilt, float rockWeatheringRate, float rockMineralRichness,
            bool coastalBeach, float annualRainMm,
            float n0 = 0f, float n1 = 0f, float n2 = 0f, float n3 = 0f, float n4 = 0f, float n5 = 0f)
        {
            var s = new GlebaSoilState();
            float sandBonus = coastalBeach ? 0.10f : 0.0f;
            s.Sand = Math.Max(rockSand + 0.05f * n0, 0f) + sandBonus;
            s.Silt = Math.Max(rockSilt + 0.05f * n1, 0f);
            s.Clay = Math.Max(rockClay + 0.05f * n2, 0f);
            s.NormalizeTexture();

            float rainFactor = FMath.Clamp((float)Math.Sqrt(2.0f * (annualRainMm / 12.0f)), 0.001f, 20.0f);
            s.Depth = Math.Max(rainFactor * rockWeatheringRate * 0.5f + 0.25f * n3, 1e-4f);
            s.MineralRichness = FMath.Clamp(rockMineralRichness + 0.05f * n4, 0f, 1f);
            s.Organics = FMath.Clamp(0.5f * n5 + 0.5f, 0f, 1f);
            return s;
        }

        public static void ApplyGlacierLoess(ref GlebaSoilState s, bool isLand, float lgmIceMeters, float elevationMeters)
        {
            if (isLand && lgmIceMeters > 0f && elevationMeters < 1500f)
            {
                float sum = Math.Max(s.Sand + s.Clay + s.Silt, 1e-5f);
                s.Sand = 0.5f * (s.Sand / sum);
                s.Clay = (s.Clay / sum) + 2.5f;
                s.Silt = 2.1f * (s.Silt / sum);
                s.Depth = 1.5f * (s.Depth + 2.5f);
            }
            s.NormalizeTexture();
        }

        public static void ApplyAeolianDeflation(ref GlebaSoilState s, bool isLand, float elevationMeters, float annualRainMm)
        {
            if (!isLand || elevationMeters >= 1500f) return;
            float rMo = annualRainMm / 12.0f;
            if (rMo < 20.0f)
            {
                float keepFines = Math.Max(rMo / 20.0f, 0f);
                s.Clay *= keepFines;
                s.Silt *= keepFines;
                s.NormalizeTexture();
            }
        }

        public static void ThinMountainSoil(ref GlebaSoilState s, float elevationMeters, float gradientMeters)
        {
            float elevF = FMath.Clamp(elevationMeters / 4000.0f, 0f, 1f);
            float thin = FMath.Clamp(((gradientMeters - 250.0f) * elevF) / 750.0f, 0f, 1f);
            s.Depth = Math.Max((1.0f - thin) * s.Depth, 1e-5f);
        }

        public static void StepFluvialSediment(
            int u, int downstreamV, float avgMonthlyRainMm, float avgMonthlyHumidity,
            float[] inflow, float[] outDischarge, GlebaSoilState[] soils,
            float[] sedSand, float[] sedSilt, float[] sedClay, float[] sedMinerals,
            bool floodplainOrDelta = false)
        {
            float h = FMath.Clamp(avgMonthlyHumidity, 1e-4f, 1.0f);
            float rawQ = avgMonthlyRainMm + inflow[u] - 10.0f;
            float evap = (float)Math.Sqrt(Math.Max(rawQ, 0f)) / h;
            float q = Math.Max(inflow[u], Math.Max(rawQ - evap, 0f));
            outDischarge[u] = q;
            if (downstreamV >= 0) inflow[downstreamV] += q;

            float erodeFrac = FMath.Clamp(((float)Math.Pow(q, 0.25) * 1.2f) / 35.0f, 0f, 1f) * (floodplainOrDelta ? 0.35f : 1.0f);
            float erodedDepth = soils[u].Depth * erodeFrac;
            soils[u].Depth -= erodedDepth;

            if (downstreamV >= 0)
            {
                sedSand[downstreamV] += erodedDepth * soils[u].Sand;
                sedSilt[downstreamV] += erodedDepth * soils[u].Silt;
                sedClay[downstreamV] += erodedDepth * soils[u].Clay;
                sedMinerals[downstreamV] += erodedDepth * soils[u].MineralRichness;
            }

            float inTotal = sedSand[u] + sedSilt[u] + sedClay[u];
            float inMinConc = inTotal > 0f ? (sedMinerals[u] / inTotal) : 0f;
            // Steep reaches drop coarse sand while carrying fines; low-gradient floodplains & deltas settle suspended silt and clay
            float rSand = floodplainOrDelta ? 0.05f : 0.10f;
            float rSilt = floodplainOrDelta ? 0.24f : 0.02f;
            float rClay = floodplainOrDelta ? 0.16f : 0.01f;
            float depSand = rSand * sedSand[u];
            float depSilt = rSilt * sedSilt[u];
            float depClay = rClay * sedClay[u];
            float depDepth = depSand + depSilt + depClay;

            if (downstreamV >= 0)
            {
                // Fluvial comminution grinds a fraction of transported coarse sand into alluvial silt down-valley
                float remSand = (1f - rSand) * sedSand[u];
                float groundToSilt = 0.06f * remSand;
                sedSand[downstreamV] += remSand - groundToSilt;
                sedSilt[downstreamV] += (1f - rSilt) * sedSilt[u] + groundToSilt;
                sedClay[downstreamV] += (1f - rClay) * sedClay[u];
                sedMinerals[downstreamV] += (inTotal - depDepth) * inMinConc;
            }

            float oldD = soils[u].Depth;
            float newD = Math.Max(oldD + depDepth, 1e-5f);
            soils[u].MineralRichness = FMath.Clamp((oldD * soils[u].MineralRichness + depDepth * inMinConc) / newD, 0f, 1f);
            soils[u].Sand = (oldD * soils[u].Sand + depSand) / newD;
            soils[u].Silt = (oldD * soils[u].Silt + depSilt) / newD;
            soils[u].Clay = (oldD * soils[u].Clay + depClay) / newD;
            soils[u].NormalizeTexture();
            soils[u].Depth = oldD + depDepth;
        }

        /// <summary>
        /// Stage 49 Pass 4 (`soil::blur_soils`, 0x1808ed640): Depth-weighted Jacobi diffusion of
        /// sand, clay, silt, and mineral richness across adjacent land tiles (leaving organics and coastlines intact).
        /// </summary>
        public static void BlurLandSoils(WorldData world, GlebaSoilState[] soils, int iterations = 4, float strength = 0.65f)
        {
            if (world == null || soils == null || iterations <= 0 || strength <= 0f) return;
            int n = world.Count;
            var next = new GlebaSoilState[n];
            for (int iter = 0; iter < iterations; iter++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (FlatTerrainAnalysis.AreaWater(world.Water[i])) continue;
                    var s = soils[i];
                    float d0 = Math.Max(s.Depth, 1e-5f);
                    float depthSum = d0;
                    float weightSum = 1f;
                    float sandSum = d0 * s.Sand;
                    float claySum = d0 * s.Clay;
                    float siltSum = d0 * s.Silt;
                    float minSum = d0 * s.MineralRichness;

                    for (int d = 0; d < 4; d++)
                    {
                        int nb = world.SphereNeighbor(i, d);
                        if (FlatTerrainAnalysis.AreaWater(world.Water[nb])) continue;
                        var sn = soils[nb];
                        float dn = Math.Max(sn.Depth, 1e-5f) * strength;
                        depthSum += dn;
                        weightSum += strength;
                        sandSum += dn * sn.Sand;
                        claySum += dn * sn.Clay;
                        siltSum += dn * sn.Silt;
                        minSum += dn * sn.MineralRichness;
                    }

                    float invD = 1f / Math.Max(depthSum, 1e-5f);
                    var dst = new GlebaSoilState
                    {
                        Sand = sandSum * invD,
                        Clay = claySum * invD,
                        Silt = siltSum * invD,
                        Depth = depthSum / weightSum,
                        MineralRichness = FMath.Clamp(minSum * invD, 0f, 1f),
                        Organics = s.Organics
                    };
                    dst.NormalizeTexture();
                    next[i] = dst;
                }
                Array.Copy(next, soils, n);
            }
        }

        /// <summary>
        /// Exact `obj.civ_spawning_block = true` biome table from Gleba v0.3.6 `mods/base/data/biomes.lua`.
        /// </summary>
        public static bool IsBiomeCivSpawningBlocked(GlebaBiomeKind biome)
        {
            switch (biome)
            {
                case GlebaBiomeKind.DesertsAndXericShrublands:
                case GlebaBiomeKind.RockyWasteland:
                case GlebaBiomeKind.ArcticWasteland:
                case GlebaBiomeKind.Tundra:
                case GlebaBiomeKind.Sea:
                case GlebaBiomeKind.GlaciatedSea:
                case GlebaBiomeKind.SweetwaterLake:
                case GlebaBiomeKind.GlaciatedSweetwaterLake:
                case GlebaBiomeKind.SaltwaterLake:
                case GlebaBiomeKind.GlaciatedSaltwaterLake:
                case GlebaBiomeKind.Glacier:
                    return true;
                default:
                    return false;
            }
        }

        public static string GetFounderCropLabel(GlebaFounderCrop crop)
        {
            switch (crop)
            {
                case GlebaFounderCrop.WildPea: return "野生豌豆 → 豌豆 (固氮 0.70)";
                case GlebaFounderCrop.WildBitterVetch: return "野生苦野豌豆 → 苦野豌豆 (固氮 0.15)";
                case GlebaFounderCrop.WildRye: return "野生黑麦 → 黑麦";
                case GlebaFounderCrop.WildRice: return "普通野生稻 → 粳稻/籼稻";
                case GlebaFounderCrop.AzollaCompanion: return "野生稻 + 满江红固氮共生系 (固氮 1.00)";
                default: return "无原生起源农作物";
            }
        }

        /// <summary>
        /// Evaluates Stage 54 domesticable wild founder crops (`plants.lua` + `PlantSpecies::get_preference_score_in`, 0x180a67c20)
        /// and Stages 61-62 settlement / carrying-capacity score (`find_civilization_spots`, 0x180a55060).
        /// </summary>
        public static GlebaCropEvaluation EvaluateFounderCropsAndCarryingCapacity(
            bool isLand, bool isCoast, float elevationMeters, float absLatitudeDegrees,
            float meanAnnualTempC, float annualRainMm, float relativeHumidity,
            float erosionWaterflow, GlebaBiomeKind biome, in GlebaSoilState soil)
        {
            var eval = new GlebaCropEvaluation();
            if (!isLand || elevationMeters < 0f || soil.Depth <= 1e-4f) return eval;

            float perm = FMath.Clamp(soil.GetPermeability(), 0f, 1f);
            float clay = FMath.Clamp(soil.Clay, 0f, 1f);
            float minRich = FMath.Clamp(soil.MineralRichness, 0f, 1f);
            float rainMo = annualRainMm / 12f;
            // Alluvial river floodplains supply natural flood-recession moisture alongside local rainfall
            float riverIrrigMo = erosionWaterflow > 12f
                ? Math.Min(52f, 2.8f * (float)Math.Sqrt(Math.Max(0f, erosionWaterflow - 12f)))
                : 0f;
            float effRainMo = rainMo + riverIrrigMo;

            // 6-curve multiplicative preference scores matching PlantSpecies::get_preference_score_in (0x180a67c20)
            float tempRain = GlebaClimateConfig.EvaluateCurve(TemperateCropRainCurve, effRainMo);
            float peaVetchTemp = GlebaClimateConfig.EvaluateCurve(PeaVetchTempCurve, meanAnnualTempC);

            eval.PeaScore = GlebaClimateConfig.EvaluateCurve(PeaLatCurve, absLatitudeDegrees)
                          * peaVetchTemp * tempRain * minRich;
            eval.BitterVetchScore = GlebaClimateConfig.EvaluateCurve(VetchLatCurve, absLatitudeDegrees)
                                  * peaVetchTemp * tempRain * minRich;
            eval.RyeScore = GlebaClimateConfig.EvaluateCurve(RyeLatCurve, absLatitudeDegrees)
                          * GlebaClimateConfig.EvaluateCurve(RyeTempCurve, meanAnnualTempC)
                          * tempRain * minRich;
            eval.RiceScore = GlebaClimateConfig.EvaluateCurve(RiceLatCurve, absLatitudeDegrees)
                           * GlebaClimateConfig.EvaluateCurve(RiceTempCurve, meanAnnualTempC)
                           * GlebaClimateConfig.EvaluateCurve(RiceRainCurve, effRainMo)
                           * GlebaClimateConfig.EvaluateCurve(RicePermClayCurve, perm)
                           * GlebaClimateConfig.EvaluateCurve(RicePermClayCurve, clay)
                           * minRich;

            // Azolla (Fairy moss) paddy nitrogen-fixing companion thrives in warm, high-water/wetland rice zones
            bool wetPaddy = effRainMo >= 75f || erosionWaterflow >= 35f ||
                            biome == GlebaBiomeKind.Marsh || biome == GlebaBiomeKind.Swamp;
            eval.AzollaScore = (wetPaddy && meanAnnualTempC >= 12f && eval.RiceScore > 0.08f)
                ? FMath.Clamp(eval.RiceScore * FMath.Clamp((effRainMo - 55f) / 45f, 0.35f, 1f), 0f, 1f)
                : 0f;

            float best = 0.02f;
            GlebaFounderCrop dom = GlebaFounderCrop.None;
            if (eval.PeaScore > best) { best = eval.PeaScore; dom = GlebaFounderCrop.WildPea; }
            if (eval.BitterVetchScore > best) { best = eval.BitterVetchScore; dom = GlebaFounderCrop.WildBitterVetch; }
            if (eval.RyeScore > best) { best = eval.RyeScore; dom = GlebaFounderCrop.WildRye; }
            if (eval.RiceScore > best)
            {
                best = eval.RiceScore;
                dom = eval.AzollaScore >= 0.25f ? GlebaFounderCrop.AzollaCompanion : GlebaFounderCrop.WildRice;
            }
            eval.DominantCrop = dom;
            eval.MaxSuitability = dom == GlebaFounderCrop.None ? 0f : FMath.Clamp(best, 0f, 1f);

            // Aggregate nitrogen-fixing potential from Wild Pea (0.70), Bitter Vetch (0.15), and Azolla (1.00)
            eval.NitrogenFixing = FMath.Clamp(
                Math.Max(0.70f * eval.PeaScore + 0.15f * eval.BitterVetchScore, eval.AzollaScore),
                0f, 1f);

            // Stage 61-62 settlement / carrying-capacity score (`find_civilization_spots`, 0x180a55060)
            bool civBlocked = IsBiomeCivSpawningBlocked(biome) && riverIrrigMo < 22f;
            if (biome == GlebaBiomeKind.Glacier || biome == GlebaBiomeKind.ArcticWasteland || meanAnnualTempC < -4f)
            {
                eval.CarryingCapacity = 0f;
                return eval;
            }

            float soilTerm = 0.52f * minRich + 0.26f * perm + 0.22f * FMath.Clamp(soil.Depth / 3.0f, 0f, 1f);
            float riverBonus = erosionWaterflow > 1f
                ? FMath.Clamp((float)Math.Log(1.0 + 2.0 * erosionWaterflow) / 13.5f, 0f, 0.42f)
                : 0f;
            float coastBonus = isCoast ? 0.16f : 0f; // Gleba +0.33 raw weight normalized to [0,1]
            float elevPenalty = FMath.Clamp(Math.Max(0f, elevationMeters - 400f) / 3600f, 0f, 0.65f);
            float tempPenalty = 0.085f * (float)Math.Sqrt(Math.Abs(meanAnnualTempC - 16.5f));
            float rainPenalty = 0.045f * (float)Math.Sqrt(Math.Abs(effRainMo - 85f));
            float humPenalty = 0.05f * (float)Math.Sqrt(Math.Abs(FMath.Clamp(relativeHumidity, 0f, 1f) - 0.62f));
            float cropSynergy = 0.34f * eval.MaxSuitability + 0.12f * eval.NitrogenFixing;

            float rawCap = soilTerm + riverBonus + coastBonus + cropSynergy - elevPenalty - tempPenalty - rainPenalty - humPenalty;
            if (civBlocked) rawCap *= 0.22f;
            eval.CarryingCapacity = FMath.Clamp(rawCap, 0f, 1f);
            return eval;
        }

        /// <summary>
        /// Evaluates deterministic single-tile Gleba soil state (bedrock base + LGM loess + aeolian deflation + mountain thinning + alluvial silt).
        /// </summary>
        public static GlebaSoilState EvaluateLocalSoil(
            bool isLand, bool coastalBeach, float elevationMeters, float gradientMeters,
            float annualRainMm, float warmestMonthTempC, float totalPlants, float riverSediment = 0f)
        {
            if (!isLand) return default;
            var soil = InitializeTileSoil(0.34f, 0.32f, 0.34f, 0.45f, 0.55f, coastalBeach, annualRainMm);
            // Last Glacial Maximum (-6 C -> -9 C amplified) loess enrichment in glaciated/periglacial lowlands
            float lgmIce = ComputeGroundedIceMeters(true, elevationMeters, warmestMonthTempC, annualRainMm / 12f, tempOffsetC: -9f);
            ApplyGlacierLoess(ref soil, true, lgmIce, elevationMeters);
            ApplyAeolianDeflation(ref soil, true, elevationMeters, annualRainMm);
            ThinMountainSoil(ref soil, elevationMeters, gradientMeters);
            if (riverSediment > 0f)
            {
                float alluvial = FMath.Clamp(riverSediment, 0f, 0.85f);
                soil.Silt += 0.8f * alluvial;
                soil.Clay += 0.3f * alluvial;
                soil.Depth += 1.2f * alluvial;
                soil.MineralRichness = FMath.Clamp(soil.MineralRichness + 0.25f * alluvial, 0f, 1f);
                soil.NormalizeTexture();
            }
            soil.Organics = FMath.Clamp(0.15f + 0.80f * totalPlants * FMath.Clamp(1.15f - Math.Max(0f, warmestMonthTempC - 22f) * 0.04f, 0.35f, 1f), 0f, 1f);
            return soil;
        }
    }
}
