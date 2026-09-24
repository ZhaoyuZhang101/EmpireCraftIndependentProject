Shader "ORBIS/Display"
{
 Properties {_MainTex("Linear scene",2D)="black"{}}
 SubShader {Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.5
 #pragma vertex vert_img
 #pragma fragment frag
 #include "OrbisOptics.cginc"
 sampler2D _MainTex;
 float4 frag(v2f_img i):SV_Target {
    float3 c=max(0,tex2D(_MainTex,i.uv).rgb);
    if(_SurfaceDebug>0){
      #if defined(UNITY_COLORSPACE_GAMMA)
      c=LinearToGammaSpace(c);
      #endif
      return float4(c,1);
    }
    return float4(OrbisDisplay(c),1);
 }
 ENDCG
 }} Fallback Off
}
