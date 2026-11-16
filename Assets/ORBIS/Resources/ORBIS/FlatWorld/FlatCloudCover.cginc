#ifndef ORBIS_FLAT_CLOUD_COVER
#define ORBIS_FLAT_CLOUD_COVER
sampler2D _SkyTex;
float _SkyClock;
// The HP radial integral is the macro-scale cloud column. Reconstruct its
// sub-texel silhouette in spherical coordinates for both visible clouds and
// their ground shadows, so zoom never exposes square cache pixels.
float4 FlatCloudColumn(float2 uv)
{
    float4 column=tex2D(_SkyTex,uv);
    float3 radial=FlatRadial(uv);
    float3 drift=float3(_SkyClock*.002,0,_SkyClock*.001);
    float detail=FlatDetailNoise(radial*66+drift);
    // Detail may break up an existing cloud edge, but must never create
    // isolated specks in an otherwise clear HP column.
    column.r*=.92+.08*detail;
    column.g*=.90+.10*detail;
    return column;
}
float FlatCloudShadowOpacity(float2 uv,float3 sun,float enabled)
{
    if(enabled<.001)return 0;
    float3 radial=FlatRadial(uv);
    // Intersect the sunlight ray with a spherical cloud layer. A longitude
    // offset divided by cos(latitude) displaced/stretched polar cloud shadows.
    float mu=dot(radial,sun),height=.006;
    float travel=-mu+sqrt(mu*mu+height*(2+height));
    float3 cloudDirection=normalize(radial+sun*travel);
    float2 shadowUV=float2(frac(atan2(cloudDirection.x,cloudDirection.z)/6.283185307+.5),
                              asin(clamp(cloudDirection.y,-1,1))/3.141592654+.5);
    float4 cloud=FlatCloudColumn(shadowUV);
    return saturate(cloud.r*.82+cloud.g*.18)*enabled*smoothstep(-.02,.12,mu);
}
#endif
