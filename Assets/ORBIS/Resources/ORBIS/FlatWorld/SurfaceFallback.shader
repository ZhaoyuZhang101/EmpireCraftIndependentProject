Shader "ORBIS/FlatWorld/SurfaceFallback"
{
 Properties {_FlatTerrain("Source map",2D)="white"{} _FlatWorldReady("Has source",Float)=0}
 SubShader
 {
  Tags {"RenderType"="Opaque"}
  Pass
  {
   Name "SAFE_TEXTURE_ONLY"
   Cull Off
   CGPROGRAM
   #pragma target 2.0
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _FlatTerrain;float _FlatWorldReady;float4x4 _WorldToPlanet;
   struct appdata {float4 vertex:POSITION;float4 color:COLOR;};
   struct v2f {float4 pos:SV_POSITION;float3 local:TEXCOORD0;float4 color:COLOR;};
   v2f vert(appdata v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.local=mul(_WorldToPlanet,mul(unity_ObjectToWorld,v.vertex)).xyz;o.color=v.color;return o;}
   float4 frag(v2f i):SV_Target
   {
    float3 n=normalize(i.local);float2 uv=float2(atan2(n.x,n.z)/6.2831853+.5,asin(clamp(n.y,-1,1))/3.14159265+.5);
    return float4(lerp(i.color.rgb,tex2D(_FlatTerrain,uv).rgb,_FlatWorldReady),1);
   }
   ENDCG
  }
 }
 Fallback Off
}
