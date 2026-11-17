#ifndef ORBIS_FLAT_VISUAL_TEXTURE
#define ORBIS_FLAT_VISUAL_TEXTURE
// One bilinear sampler serves both fields. Inline state also works in probes
// where the compiler removes the unused visual texture.
UNITY_DECLARE_TEX2D_NOSAMPLER(_VisualTex);
#ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
SamplerState orbis_linear_repeatU_clampV_sampler;
#define FlatPresentationSample(texture,coordinate,lod) texture.SampleLevel(orbis_linear_repeatU_clampV_sampler,coordinate,lod)
#else
#define FlatPresentationSample(texture,coordinate,lod) tex2Dlod(texture,float4(coordinate,0,lod))
#endif
#define FlatVisualSample(coordinate) FlatPresentationSample(_VisualTex,(coordinate).xy,(coordinate).w)
#endif
