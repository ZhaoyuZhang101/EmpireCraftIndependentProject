Shader "Hidden/ORBIS/TreeBanks"
{
 SubShader { Pass {
 Cull Off ZWrite Off ZTest Always
 CGPROGRAM
 #pragma target 3.0
 #pragma editor_sync_compilation
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "FlatSurfaceField.cginc"
 #include "FlatTreePlacement.cginc"
 sampler2D _InfoTex;
 float2 frag(v2f_img i):SV_Target
 {
    // Address the texel itself. Blit UV orientation and half-precision
    // interpolators must not move a tree to a different row or column.
    float2 id=floor(i.pos.xy);
    float2 root=(id+PDTreePlacement(id).xy/32)*_MapSize.zw;
    float shore=FlatVisualSample(float4(root,0,0)).g;
    float kind=tex2Dlod(_InfoTex,float4((id+.5)*_MapSize.zw,0,0)).g*255;
    return FlatEarthTreeBank(root,kind,shore);
 }
 ENDCG
 }}
}
