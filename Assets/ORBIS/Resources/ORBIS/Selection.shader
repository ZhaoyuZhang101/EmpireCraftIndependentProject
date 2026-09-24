Shader "ORBIS/Selection"
{
 SubShader
 {
  Tags{"Queue"="Transparent+20" "RenderType"="Transparent"}
  Pass
  {
   Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha Offset -2,-2
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   float _Map;
   struct v2f{float4 pos:SV_POSITION;float4 color:COLOR;float3 local:TEXCOORD0;float3 camera:TEXCOORD1;};
   v2f vert(appdata_full v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.color=v.color;o.color.rgb=GammaToLinearSpace(max(0,o.color.rgb));o.local=v.vertex.xyz;o.camera=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz;return o;}
   float4 frag(v2f i):SV_Target
   {
    if(_Map>.5){clip(3.14159265-abs(i.local.x));clip(1.57079633-abs(i.local.y));}
    else clip(dot(normalize(i.local),i.camera-i.local));
    return i.color;
   }
   ENDCG
  }
 }
}
