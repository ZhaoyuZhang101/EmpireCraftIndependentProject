#ifndef ORBIS_CELESTIAL_LENS
#define ORBIS_CELESTIAL_LENS
#include "UnityCG.cginc"

// Optical references: mu6k's Lens Flare (shadertoy.com/view/4sX3Rs) and
// Reinder Nijhoff's Planet Shadertoy (shadertoy.com/view/4tjGRh).

float4 _LensSun; // viewport centre, incident intensity, aspect
sampler2D _LensTransmission;

struct OrbisLensVaryings
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 incident : TEXCOORD1;
};

OrbisLensVaryings OrbisLensVertex(appdata_img v)
{
    v2f_img screen = vert_img(v);
    OrbisLensVaryings o;
    o.pos = screen.pos;
    o.uv = screen.uv;
    o.incident = 0;
    if (_LensSun.z > 0)
    {
        // Each bilinear read averages four of the 8x8 equal-area disc samples.
        // Incident light is constant across the image, so reduce it at the quad vertices.
        [unroll] for (int y = 0; y < 4; ++y)
        [unroll] for (int x = 0; x < 4; ++x)
            o.incident += tex2Dlod(_LensTransmission, float4((float2(x, y) + .5) * .25, 0, 0)).rgb;
        o.incident *= _LensSun.z / 16;
    }
    return o;
}

// Internal reflections run along the optical axis; oblique incidence shifts the
// colour edges and compresses the aperture in the radial direction.
float3 OrbisApertureGhost(float2 p, float2 axis, float eccentricity, float radius, float spread)
{
    float2 q = float2(dot(p, axis), dot(p, float2(-axis.y, axis.x))) / radius;
    q.x *= 1 + .55 * eccentricity;
    float3 x = abs(q.x + float3(-1, 0, 1) * spread * eccentricity);
    float3 blades = max(abs(q.y), .8660254 * x + .5 * abs(q.y));
    float3 edge = lerp(blades, sqrt(x*x + q.y*q.y), .44);
    float3 rim = 1 - smoothstep(.025, .28, abs(edge - .88));
    float rimLight = saturate(.52 - .38 * q.x + .14 * q.y);
    return .24 * (1 - smoothstep(.44, 1.09, edge)) + (.18 + .50 * rimLight) * rim;
}

float3 OrbisLensRadiance(OrbisLensVaryings i)
{
    if (!any(i.incident > 0)) return 0;
    float2 scale = float2(_LensSun.w, 1);
    float2 p = (i.uv - .5) * scale;
    float2 sun = (_LensSun.xy - .5) * scale;
    float2 d = p - sun;
    float r2 = dot(d, d), r = sqrt(r2);
    float offset = length(sun);
    float2 axis = offset > .0001 ? sun / offset : float2(1, 0);
    float eccentricity = saturate(offset * 1.45);

    // Forward scatter around the angular disc, plus a narrow diffraction streak.
    // The photosphere itself is drawn once by OrbisBackgroundWithSun.
    float corona = .29 / (1 + r2 * 2400) + .060 * exp2(-r * 13);
    float streak = .095 * exp2(-abs(d.y) * 700 - abs(d.x) * 17);
    float2 blades = abs(.866 * d.y + float2(-.5, .5) * d.x);
    float2 rays = exp2(-blades * 850 - r * 49);
    float3 glare = float3(1, .84, .61) * (corona + .035 * (rays.x + rays.y)) + float3(.73, .83, 1) * streak;

    // Distortion changes the spacing as the sun moves towards the frame edge.
    float2 warped = p * (1 + .20 * dot(p, p));
    float3 ghosts = float3(.016, .053, .062) * OrbisApertureGhost(warped + .47 * sun, axis, eccentricity, .039, .16);
    ghosts += float3(.057, .027, .010) * OrbisApertureGhost(warped + 1.10 * sun, axis, eccentricity, .065, .12);
    float2 glint = (warped - .31 * sun) / .016;
    ghosts += float3(.050, .085, .068) * exp2(-dot(glint, glint));
    return i.incident * (glare + smoothstep(.035, .24, offset) * ghosts);
}
#endif
