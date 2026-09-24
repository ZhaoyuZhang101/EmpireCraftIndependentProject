Shader "ORBIS/WeatherParticles"
{
 Properties { _Mode("0 rain 1 splash 2 lightning",Float)=0 }
 SubShader
 {
  Tags { "Queue"="Transparent+25" "RenderType"="Transparent" }
  Blend SrcAlpha OneMinusSrcAlpha Cull Off ZWrite Off ZTest LEqual
  Pass
  {
   CGPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   float _Mode;float3 _Sun;
   struct app {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
   struct vary {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
   vary vert(app v){vary o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   float4 frag(vary i):SV_Target
   {
    float alpha=1;
    if(_Mode<.5)alpha=pow(saturate(1-abs(i.uv.x-.5)*2),2)*sin(saturate(i.uv.y)*3.14159265);
    else if(_Mode<1.5)alpha=exp(-abs(length(i.uv-.5)*2-.72)*30);
    return float4(i.color.rgb,saturate(i.color.a*alpha));
   }
   ENDCG
  }
 }
 Fallback Off
}
