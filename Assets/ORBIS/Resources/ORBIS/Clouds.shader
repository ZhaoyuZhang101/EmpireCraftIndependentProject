// V4.2 Repair IPC fix: flat view traversal + shared direct-shadow fallback.
// Same target, pass indices, MRT layouts and active GLtracy atmosphere as V4.2 Repair.
// UnityShaderCompiler/GPU acceptance must be run in the target Unity Editor.
Shader "ORBIS/Clouds"
{
 Properties
 {
  [HideInInspector] _Sun("Sun direction",Vector)=(0,0,1,0)
  [HideInInspector] _MarkleyDirections("Markley directions",2D)="white"{}
  _MainTex("Source",2D)="black"{}
  [HideInInspector] _CloudNoise("Cloud shape",3D)="white"{}
  [HideInInspector] _CloudDetailNoise("Cloud erosion",3D)="white"{}
  [HideInInspector] _HPHighDetail("High cloud detail",2D)="black"{}
  [HideInInspector] _ScrollWeatherTex("Published regional weather",2D)="black"{}
  [HideInInspector] _ScrollCloudWindTex("Regional wind",2D)="black"{}
  [HideInInspector] _ScrollHeightTex("Regional ground height",2D)="black"{}
  [HideInInspector] _ScrollCloudSource("Use regional weather",Float)=0
 }
 SubShader
 {
  Cull Off ZWrite Off ZTest Always
  Pass
  {
   Name "MRT_VOLUME"
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert_img
   #pragma fragment frag
   #define ORBIS_SHARED_SCROLL_WEATHER 1
   #include "OrbisAtmosphereCommon.cginc"
   struct MRT {float4 light:SV_Target0;float4 transmission:SV_Target1;};
   MRT frag(v2f_img i)
   {OrbisVolumeResult r=OrbisMarch(i.uv);MRT o;o.light=float4(r.light,r.sceneDepth);o.transmission=float4(r.transmission,r.sceneDepth);return o;}
   ENDCG
  }
  Pass
  {
   Name "FULL_RES_VOLUME"
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex OrbisLensVertex
   #pragma fragment frag
   #define ORBIS_SHARED_SCROLL_WEATHER 1
   #include "OrbisAtmosphereCommon.cginc"
   #include "OrbisCelestialLens.cginc"
   float4 frag(OrbisLensVaryings i):SV_Target
   {
    OrbisVolumeResult r=OrbisMarch(i.uv);
    float3 background=OrbisBackgroundWithSun(tex2D(_MainTex,i.uv).rgb,i.uv,r.sceneDepth);
    return float4(OrbisTone(background*r.transmission+r.light+OrbisLensRadiance(i)),1);
   }
   ENDCG
  }
  Pass
  {
   Name "MAP_VOLUME"
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert_img
   #pragma fragment frag
   #define ORBIS_SHARED_SCROLL_WEATHER 1
   #include "OrbisAtmosphereCommon.cginc"
   float4 frag(v2f_img i):SV_Target
   {OrbisVolumeResult r=OrbisMarchMap(i.uv);return float4(OrbisTone(tex2D(_MainTex,i.uv).rgb*r.transmission+r.light),1);}
   ENDCG
  }
  Pass
  {
   Name "MRT_MAP_VOLUME"
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert_img
   #pragma fragment frag
   #define ORBIS_SHARED_SCROLL_WEATHER 1
   #include "OrbisAtmosphereCommon.cginc"
   struct MRT {float4 light:SV_Target0;float4 transmission:SV_Target1;};
   MRT frag(v2f_img i)
   {OrbisVolumeResult r=OrbisMarchMap(i.uv);MRT o;o.light=float4(r.light,r.sceneDepth);o.transmission=float4(r.transmission,r.sceneDepth);return o;}
   ENDCG
  }
  Pass
  {
   Name "SOLAR_APERTURE"
   CGPROGRAM
   #pragma target 3.5
   #pragma vertex vert_img
   #pragma fragment frag
   #define ORBIS_SHARED_SCROLL_WEATHER 1
   #include "OrbisAtmosphereCommon.cginc"
   float4 _LensSun,_LensDiskAxes;
   float4 frag(v2f_img i):SV_Target
   {
    // Equal-area rings, staggered in azimuth so a grazing horizon cuts the disc progressively.
    float angle=2*ORBIS_PI*frac(i.uv.x+floor(i.uv.y*8)*.381966);
    float s,c;sincos(angle,s,c);
    float2 uv=_LensSun.xy+sqrt(i.uv.y)*(_LensDiskAxes.xy*c+_LensDiskAxes.zw*s);
    if(any(uv<0)||any(uv>1))return 0;
    float eye=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,OrbisDepthUV(uv)));
    float3 rd=normalize(OrbisViewRay(uv));
    if(!OrbisSkyVisible(rd,eye))return 0;
    if(_OrbisCelestialReady>.5 && dot(rd,_OrbisMoon.xyz)>0 && length(cross(rd,_OrbisMoon.xyz))<ORBIS_MOON_SIN_RADIUS)return 0;
    if(_Clouds<.5 && _Atmosphere<.5)return 1;
    return float4(OrbisMarch(uv).transmission,1);
   }
   ENDCG
  }
 }
 Fallback Off
}
