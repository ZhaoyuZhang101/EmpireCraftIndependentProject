using UnityEditor;
using UnityEngine;

namespace EmpireCraft.EditorTools
{
    // Pixel-art UI textures (icons, nine-slice frames) under Assets/EmpireCraft/UI:
    // point filtering, no compression, no mipmaps, original size.
    public sealed class EmpireUiTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith("Assets/EmpireCraft/UI/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = true;
        }
    }
}
