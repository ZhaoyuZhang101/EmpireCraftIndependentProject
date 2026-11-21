using System;
using UnityEngine;

namespace Orbis.Flat
{
    // Read-only surveyed detail for the Earth preset. The simulation grid and
    // its edited elevations remain authoritative for picking, paths and water.
    internal sealed class FlatEarthPolarRelief : IDisposable
    {
        readonly Texture2D bed, coast, coverage;
        Vector4 poleCoverage;

        internal static FlatEarthPolarRelief Create(WorldData world)
        {
            if (world.Preset != TerrainPreset.Earth || world.WaterReference == null ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.R16) || !SystemInfo.SupportsTextureFormat(TextureFormat.R8) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RG16)) return null;
            var asset = Resources.Load<TextAsset>("ORBIS/FlatWorld/EarthPolarBed");
            var marine = Resources.Load<TextAsset>("ORBIS/FlatWorld/EarthPolarCoast");
            if (!asset || !marine) return null;
            byte[] data = asset.bytes;
            byte[] shore = marine.bytes;
            int size = (int)Math.Sqrt(data.Length / 4);
            if (size < 3 || size % 2 == 0 || (long)size * size * 4 != data.Length || shore.Length != data.Length / 2)
            {
                Debug.LogWarning("地球极区高程图的尺寸无效，已使用游戏地形。");
                return null;
            }
            return 2 * size <= SystemInfo.maxTextureSize ? new FlatEarthPolarRelief(world, data, shore, size) : null;
        }

        FlatEarthPolarRelief(WorldData world, byte[] data, byte[] shore, int size)
        {
            bed = Atlas("ETOPO polar stereographic relief", TextureFormat.R16, data, size);
            coast = Atlas("Natural Earth polar shoreline", TextureFormat.R8, shore, size);
            coverage = new Texture2D(world.Width, world.Height, TextureFormat.RG16, false, true) {
                name = "Earth survey coverage and marine support", filterMode = FilterMode.Bilinear,
                wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp
            };
        }

        static Texture2D Atlas(string name, TextureFormat format, byte[] data, int size)
        {
            var map = new Texture2D(2 * size, size, format, true, true) {
                name = name, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
            };
            map.SetPixelData(data, 0);
            map.Apply(true, true);
            return map;
        }

        internal void UpdateCoverage(WorldData world)
        {
            var reference = world.WaterReference;
            poleCoverage = new Vector4(1, 1, 0, 0);
            var pixels = coverage.GetPixelData<byte>(0);
            for (int i = 0; i < world.Count; i++)
            {
                pixels[2*i] = 255;
                pixels[2*i+1] = (byte)(world.Water[i] != WaterKind.Lake && world.CoastDistance[i] <= 2 &&
                    reference.ContainsMarineShoreOrSea(i) ? 255 : 0);
            }
            for (int i = 0; i < world.Count; i++)
            {
                if (reference.MatchesOriginalBed(i, world.Elevation[i])) continue;
                // Keep the whole edited cell and its immediate neighbours on the player surface.
                int x = i % world.Width, y = i / world.Width;
                if (y <= 1) poleCoverage.x = 0;
                if (y >= world.Height - 2) poleCoverage.y = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int row = y + dy, column = x;
                    if (row < 0 || row >= world.Height) { row = y; column += world.Width / 2; }
                    for (int dx = -1; dx <= 1; dx++)
                        pixels[2 * (row * world.Width + FMath.Wrap(column + dx, world.Width))] = 0;
                }
            }
            coverage.Apply(false, false);
        }

        internal void Bind(Material material)
        {
            material.SetTexture("_EarthPolarBed", bed);
            material.SetTexture("_EarthPolarCoast", coast);
            material.SetTexture("_EarthSurveyMask", coverage);
            material.SetVector("_EarthSurveyAtPoles", poleCoverage);
            material.SetFloat("_EarthPolarReady", 1);
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(bed);
            UnityEngine.Object.Destroy(coast);
            UnityEngine.Object.Destroy(coverage);
        }
    }
}
