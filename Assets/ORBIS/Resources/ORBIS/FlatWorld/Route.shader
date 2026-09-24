Shader "ORBIS/FlatWorld/Route" {
 SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" } Pass {
 Blend SrcAlpha OneMinusSrcAlpha Cull Off ZWrite Off
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct v2f { float4 pos:SV_POSITION;float4 color:COLOR; };
 v2f vert(appdata_full v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.color=v.color;return o;}
 float4 frag(v2f i):SV_Target {return i.color;}
 ENDCG
 } }
}
