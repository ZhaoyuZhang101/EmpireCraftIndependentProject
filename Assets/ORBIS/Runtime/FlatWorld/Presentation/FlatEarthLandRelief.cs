using System;
using System.IO;
using System.IO.Compression;
using Unity.Collections;
using UnityEngine;

namespace Orbis.Flat
{
    // The detailed atlas is GPU-only. Keep the shared overview readable for
    // picked elevations without synchronizing with the renderer.
    internal sealed class FlatEarthLandRelief : IDisposable
    {
        Texture2D overview, directory, pages;

        internal FlatEarthLandRelief()
        {
            if (SystemInfo.maxTextureSize < 3600) return;
            Load(false);
            if (overview && SystemInfo.maxTextureSize >= 8192) Load(true);
        }

        void Load(bool detailed)
        {
            string name = detailed ? "EarthLandDetail" : "EarthLandOverview";
            var asset = Resources.Load<TextAsset>("ORBIS/FlatWorld/" + name);
            if (!asset) return;
            try
            {
                using var stream = new MemoryStream(asset.bytes, false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadUInt32() != (detailed ? 0x31444c4fu : 0x314f4c4fu)) // OLD1 / OLO1
                    throw new InvalidDataException("Invalid land survey header.");
                int width = reader.ReadInt32(), height = reader.ReadInt32();
                if (detailed ? width != 8192 || height < 62 || height > 8192 || height % 62 != 0
                             : width != 3600 || height != 1800)
                    throw new InvalidDataException("Invalid land survey dimensions.");
                using var inflater = new DeflateStream(stream, CompressionMode.Decompress);
                var buffer = new byte[64 * 1024];
                if (detailed)
                {
                    directory = ReadTexture("ETOPO native land directory", 360, 120, TextureFormat.RGBA32, false, inflater, buffer);
                    pages = ReadTexture("ETOPO native land elevations", width, height, TextureFormat.R8, false, inflater, buffer);
                }
                else overview = ReadTexture("ETOPO filtered land elevations", width, height, TextureFormat.R16, true, inflater, buffer, keepReadable: true);
                if (inflater.ReadByte() != -1) throw new InvalidDataException("Excess land survey pixels.");
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException)
            {
                if (detailed)
                {
                    UnityEngine.Object.Destroy(directory);
                    UnityEngine.Object.Destroy(pages);
                    directory = pages = null;
                }
                else
                {
                    UnityEngine.Object.Destroy(overview);
                    overview = null;
                }
                Debug.LogWarning(name + ": " + e.Message);
            }
            finally { Resources.UnloadAsset(asset); }
        }

        internal static Texture2D ReadTexture(string name, int width, int height, TextureFormat format, bool mips, Stream source, byte[] buffer, bool keepReadable = false)
        {
            var texture = new Texture2D(width, height, format, mips, true) {
                name = name, filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear,
                wrapModeU = mips ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Clamp, anisoLevel = 0
            };
            try
            {
                // Inflate straight into Unity's upload buffer in small blocks.
                var pixels = texture.GetPixelData<byte>(0);
                for (int offset = 0; offset < pixels.Length;)
                {
                    int count = source.Read(buffer, 0, Math.Min(buffer.Length, pixels.Length - offset));
                    if (count == 0) throw new InvalidDataException("Incomplete land survey texture.");
                    NativeArray<byte>.Copy(buffer, 0, pixels, offset, count);
                    offset += count;
                }
                texture.Apply(mips, !keepReadable);
                return texture;
            }
            catch
            {
                UnityEngine.Object.Destroy(texture);
                throw;
            }
        }

        internal float? HeightAt(float u, float v)
        {
            if (!overview) return null;
            int width = overview.width, height = overview.height;
            float x = Mathf.Repeat(u, 1) * width - .5f;
            float y = Mathf.Clamp(v * height - .5f, 0, height - 1);
            int column = Mathf.FloorToInt(x), row = (int)y;
            int left = FMath.Wrap(column, width), right = FMath.Wrap(column + 1, width);
            int south = row * width, north = Math.Min(row + 1, height - 1) * width;
            var metres = overview.GetPixelData<ushort>(0);
            float bottom = Mathf.Lerp(metres[south + left], metres[south + right], x - column);
            float top = Mathf.Lerp(metres[north + left], metres[north + right], x - column);
            float result = Mathf.Lerp(bottom, top, y - row) - 32768;
            // The overview can still contain ocean averaged into a small island.
            return result >= 0 ? result : (float?)null;
        }

        internal void Bind(Material material)
        {
            if (!material.HasProperty("_EarthLandOverview")) return;
            material.SetTexture("_EarthLandOverview", overview ? overview : Texture2D.blackTexture);
            material.SetTexture("_EarthLandDirectory", directory ? directory : Texture2D.blackTexture);
            material.SetTexture("_EarthLandPages", pages ? pages : Texture2D.blackTexture);
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(overview);
            UnityEngine.Object.Destroy(directory);
            UnityEngine.Object.Destroy(pages);
        }
    }
}
