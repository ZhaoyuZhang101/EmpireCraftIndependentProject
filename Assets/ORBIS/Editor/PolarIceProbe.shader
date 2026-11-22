Shader "Hidden/ORBIS/PolarIceProbe"
{
 Properties { _LakeIceTex("Monthly temperature history",2D)="white"{} }
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatPolarIce.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatLakeIce.cginc"
 float _ProbeWater;
 float _ProbeLakeMode,_ProbeDepth;
 float4 _ProbeBasin,_ProbeLakeUV;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeLakeMode>.5)
  {
   float2 uv=_ProbeLakeUV.xy;
   float receiver=FlatLakeIceReceiver(_ProbeBasin.x,_ProbeBasin.y,_ProbeBasin.z,_ProbeBasin.w,.0001);
   float cover=FlatLakeIceCover(uv,FlatRadial(uv),_ProbeDepth,_ProbeBasin.w,.5);
   float raw=lerp(FlatLakeIceMonth(uv,_LakeIceSeason.x),FlatLakeIceMonth(uv,_LakeIceSeason.y),_LakeIceSeason.z);
   return float4(receiver,receiver*cover,raw,1);
  }
  return float4(FlatPolarIceColor(i.uv,_ProbeWater),FlatPolarIceMask(i.uv,_ProbeWater,0));
 }
 ENDCG
 }}
}
