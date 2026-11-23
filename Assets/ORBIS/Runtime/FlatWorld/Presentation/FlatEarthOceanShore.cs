using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Orbis.Flat
{
    // Four pages per RGBA tile keep every coast at the same resolution on 4K
    // devices. Inflate directly into Unity's upload buffers.
    internal sealed class FlatEarthOceanShore : IDisposable
    {
        Texture2D directory, pages;

        internal static FlatEarthOceanShore Load(int textureLimit)
        {
            if (textureLimit < 4096) return null;
            var asset = Resources.Load<TextAsset>("ORBIS/FlatWorld/EarthOceanShore");
            if (!asset) return null;
            var result = new FlatEarthOceanShore();
            try
            {
                byte[] bytes = asset.bytes;
                using var reader = new BinaryReader(new MemoryStream(bytes, false));
                if (reader.ReadUInt32() != 0x3153434f) throw new InvalidDataException("Invalid ocean survey header.");
                int count = reader.ReadInt32(), index = reader.ReadInt32(), banks = reader.ReadInt32();
                if (count < 1 || count > 65535 || Math.Min(index, banks) <= 0 ||
                    16L + index + banks != bytes.Length) throw new InvalidDataException("Invalid ocean survey directory.");
                var buffer = new byte[64 * 1024];
                Texture2D Read(string name, int width, int height, TextureFormat format, int start, int length)
                {
                    using var source = new MemoryStream(bytes, start, length, false);
                    using var inflater = new DeflateStream(source, CompressionMode.Decompress);
                    var tex = FlatEarthLandRelief.ReadTexture(name, width, height, format, false, inflater, buffer);
                    if (inflater.ReadByte() != -1)
                    {
                        UnityEngine.Object.Destroy(tex);
                        throw new InvalidDataException("Excess ocean survey pixels.");
                    }
                    return tex;
                }
                result.directory = Read("Natural Earth ocean directory", 1440, 720, TextureFormat.RGBA32, 16, index);
                result.directory.wrapModeU = TextureWrapMode.Repeat;
                result.pages = Read("Natural Earth ocean coast", 4096, ((count + 511) / 512) * 32,
                    TextureFormat.RGBA32, 16 + index, banks);
                return result;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException)
            {
                result.Dispose();
                Debug.LogWarning("EarthOceanShore: " + e.Message);
                return null;
            }
            finally { Resources.UnloadAsset(asset); }
        }

        internal static void Bind(Material material, FlatEarthOceanShore survey)
        {
            if (!material.HasProperty("_EarthOceanDirectory")) return;
            material.SetTexture("_EarthOceanDirectory", survey == null ? Texture2D.blackTexture : survey.directory);
            material.SetTexture("_EarthOceanPages", survey == null ? Texture2D.blackTexture : survey.pages);
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(directory);
            UnityEngine.Object.Destroy(pages);
        }
    }
}
