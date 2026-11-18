Shader "Hidden/ORBIS/TerrainQualityProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 float4 _ProbeOrigin,_ProbeSpan,_ProbeGradient;
 float4 _ProbeSun,_ProbeMoon,_ProbeView,_ProbeGround;
 sampler2D _ProbeGeo;
 float _ProbePixels,_ProbeFlip,_ProbeMode,_ProbeCoverage;
 float4 frag(v2f_img i):SV_Target
 {
  float2 sampleUV=i.uv;
  if(_ProbeFlip>.5)sampleUV.x=1-sampleUV.x;
  float2 span=_ProbeSpan.x>0?_ProbeSpan.xy:float2(2,1);
  float2 cell=_ProbeOrigin.xy+sampleUV*span;
  if(_ProbeMode>9.5&&_ProbeMode<10.5)
  {
   float4 centre=tex2Dlod(_ProbeGeo,float4((floor(cell)+.5)*_MapSize.zw,0,0));
   return FlatNaturalGround(_ProbeGeo,cell,centre,_ProbePixels,0);
  }
  if(_ProbeMode>6.5&&_ProbeMode<9.5)
  {
   float4 field=FlatField(cell);
   float2 gradient=FlatFieldGradient(cell,0);
   float4 light=FlatTerrainLight(_ProbeSun.xyz,_ProbeMoon,_ProbeGround.w);
   float4 substrate=FlatGroundProperties(float4(0,_ProbeGround.x,_ProbeGround.y,0));
   if(_ProbeMode>8.5)
    return float4(FlatTerrainFine(FlatColor(float3(.38,.47,.24)),cell,_ProbePixels,gradient,field,substrate.xy,_ProbeGround.y,light),1);
   float wet=FlatWetMineral(substrate.z,_ProbeGround.y,_ProbeGround.z);
   return FlatTerrainRelief(cell,field.r,gradient,light,_ProbeView.xyz,wet);
  }
  if(_ProbeMode>19.5&&_ProbeMode<20.5)return FlatSurfaceSeason(cell,FlatRadial(cell*_MapSize.zw));
  if(_ProbeMode>5.5)return float4(FlatVegetationSeason(cell),0,1);
  if(_ProbeMode>3.5)
  {
   float major;
   float contour=FlatContour(FlatField(cell).r,100,_ProbePixels,major);
   if(_ProbeMode>4.5)
    contour*=FlatGentleContourWeight(FlatFieldGradient(cell,0));
   return float4(contour,major,0,1);
  }
  if(_ProbeMode>2.5)return float4(FlatElevationColor(FlatVisualSample(float4(cell*_MapSize.zw,0,0)).r),1);
  if(_ProbeMode>1.5)return float4(FlatTerrainSnow(_ProbeCoverage,cell,_ProbeGradient.xy,_ProbePixels).xxx,1);
  if(_ProbeMode>.5)return float4(FlatFieldRelief(cell,0,float3(-.45,.6,.75)).xxx,1);
  return float4(FlatTerrainFine(FlatColor(float3(.38,.47,.24)),cell,_ProbePixels,FlatFieldGradient(cell,0),
                FlatField(cell),0,.3,float4(normalize(float3(-.45,.6,.75)),1)),1);
 }
 ENDCG
 }}
}
