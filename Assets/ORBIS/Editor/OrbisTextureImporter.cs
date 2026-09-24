using UnityEditor;
using UnityEngine;
namespace Orbis.Editor
{
    public sealed class OrbisTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(assetPath!="Assets/ORBIS/Resources/ORBIS/TerrainAtlas.png")return;
            var importer=(TextureImporter)assetImporter;importer.textureType=TextureImporterType.Default;
            importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.None;
            // Keep the exact image-generation output; do not resize its quadrant boundaries.
            importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=2048;
            importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=8;
            importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Uncompressed;
        }
    }
}
