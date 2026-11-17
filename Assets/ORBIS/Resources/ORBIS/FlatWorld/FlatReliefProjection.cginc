#ifndef ORBIS_FLAT_RELIEF_PROJECTION
#define ORBIS_FLAT_RELIEF_PROJECTION
// Weight, geographic centre surface height, oblique viewing slope.
float4 _ScrollRelief;
float _ScrollHeightScale;
float FlatReliefClipWeight(float height,float morph)
{
    return 1-(height-_ScrollRelief.y)*_ScrollRelief.x*morph/.18;
}
float3 FlatReliefPosition(float2 coordinate,float height)
{
    float relative=(height-_ScrollRelief.y)*_ScrollRelief.x;
    float perspective=.18/(.18-relative);
    return float3(float2(coordinate.x,coordinate.y+relative*_ScrollRelief.z)*perspective,height*_ScrollRelief.x);
}
#endif
