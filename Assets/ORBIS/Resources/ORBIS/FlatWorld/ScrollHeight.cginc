#ifndef ORBIS_SCROLL_HEIGHT
#define ORBIS_SCROLL_HEIGHT
float2 _ScrollPoleHeights;
float _ScrollHeightScale;
float ScrollRadius(float4 field,float2 uv,float rows)
{
 float h=clamp(field.r+(field.g>0?field.b:0),0,4000);
 float polar=lerp(_ScrollPoleHeights.x,_ScrollPoleHeights.y,step(.5,uv.y));
 float blend=smoothstep(0,1,min(uv.y,1-uv.y)*rows);
 return 1+lerp(polar,h,blend)*_ScrollHeightScale;
}
#endif
