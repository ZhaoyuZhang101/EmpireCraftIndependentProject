#ifndef ORBIS_MARKLEY_INCLUDED
#define ORBIS_MARKLEY_INCLUDED
sampler2D _MarkleyDirections;
float4 _MarkleyTableSize;
float2 MarkleyFold(float2 uv){float row=floor(uv.y),k=floor(row*.5),odd=row-2*k;return float2(frac(odd>.5?.75-uv.x-k*.5:uv.x+k*.5),odd>.5?1-frac(uv.y):frac(uv.y));}
float3 MarkleyDirection(float2 uv){uv=MarkleyFold(uv);uv=(uv*(_MarkleyTableSize.xy-1)+.5)*_MarkleyTableSize.zw;return normalize(tex2Dlod(_MarkleyDirections,float4(uv,0,0)).xyz);}
float2 MarkleyGeoUV(float2 uv){float3 n=MarkleyDirection(uv);return float2(atan2(n.x,n.z)/6.28318530718+.5,asin(clamp(n.y,-1,1))/3.14159265359+.5);}
#endif
