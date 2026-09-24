Shader "ORBIS/RiverWater"
{
 Properties{_Weather("Weather",2D)="black"{}}
 SubShader
 {
  Tags{"Queue"="Transparent-30" "RenderType"="Transparent"}
  Pass
  {
   Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha Offset -1,-1
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.5
   #include "UnityCG.cginc"
   #include "OrbisOptics.cginc"
   #include "OrbisWater.cginc"
   #include "OrbisPatch.cginc"
   float3 _Sun;float _CloudShadow,_Natural;
   struct appdata{float4 vertex:POSITION;float3 normal:NORMAL;float3 geo:TEXCOORD0;float4 water:TEXCOORD2;float3 flow:TEXCOORD3;float2 profile:TEXCOORD4;};
   struct v2f{float4 pos:SV_POSITION;float3 p:TEXCOORD0;float3 normal:TEXCOORD1;float3 view:TEXCOORD2;float4 water:TEXCOORD3;float3 flow:TEXCOORD4;float2 profile:TEXCOORD5;float2 mapPosition:TEXCOORD6;};
   v2f vert(appdata v)
   {
    v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.p=_Map>.5?normalize(v.geo):v.vertex.xyz;
    o.normal=normalize(v.normal);o.view=_Map>.5?normalize(v.geo):mul(_WorldToPlanet,float4(_WorldSpaceCameraPos,1)).xyz-o.p;
    o.water=v.water;o.flow=v.flow;o.profile=v.profile;o.mapPosition=v.vertex.xy;return o;
   }
   float4 frag(v2f i):SV_Target
   {
    if(_Map>.5){clip(ORBIS_PI-abs(i.mapPosition.x));clip(ORBIS_PI*.5-abs(i.mapPosition.y));}
    float3 up=normalize(i.p),sun=normalize(_Sun),view=normalize(i.view);
    float shadow=1;if(_CloudShadow>.5)shadow=lerp(1.0,OrbisCloudShadow(i.p,sun),_ShadowStrength);
    float3 color=OrbisShadeWater(i.p,normalize(i.normal),view,sun,i.water,i.flow,shadow,1-_Night);
    // Same optical model as ocean/lake: only depth, roughness and advection differ.
    float edge=1-smoothstep(.82,1,abs(i.profile.x*2-1));
    float mouth=1-smoothstep(.45,1,i.profile.y);
    float alpha=edge*mouth;
    // Diagnostic layers remain visible rather than turning into another opaque river overlay.
    alpha*=lerp(.40,1,step(.5,_Natural));
    if(_SurfaceDebug>0)return float4(0,0,0,0);
    return float4(max(0,color),alpha);
   }
   ENDCG
  }
 }
}
