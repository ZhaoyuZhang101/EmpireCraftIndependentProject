using System;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace EmpireCraft.UI
{
    // The house pixel font (Fusion Pixel 12px, OFL; docs/design/ui-style.md §3), built once as a
    // dynamic raster font asset so the 12 px glyphs stay hard-edged instead of SDF-softened.
    public static class PixelFont
    {
        const string Path = "EmpireCraft/Fonts/FusionPixel12";
        static bool tried;
        static FontDefinition? definition;

        public static FontDefinition? Definition
        {
            get
            {
                if (tried) return definition;
                tried = true;
                try
                {
                    var font = Resources.Load<Font>(Path);
                    if (!font) { Debug.LogWarning("EmpireCraft pixel font missing: " + Path); return null; }
                    var asset = FontAsset.CreateFontAsset(font, 12, 1, GlyphRenderMode.RASTER_HINTED, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                    if (!asset) return null;
                    asset.name = "Fusion Pixel 12 (raster)";
                    foreach (var atlas in asset.atlasTextures) if (atlas) atlas.filterMode = FilterMode.Point;
                    definition = FontDefinition.FromSDFFont(asset);
                }
                catch (Exception e) { Debug.LogException(e); }
                return definition;
            }
        }
    }
}
