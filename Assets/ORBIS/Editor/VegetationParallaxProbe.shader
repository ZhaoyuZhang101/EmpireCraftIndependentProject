Shader "Hidden/ORBIS/VegetationParallaxProbe"
{
 SubShader { Pass {
 Cull Off ZWrite Off ZTest Always
 CGPROGRAM
 #pragma target 3.0
 #pragma multi_compile_local __ ORBIS_PROBE_REEDS
 #pragma multi_compile_local __ ORBIS_ATLAS_PLANTS
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_FLAT_NO_SWE
 #include "../Resources/ORBIS/FlatWorld/Water/FlatWater.cginc"
 #ifdef ORBIS_ATLAS_PLANTS
 #include "../Resources/ORBIS/FlatWorld/ScrollProjection.cginc"
 #else
 #include "../Resources/ORBIS/FlatWorld/LocalProjection.cginc"
 #define ORBIS_LOCAL_PLANTS 1
 #endif
 #include "../Resources/ORBIS/FlatWorld/FlatSkyComposite.cginc"
 sampler2D _InfoTex;
 #include "../Resources/ORBIS/FlatWorld/FlatPixelDetail.cginc"
 float2 _ProbeTile;
 float _ProbeKind,_ProbeCover,_ProbeHalfExtent;
 struct outputData {float4 pos:SV_POSITION;float2 chart:TEXCOORD0;float3 projected:TEXCOORD1;};
 outputData vert(appdata_base v)
 {
  outputData o;
  #ifdef ORBIS_ATLAS_PLANTS
  o.chart=v.vertex.xy;
  float2 plane=(o.chart*_ProjectionSize.xy-_ScrollPan.xy)*(6.283185307*_MapSize.z);
  float h=ScrollTerrainRadius(MarkleyGeoUV(o.chart),_MapSize)-1;
  #else
  float2 plane=v.vertex.xy*_LocalRadius;
  o.chart=plane;
  float h=ScrollTerrainRadius(LocalGeoUV(plane),_MapSize)-1;
  #endif
  float2 p=FlatReliefPosition(plane,h).xy;
  float weight=FlatReliefClipWeight(h,1);
  o.projected=float3(p,1)*weight;
  o.pos=float4(p/_ProbeHalfExtent,.5,1)*weight;
  return o;
 }
 float4 frag(outputData i):SV_Target
 {
  float2 cluster=float2(2,1);
  float salt=.42;
  if(_ProbeKind>=3)
  {
   if(_ProbeCover<2)
   {
    float best=1;
    [unroll] for(int c=0;c<16;c++)
    {
     float2 candidate=float2(c%4,c/4);
     float seed=PDHash(_ProbeTile+candidate*.127+salt*23);
     if(seed<best){cluster=candidate;best=seed;}
    }
   }
   float variation=PDHash(_ProbeTile+cluster*.213+salt*41+71);
   float2 anchor=cluster*8+PDGroundPlantLayout(variation,1,_ProbeCover).xy;
   anchor.y=16+(anchor.y-16)*FlatArtworkLatitudeScale((_ProbeTile.y+.5)*_MapSize.w);
   return float4(PDTreePlacement(_ProbeTile).xy/32,anchor/32);
  }
  #ifdef ORBIS_ATLAS_PLANTS
  float2 cell=MarkleyGeoUV(i.chart)*_MapSize.xy;
  PDPlantView view=PDAtlasPlantView(i.projected.xy/i.projected.z,i.chart);
  #else
  float2 cell=LocalGeoUV(i.chart)*_MapSize.xy;
  PDPlantView view=PDLocalPlantView(i.projected.xy/i.projected.z,i.chart);
  #endif
  #ifdef ORBIS_PROBE_REEDS
  float3 art=PixelGround(0,cell,float4(1000,-4,0,1),0,0,float4(_ProbeCover,3,.7,salt),view);
  #else
  float3 art=PixelProps(0,cell,0,0,0,1,view);
  #endif
  return float4(art,any(floor(cell)!=_ProbeTile)?1:0);
 }
 ENDCG
 }}
}
