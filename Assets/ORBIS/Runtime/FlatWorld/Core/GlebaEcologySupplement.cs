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
        Taiga = 17
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
        /// mean of monthly temperatures strictly inside [0, 30] C (months outside [0, 30] contribute 0).
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
            Koppen koppen, in GlebaPlantCover flora)
        {
            bool icy = iceThickness > 0.001f;
            if (aquatic)
            {
                if (!lake) return icy ? GlebaBiomeKind.GlaciatedSea : GlebaBiomeKind.Sea;
                if (freshwater) return icy ? GlebaBiomeKind.RockyWasteland : GlebaBiomeKind.SweetwaterLake;
                return icy ? GlebaBiomeKind.GlaciatedSaltwaterLake : GlebaBiomeKind.SaltwaterLake;
            }
            if (icy) return GlebaBiomeKind.Glacier;

            float tWin = Math.Min(janTempC, julTempC);
            float tSum = Math.Max(janTempC, julTempC);
            float tAvg = 0.5f * (janTempC + julTempC);
            float dead = flora.DeadLand;
            float trees = flora.Trees;
            float conifer = flora.ConiferFraction;

            float winToSum = 0.5f, sumToWin = 0.5f;
            if (janRainMm > 0f || julRainMm > 0f)
            {
                if (julTempC > janTempC)
                {
                    winToSum = janRainMm / Math.Max(julRainMm, 1f);
                    sumToWin = julRainMm / Math.Max(janRainMm, 1f);
                }
                else
                {
                    winToSum = julRainMm / Math.Max(janRainMm, 1f);
                    sumToWin = janRainMm / Math.Max(julRainMm, 1f);
                }
            }

            // Priority 0..301 ascending (last match wins)
            GlebaBiomeKind chosen = GlebaBiomeKind.RockyWasteland;
            if (dead >= 0.50f && tWin >= 0f) chosen = GlebaBiomeKind.DesertsAndXericShrublands; // p=20

            if (dead <= 0.50f)
            {
                if (trees >= 0.55f && conifer >= 0.65f) chosen = GlebaBiomeKind.TemperateConiferousForest; // p=50
                if (trees >= 0.55f && conifer <= 0.65f) chosen = GlebaBiomeKind.TemperateBroadleafAndMixedForest; // p=50
                if (trees >= 0.55f && tWin >= 18f) chosen = GlebaBiomeKind.DryJungle; // p=50
                if (trees <= 0.55f) chosen = GlebaBiomeKind.TemperateGrasslandsSavannasAndShrublands; // p=50
                if (trees <= 0.55f && tWin >= 18f) chosen = GlebaBiomeKind.TropicalGrasslandsSavannasAndShrublands; // p=50
                if (tWin >= 0f && tWin <= 18f && (koppen == Koppen.Csa || koppen == Koppen.Csb || koppen == Koppen.Csc))
                    chosen = GlebaBiomeKind.MediterraneanForestWoodlandAndScrub; // p=50
            }

            if (elevationMeters >= 2500f && trees <= 0.55f && dead <= 0.90f)
                chosen = GlebaBiomeKind.MontaneGrasslandsAndShrublands; // p=50

            if (trees >= 0.55f && tWin >= 18f && dead <= 0.50f && winToSum <= 8.25f && sumToWin <= 8.25f)
                chosen = GlebaBiomeKind.WetJungle; // p=51

            if (tWin <= 0f && tSum <= 15f && tAvg <= 0f)
                chosen = GlebaBiomeKind.Tundra; // p=300

            if (trees >= 0.55f && dead <= 0.50f &&
                (koppen == Koppen.ET || koppen == Koppen.Dfc || koppen == Koppen.Dwc || koppen == Koppen.Dsc))
                chosen = GlebaBiomeKind.Taiga; // p=301

            return chosen;
        }

        /// <summary>
        /// Sea-ice thickness in meters (0..30 m) from annual mean water temperature (glacier::assign_sea_ice).
        /// </summary>
        public static float ComputeSeaIceMeters(bool isWater, float meanTemperatureC)
            => isWater ? GlebaClimateConfig.EvaluateCurve(SeaIceCurve, meanTemperatureC) : 0f;

        /// <summary>
        /// Present-day or LGM grounded glacier thickness in meters (glacier::assign_grounded_ice).
        /// </summary>
        public static float ComputeGroundedIceMeters(
            bool isLand, float elevationMeters, float warmestMonthTempC, float meanMonthlyRainMm,
            float tempOffsetC = 0f, float noiseCoolingC = 0f)
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
            float low = ice - 0.75f * Math.Max(elevationMeters, 0f);
            return Math.Max(low + 0.05f * (ice - low), 0f);
        }
    }
}
