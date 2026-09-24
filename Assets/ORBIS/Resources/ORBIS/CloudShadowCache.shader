Shader "ORBIS/CloudShadowCache"
{
 SubShader { Cull Off ZWrite Off ZTest Always
 Pass {
 CGPROGRAM
 #pragma target 3.5
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "OrbisCloudCommon.cginc"
 int _ShadowSteps;
 float4 frag(v2f_img i):SV_Target
 {
    float slice=min(7.0,floor(i.uv.x*8.0));
    float2 uv=float2(frac(i.uv.x*8.0),i.uv.y);
    float2 xy=(uv*2.0-1.0)*ORBIS_CLOUD_TOP;
    float impact2=dot(xy,xy),outer2=ORBIS_CLOUD_TOP*ORBIS_CLOUD_TOP;
    if(impact2>=outer2)return 0;
    float top=sqrt(max(0.0,outer2-impact2));
    float inner2=ORBIS_CLOUD_BASE*ORBIS_CLOUD_BASE;
    float bottom=impact2<inner2?sqrt(max(0.0,inner2-impact2)):-top;
    float z=lerp(top,bottom,slice/7.0);
    int shadowSteps=clamp(_ShadowSteps,1,32);
    float ds=max(0.0,top-bottom)/shadowSteps,tau=0;
    float3 basePoint=_OrbisShadowRight*xy.x+_OrbisShadowUp*xy.y;
    [loop] for(int k=0;k<32;k++)
    {
        if(k>=shadowSteps)break;
        // Every depth layer uses the SAME quadrature nodes. Partial last bins
        // give monotone optical depth instead of unrelated noisy integrations.
        float segmentTop=top-k*ds;
        if(segmentTop<=z)break;
        float covered=max(0.0,segmentTop-max(z,segmentTop-ds));
        tau+=OrbisCloudDensity(basePoint+_OrbisShadowSun*(top-(k+.5)*ds))*covered*ORBIS_EXTINCTION;
    }
    return float4(min(16.0,tau),0,0,1);
 }
 ENDCG }
 }
 Fallback Off
}
