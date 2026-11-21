using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Orbis.Flat
{
    // Read-only surveyed detail for the Earth preset. The simulation grid and
    // its edited elevations remain authoritative for picking, paths and water.
    internal sealed class FlatEarthPolarRelief : IDisposable
    {
        readonly Texture2D bed, coast, coverage, lakes, lakeDetail, straits, straitDetail;
        static Texture2D sharedLakes, sharedLakeDetail, sharedStraits, sharedStraitDetail;
        static int lakeUsers;
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
            if (lakeUsers == 0)
            {
                sharedLakes = LoadLakeShore(false);
                if (sharedLakes) sharedLakeDetail = LoadLakeShore(true);
            }
            lakes = sharedLakes;
            lakeDetail = sharedLakeDetail;
            if (world.UsesSurveyedStraits)
            {
                if (!sharedStraits) LoadStraitShore(out sharedStraits, out sharedStraitDetail);
                straits = sharedStraits;
                straitDetail = sharedStraitDetail;
            }
            lakeUsers++;
            coverage = new Texture2D(world.Width, world.Height, TextureFormat.RG16, false, true) {
                name = "Earth survey coverage and marine support", filterMode = FilterMode.Bilinear,
                wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp
            };
        }

        static Texture2D LoadLakeShore(bool detail)
        {
            string name = detail ? "EarthLakeDetail" : "EarthMajorLakes";
            var asset = Resources.Load<TextAsset>("ORBIS/FlatWorld/" + name);
            if (!asset) return null;
            byte[] data = asset.bytes;
            try
            {
                using var stream = new MemoryStream(data, false);
                using var reader = new BinaryReader(stream);
                if (data.Length < 15 || reader.ReadUInt32() != (detail ? 0x31444c4fu : 0x314b4c4fu))
                    throw new InvalidDataException("Invalid shoreline header.");
                int width = reader.ReadInt32(), height = reader.ReadInt32();
                if (detail ? width != 4096 || height <= 256 || height > 8192 || (height-256)%66 != 0
                           : width != 8192 || height != 4096 || (data.Length-12)%3 != 0)
                    throw new InvalidDataException("Invalid shoreline dimensions.");
                if (Math.Max(width, height) > SystemInfo.maxTextureSize) return null;
                var pixels = new byte[width * height];
                int offset = 0;
                if (detail)
                {
                    using var inflater = new DeflateStream(stream, CompressionMode.Decompress, true);
                    while (offset < pixels.Length)
                    {
                        int count = inflater.Read(pixels, offset, pixels.Length - offset);
                        if (count == 0) break;
                        offset += count;
                    }
                    if (inflater.ReadByte() != -1) throw new InvalidDataException("Excess shoreline detail.");
                }
                else
                {
                    for (int p = 12; p < data.Length; p += 3)
                    {
                        byte value = data[p];
                        int run = data[p+1] | data[p+2] << 8;
                        if (value == 0 || run == 0 || run > pixels.Length - offset)
                            throw new InvalidDataException("Invalid shoreline run.");
                        Array.Fill(pixels, value, offset, run);
                        offset += run;
                    }
                }
                if (offset != pixels.Length) throw new InvalidDataException("Incomplete shoreline texture.");
                var map = new Texture2D(width, height, TextureFormat.R8, !detail, true) {
                    name = name, filterMode = detail ? FilterMode.Bilinear : FilterMode.Trilinear,
                    wrapModeU = detail ? TextureWrapMode.Clamp : TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp
                };
                map.SetPixelData(pixels, 0);
                map.Apply(!detail, true);
                return map;
            }
            catch (InvalidDataException e)
            {
                Debug.LogWarning(name + ": " + e.Message);
                return null;
            }
        }

        static void LoadStraitShore(out Texture2D regional, out Texture2D detail)
        {
            regional = detail = null;
            var asset = Resources.Load<TextAsset>("ORBIS/FlatWorld/EarthStraitShore");
            if (!asset) return;
            try
            {
                using var stream = new MemoryStream(asset.bytes, false);
                using var reader = new BinaryReader(stream);
                if (stream.Length <= 20 || reader.ReadUInt32() != 0x3148534fu) // OSH1
                    throw new InvalidDataException("Invalid regional shoreline header.");
                int width = reader.ReadInt32(), height = reader.ReadInt32();
                int fineWidth = reader.ReadInt32(), fineHeight = reader.ReadInt32();
                if (width != 2048 || height <= 507 || height > 8192 || (height-507)%19 != 0 ||
                    fineWidth != 4096 || fineHeight <= 0 || fineHeight > 8192 || fineHeight%130 != 0)
                    throw new InvalidDataException("Invalid regional shoreline dimensions.");
                int limit = SystemInfo.maxTextureSize;
                if (width > limit || height > limit) return;
                using var inflater = new DeflateStream(stream, CompressionMode.Decompress);
                byte[] shore = ReadPixels(inflater, width*height*2);
                byte[] fine = fineWidth <= limit && fineHeight <= limit ? ReadPixels(inflater, fineWidth*fineHeight) : null;
                if (fine != null && inflater.ReadByte() != -1) throw new InvalidDataException("Excess regional shoreline data.");
                regional = StraitTexture("Natural Earth strait regions", TextureFormat.RG16, width, height, shore);
                if (fine != null) detail = StraitTexture("Natural Earth strait banks", TextureFormat.R8, fineWidth, fineHeight, fine);
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException)
            {
                Debug.LogWarning("EarthStraitShore: " + e.Message);
            }
        }

        static byte[] ReadPixels(Stream stream, int length)
        {
            var pixels = new byte[length];
            for (int offset = 0; offset < length;)
            {
                int count = stream.Read(pixels, offset, length-offset);
                if (count == 0) throw new InvalidDataException("Incomplete regional shoreline texture.");
                offset += count;
            }
            return pixels;
        }

        static Texture2D StraitTexture(string name, TextureFormat format, int width, int height, byte[] pixels)
        {
            var texture = new Texture2D(width, height, format, false, true) {
                name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0
            };
            texture.SetPixelData(pixels, 0);
            texture.Apply(false, true);
            return texture;
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
            if (lakes) material.SetTexture("_EarthLakeShore", lakes);
            if (lakeDetail) material.SetTexture("_EarthLakeDetail", lakeDetail);
            material.SetFloat("_EarthLakeReady", lakes ? 1 : 0);
            if (straits) material.SetTexture("_EarthStraitShore", straits);
            material.SetTexture("_EarthStraitDetail", straitDetail ? straitDetail : Texture2D.blackTexture);
            material.SetFloat("_EarthStraitReady", straits ? 1 : 0);
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(bed);
            UnityEngine.Object.Destroy(coast);
            UnityEngine.Object.Destroy(coverage);
            if (--lakeUsers == 0)
            {
                UnityEngine.Object.Destroy(sharedLakes);
                UnityEngine.Object.Destroy(sharedLakeDetail);
                UnityEngine.Object.Destroy(sharedStraits);
                UnityEngine.Object.Destroy(sharedStraitDetail);
                sharedLakes = sharedLakeDetail = null;
                sharedStraits = sharedStraitDetail = null;
            }
        }
    }
}
