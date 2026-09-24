Shader "Hidden/ORBIS/WaterFiniteProbe"{SubShader{Pass{Cull Off ZWrite Off ZTest Always
CGPROGRAM
#pragma vertex vert_img
#pragma fragment frag
#pragma target 3.0
#include "UnityCG.cginc"
#include "../Resources/ORBIS/FlatWorld/Water/CrestPairedWaves.cginc"
float4 frag(v2f_img i):SV_Target {return float4(OrbisCrestSlope(float3(.3,.4,.866),123,lerp(.1,4000,i.uv.x)),0,1);}
ENDCG
}}}
