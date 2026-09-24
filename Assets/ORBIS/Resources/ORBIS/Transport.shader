Shader "ORBIS/Transport"
{
 SubShader { Tags {"Queue"="Transparent+30" "RenderType"="Transparent"} Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct app {float4 vertex:POSITION;float4 color:COLOR;};
 struct outv {float4 pos:SV_POSITION;float4 color:COLOR;};
 outv vert(app v){outv o;o.pos=UnityObjectToClipPos(v.vertex);o.color=v.color;o.color.rgb=GammaToLinearSpace(max(0,o.color.rgb));return o;}
 float4 frag(outv i):SV_Target{return i.color;}
 ENDCG }
 }
}
