using System;
using UnityEngine;
namespace Orbis
{
    /// <summary>GLtracy atmosphere and HP cloud shell use distinct altitude bounds.
    /// Radius 1 planet, 1.06 cloud bound, 1.20 atmosphere; an artistic vertical scale.</summary>
    [Serializable]
    public sealed class OrbisAtmosphereProfile
    {
        public const float OuterRadius=1.20f,CloudBase=1.0003f,CloudTop=1.060f;
        public const float CloudMetresToRadius=.0000015f;
        public const string ActiveSource="USER_GLTRACY_V4_2_REPAIR";
        public const string SourceSHA256="baa5c9136a8e37ff69f5a18f29a6254f2a63e81acb6dff577f30bfc0f40ad9ee";
        [Range(1,3)]public float rayleighHeightStretch=2f;
        [Range(1,5)]public float mieHeightStretch=3.3333333f;
        [Range(-.9f,.9f)]public float mieAnisotropy=.78f;
        [Range(1,60)]public float solarRadiance=10f;
        [Tooltip("Legacy serialized field. The supplied GLtracy model has no ozone term.")]
        [Range(0,2)]public float ozoneStrength=0;
        [Range(.1f,1.2f)]public float sunAngularRadiusDegrees=.266f;
        public void Bind(Material material,int quality)
        {
            if(!material)return;
            float r=Mathf.Clamp(rayleighHeightStretch,1,3)/2f;
            float m=Mathf.Clamp(mieHeightStretch,1,5)/3.3333333f;
            // Source spectral ratios 3.8:13.5:33.1 retained. Vertical optical depth is
            // reduced from the thick demo shell; aerosol extinction separately uses 1.1.
            material.SetVector("_AtmoBetaRayleigh",new Vector4(1.9f/r,6.75f/r,16.55f/r,.022f*r));
            material.SetVector("_AtmoBetaMie",new Vector4(2.1f/m,2.1f/m,2.1f/m,.0064f*m));
            material.SetVector("_AtmoBetaAbsorption",Vector4.zero);
            material.SetVector("_AtmoParameters",new Vector4(0,Mathf.Clamp(mieAnisotropy,-.9f,.9f),Mathf.Clamp(solarRadiance,1,60),8));
            material.SetVector("_AtmoSunDisk",new Vector4(Mathf.Cos(Mathf.Clamp(sunAngularRadiusDegrees,.1f,1.2f)*Mathf.Deg2Rad),3,0,0));
            material.SetTexture("_AtmoOpticalDepth",OrbisAtmosphereLut.Get(.022f*r,.0064f*m));
            material.SetVector("_AtmoLutSize",new Vector4(OrbisAtmosphereLut.Width,OrbisAtmosphereLut.Height,1f/OrbisAtmosphereLut.Width,1f/OrbisAtmosphereLut.Height));
            material.SetFloat("_AtmoLutReady",1);
        }
    }
}
