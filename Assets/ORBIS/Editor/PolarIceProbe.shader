Shader "Hidden/ORBIS/PolarIceProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatPolarIce.cginc"
 float _ProbeWater;
 float4 frag(v2f_img i):SV_Target
 {return float4(FlatPolarIceColor(i.uv,_ProbeWater),FlatPolarIceMask(i.uv,_ProbeWater,0));}
 ENDCG
 }}
}
