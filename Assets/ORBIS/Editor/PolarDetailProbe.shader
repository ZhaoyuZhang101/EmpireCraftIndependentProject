Shader "Hidden/ORBIS/PolarDetailProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 #include "../Resources/ORBIS/FlatWorld/LocalProjection.cginc"
 float _Latitude,_ProjectionProbe;
 float4 _ProbeOrigin,_ProbeSpan;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProjectionProbe>.5)
  {
   if(_ProjectionProbe>2.5)
   {
    float2 chart;
    return float4(ScrollPosition(_ProbeOrigin.xy/_LocalRadius,0,chart),1);
   }
   float2 xy=_ProbeOrigin.xy+(i.uv-.5)*_ProbeSpan.xy;
   float3 direction=LocalDirection(xy);
   float3 recovered=FlatRadial(LocalGeoUV(xy));
   float3 east=LocalDirection(xy+float2(.0005,0))-LocalDirection(xy-float2(.0005,0));
   float3 north=LocalDirection(xy+float2(0,.0005))-LocalDirection(xy-float2(0,.0005));
   float ratio=length(east)/max(1e-12,length(north));
   float shear=abs(dot(east,north))/max(1e-16,length(east)*length(north));
   float pixelError=length(recovered-direction)/max(1e-12,length(ddx(direction)));
   return float4(ratio,shear,pixelError,1);
  }
  float lat=_Latitude*.01745329252;float2 xy=(i.uv-.5)*.001;
  float3 dir=normalize(float3(xy.x,sin(lat)+cos(lat)*xy.y,cos(lat)-sin(lat)*xy.y));
  float2 uv=float2(atan2(dir.x,dir.z)/6.283185307+.5,atan2(dir.y,length(dir.xz))/3.141592654+.5);
  float3 detail=FlatDetailPosition(uv*_MapSize.xy);
  return float4(length(ddx(detail))*128000,length(ddy(detail))*128000,0,1);
 }
 ENDCG
 }
 Pass
 {
 ZWrite On ZTest LEqual
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatVisualTexture.cginc"
 float4 _MapSize;
 #include "../Resources/ORBIS/FlatWorld/LocalProjection.cginc"
 struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
 struct v2f {float4 pos:SV_POSITION;float2 chart:TEXCOORD0;};
 v2f vert(appdata v)
 {
  v2f o;float2 chart;float weight;float3 position=ScrollPosition(v.uv,0,chart,weight);
  o.pos=UnityObjectToClipPos(float4(position,1))*weight;o.chart=chart;return o;
 }
 float4 frag(v2f i):SV_Target {return float4(LocalDirection(i.chart),1);}
 ENDCG
 }
 Pass
 {
 ZWrite On ZTest LEqual
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatVisualTexture.cginc"
 float4 _MapSize;
 #include "../Resources/ORBIS/FlatWorld/ScrollProjection.cginc"
 struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
 struct v2f {float4 pos:SV_POSITION;float2 chart:TEXCOORD0;};
 v2f vert(appdata v)
 {
  v2f o;float2 chart;float weight;float3 position=ScrollPosition(v.uv,0,chart,weight);
  o.pos=UnityObjectToClipPos(float4(position,1))*weight;o.chart=chart;return o;
 }
 float4 frag(v2f i):SV_Target {return float4(MarkleyDirection(i.chart),1);}
 ENDCG
 }
 }
}
