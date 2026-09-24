// SPDX-License-Identifier: MPL-2.0
// Adapted from AshenOneArt / HPWater, HPWaterVolumetrics.hlsl,
// CaculateScatteredLight and FresnelSchlick; source retrieved 2026-09-12.
// https://github.com/AshenOneArt/HPWater
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// ORBIS modifications: componentwise safe extinction, bounded path,
// no HDRP dependencies, homogeneous optical segment rather than ray marching.
#ifndef ORBIS_HP_WATER_OPTICS
#define ORBIS_HP_WATER_OPTICS
float3 OrbisHPScatter(float3 incident, float3 absorption, float3 scattering,
                      float pathMeters, out float3 transmission)
{
    float3 extinction=max(float3(1e-6,1e-6,1e-6),absorption+scattering);
    transmission=exp(-extinction*max(0,pathMeters));
    return incident*(1-transmission)*max(0,scattering)/extinction;
}
float OrbisHPSchlick(float cosine)
{
    float facing=1-saturate(cosine);
    return .02037+.97963*facing*facing*facing*facing*facing;
}
#endif
