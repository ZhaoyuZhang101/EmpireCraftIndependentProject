#ifndef ORBIS_FLAT_ECOLOGY_DETAIL
#define ORBIS_FLAT_ECOLOGY_DETAIL
UNITY_DECLARE_TEX2D_NOSAMPLER(_EcologyTex);
float _EcologyReady;

// IDs are read at the authoritative texel centre. Only wetness is continuous.
// R vegetation cover, G biome, B soil wetness, A world-seeded visual variation.
float4 FlatEcologyData(float2 uv,float4 info)
{
 float2 id=floor(uv*_MapSize.xy);
 if(_EcologyReady<.5)return float4(-1,info.g>.5&&info.g<4.5?0:3,.5,PDHash(id));
 float4 ecology=FlatPresentationSample(_EcologyTex,(id+.5)*_MapSize.zw,0);
 return float4(floor(ecology.rg*255+.5),ecology.ba);
}

float FlatTreeDensity(float cover)
{
 if(cover<-.5)return 1;
 if(cover<.5||cover>12.5)return 0;
 if(cover<2.5)return cover<1.5?.16:.08;
 if(cover<3.5)return .55;
 if(cover<4.5)return .72;
 if(cover<9.5)return 1;
 if(cover<10.5)return .58;
 return .15;
}
#endif
