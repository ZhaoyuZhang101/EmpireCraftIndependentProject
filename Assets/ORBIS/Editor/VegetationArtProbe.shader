Shader "Hidden/ORBIS/VegetationArtProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_FLAT_NO_SWE
 #include "../Resources/ORBIS/FlatWorld/Water/FlatWater.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSkyComposite.cginc"
 sampler2D _InfoTex;
 #include "../Resources/ORBIS/FlatWorld/FlatPixelDetail.cginc"
 float2 _ProbeRoot;
 float2 _ProbeGrid;
 float _ProbeMode;
 float _ProbeSpecies,_ProbeSeed,_ProbeFoliage;
 float3 _ProbeLight;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeMode>11.5)
  {
   float2 cell=_ProbeRoot*_MapSize.xy;
   float4 placement=float4(frac(cell)*32,.65,0);
   float shore=-3;
   float lake=FlatEarthLake(_ProbeRoot,0,0,shore);
   return float4(shore,lake,PDTreeSize(floor(cell),placement,0,float4(-1,0,0,0)),1);
  }
  if(_ProbeMode>9.5)
  {
   float2 id=_ProbeRoot+floor(i.uv*_ProbeGrid);
   float4 placement=PDTreePlacement(id);
   float2 uv=(id+placement.xy/32)*_MapSize.zw;
   float2 root=FlatEarthStrait(uv,0);
   if(_ProbeMode>10.5)
   {
    float2 centreUV=(id+.5)*_MapSize.zw;
    float4 info=tex2Dlod(_InfoTex,float4(centreUV,0,0))*255;
    float4 ecology=FlatEcologyData(centreUV,info);placement.w=ecology.a;
    return float4((uv-.5)*float2(360,180),root.x*40030.2*_MapSize.z,PDTreeAvailable(id,placement,info,ecology));
   }
   float centre=FlatEarthStrait((id+.5)*_MapSize.zw,0).x;
   return float4(PDTreeSize(id,placement,0,float4(-1,0,0,0)),root,centre);
  }
  if(_ProbeMode>2.5)
  {
   float2 pixel=floor((i.uv-float2(.5,.30))*48);
   float2 wind=PDVegetationWind(_ProbeRoot,_ProbeSeed);
   if(_ProbeMode>8.5)
   {
    // Hold the trunk deflection fixed to isolate the movement of individual branches.
    float2 bend=float2(1.4,.25),edge;
    float4 crown=PDTreeCrown(pixel,_ProbeSpecies,_ProbeSeed,_ProbeFoliage,bend,edge);
    return float4(crown.a,PDTreeCastShadow(pixel,_ProbeLight,_ProbeSpecies,_ProbeSeed,_ProbeFoliage,bend),0,1);
   }
   if(_ProbeMode>7.5)
   {
    float2 edge;
    float4 crown=PDTreeCrown(pixel,_ProbeSpecies,_ProbeSeed,_ProbeFoliage,wind,edge);
    return float4(crown.a,PDTreeCastShadow(pixel,_ProbeLight,_ProbeSpecies,_ProbeSeed,_ProbeFoliage,wind),0,1);
   }
   if(_ProbeMode>6.5)
   {
    float2 boundsPixel=floor((i.uv-float2(.5,.32))*64);
    float4 art=PDTreeArtwork(boundsPixel,_ProbeRoot,_ProbeSpecies,_ProbeSeed,_ProbeFoliage,1,_ProbeLight);
    float outside=!PDTreeInDrawBounds(boundsPixel);
    return (art.a*outside).xxxx;
   }
   if(_ProbeMode>5.5)return float4(PDTreeView,PDTreeUp.y);
   if(_ProbeMode>4.5)return PDTreeTrunk(pixel,_ProbeSpecies,_ProbeSeed,wind).xxxx;
   if(_ProbeMode>3.5)return PDTreeCastShadow(pixel,_ProbeLight,_ProbeSpecies,_ProbeSeed,_ProbeFoliage,wind).xxxx;
   return PDTreeArtwork(pixel,_ProbeRoot,_ProbeSpecies,_ProbeSeed,_ProbeFoliage,1,_ProbeLight);
  }
  if(_ProbeMode>1.5)
  {
   float2 uv=(_ProbeRoot+.5)*_MapSize.zw;
   float4 info=tex2Dlod(_InfoTex,float4(uv,0,0))*255;
   float tree;
   float4 ecology=FlatEcologyData(uv,info,tree);
   return float4(PDTreeSpecies(ecology,info,0,tree),ecology.g,tree,ecology.a);
  }
  float scale=max(.02,cos(((_ProbeRoot.y+.5)*_MapSize.w-.5)*3.141592654));
  float2 cell=_ProbeRoot+PDTreePlacement(_ProbeRoot).xy/32+(i.uv-.5)*float2(2,2*scale);
  if(_ProbeMode>.5)return PixelPropShadow(cell).xxxx;
  float3 col=PixelProps(0,cell,float4(10,-3,0,1),0,0,1);
  return float4(col,step(.001,max(col.r,max(col.g,col.b))));
 }
 ENDCG
 }}
}
