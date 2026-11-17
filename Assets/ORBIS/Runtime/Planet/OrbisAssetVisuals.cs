using UnityEngine;
namespace Orbis.Planet
{
    // Reuses the owner's supplied artwork/noise, without importing incompatible render pipelines.
    public static class OrbisAssetVisuals
    {
        static bool loaded;static Texture2D stream,ocean,lake,oceanFoam,riverFoam;
        static bool celestialLoaded;static Texture2D sunArt,moonArt;
        static void Load(){if(loaded&&stream&&ocean&&lake&&oceanFoam&&riverFoam)return;loaded=true;
            stream=Resources.Load<Texture2D>("ORBISVendor/SW3/StreamWaves");ocean=Resources.Load<Texture2D>("ORBISVendor/SW3/OceanWaves");
            lake=Resources.Load<Texture2D>("ORBISVendor/SW3/SharpWaves");
            if(!lake)lake=Resources.Load<Texture2D>("ORBISVendor/SW3/SmoothWaves");
            oceanFoam=Resources.Load<Texture2D>("ORBISVendor/SW3/OceanFoam");riverFoam=Resources.Load<Texture2D>("ORBISVendor/SW3/DirectionalFoam");
        }
        public static void BindWater(Material m)
        {
            Load();m.SetFloat("_VendorWaterReady",stream&&ocean&&lake&&oceanFoam&&riverFoam?1:0);
            if(stream)m.SetTexture("_VendorStreamNormal",stream);if(ocean)m.SetTexture("_VendorOceanNormal",ocean);
            if(lake)m.SetTexture("_VendorLakeNormal",lake);if(oceanFoam)m.SetTexture("_VendorOceanFoam",oceanFoam);if(riverFoam)m.SetTexture("_VendorRiverFoam",riverFoam);
        }
        public static void BindCelestials(Material m)
        {
            if (!m) return;
            if (!celestialLoaded)
            {
                celestialLoaded=true;
                sunArt=Resources.Load<Texture2D>("ORBIS/Celestial/Sun");
                moonArt=Resources.Load<Texture2D>("ORBIS/Celestial/Moon8K");
            }
            m.SetFloat("_OrbisCelestialReady",sunArt&&moonArt?1:0);
            if(sunArt)m.SetTexture("_OrbisSunTex",sunArt);
            if(moonArt)m.SetTexture("_OrbisMoonTex",moonArt);
        }
    }
}
