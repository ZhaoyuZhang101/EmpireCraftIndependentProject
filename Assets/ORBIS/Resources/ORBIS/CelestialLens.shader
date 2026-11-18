Shader "ORBIS/CelestialLens"
{
 Properties
 {
  _MainTex("Scene",2D)="black"{}
  [HideInInspector] _LensSun("Sun position, intensity and aspect",Vector)=(.5,.5,0,1)
  [HideInInspector] _LensTransmission("Solar disc transmission",2D)="black"{}
 }
 SubShader
 {
  Cull Off ZWrite Off ZTest Always
  Pass
  {
   Name "CLEAR_SKY"
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex OrbisLensVertex
   #pragma fragment frag
   #include "OrbisOptics.cginc"
   #include "OrbisAtmosphereView.cginc"
   #include "OrbisCelestialLens.cginc"
   sampler2D _MainTex;
   float4 _MainTex_TexelSize;
   UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
   float4 frag(OrbisLensVaryings i):SV_Target
   {
    float2 depthUV=i.uv;
    #if UNITY_UV_STARTS_AT_TOP
    if(_MainTex_TexelSize.y<0)depthUV.y=1-depthUV.y;
    #endif
    float depth=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,depthUV));
    float3 scene=OrbisBackgroundWithSun(tex2D(_MainTex,i.uv).rgb,i.uv,depth);
    return float4(OrbisDisplay(scene+OrbisLensRadiance(i)),1);
   }
   ENDCG
  }
 }
 Fallback Off
}
