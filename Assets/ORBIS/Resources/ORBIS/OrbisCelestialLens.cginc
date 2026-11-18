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

// Angular variation of the glare radius in thousandths, counterclockwise
// from camera right. Fitted to the shapes of equal-brightness contours in the
// reference. Sampling a cyclic profile preserves irregular groups of rays
// without evaluating a stack of trigonometric noise layers for every pixel.
static const float OrbisGlareContour[96] = {
     95, 74,-13, 94, 97, 14,-13,-41,-32,-49,-32,  2, 20, 18, -4,-39,
    -61,-44,-67,-42,-51,-28, 63, 73, 73, 12,-64,-15, 64, 72, 62,-15,
    -74,-76,-53,-23,  4, 22, 32, 33, 30, 18, -1,-27,-54,-72,-65,-13,
     63, 95, 96, 49,-38,-38, 42, 96, 93, 88, 12,-59,-10,-54,-55,-39,
    -57,-44,-20,-10,-12,-33,-51,-36,-46,-27,-33, 46,100, 91,-14, 69,
     98,-28, 63, 99, 59, 17, 11, 13, 14, 13, 11, 13, 52, 93, 80,-34
};

float OrbisSunburstSpread(float2 d, float r2)
{
    if (r2 < 1e-10 || r2 >= .5) return 1;
    float angle = atan2(d.y, d.x);
    float samplePos = frac(angle / (2 * UNITY_PI)) * 96;
    int k = min((int)samplePos, 95);
    float4 contour = float4(OrbisGlareContour[(k + 95) % 96], OrbisGlareContour[k],
                            OrbisGlareContour[(k + 1) % 96], OrbisGlareContour[(k + 2) % 96]);
    float t = frac(samplePos);
    float2 tangent = .5 * (contour.zw - contour.xy);
    float width = lerp(contour.y, contour.z, t*t*(3-2*t)) +
                  t*(1-t)*lerp(tangent.x, -tangent.y, t);
    return 1 + .001 * width * (1 - smoothstep(.14, .50, r2));
}

// Radius is measured as a fraction of image height. The bright core is sensor
// glare; the visible solar disc and the disc tested for occlusion keep their
// astronomical angular radius. Work in radiance until the shared exposure pass.
float3 OrbisSolarGlare(float2 d)
{
    float r2 = dot(d, d);
    float spread = OrbisSunburstSpread(d, r2);
    float radius2 = r2 / (spread * spread);

    // A finite, steep central lobe clips to white. The wider red and green
    // shoulders give way to a broad, unsaturated champagne-coloured halo.
    float q = min(16, 100 * radius2);
    float q2 = q * q, q4 = q2 * q2;
    float3 core = 13.2 / (1 + (q4 * q4) * float3(.188, 1.92, 19.7));
    float3 shoulder = float3(2.75, 1.27, .68) * exp2(-147 * radius2);
    float3 scatter = float3(.0155, .0121, .00945) / (.029 + radius2);
    return core + shoulder + scatter;
}

// Half-intensity radii in the directions along and across the optical axis.
float OrbisSoftReflection(float2 lens, float2 centre, float2 inverseRadius)
{
    float2 q = (lens - centre) * inverseRadius;
    return exp2(-dot(q, q));
}

// Fixed sensor grain modulates only incident lens light. Exposure naturally
// clips the noise out of the white core. The pattern is stable between frames.
float OrbisLensGrain(uint2 pixel)
{
    uint h = pixel.x * 1597334677u + pixel.y * 3812015801u;
    h = (h ^ (h >> 16)) * 2246822519u;
    h ^= h >> 13;
    return 1 + .062 * (float(h & 1023u) / 1023 - .5);
}

float3 OrbisLensRadiance(OrbisLensVaryings i)
{
    if (!any(i.incident > 0)) return 0;
    float2 scale = float2(_LensSun.w, 1);
    float2 p = (i.uv - .5) * scale;
    float2 sun = (_LensSun.xy - .5) * scale;
    float2 d = p - sun;
    float offset = length(sun);
    float2 axis = offset > .0001 ? sun / offset : float2(1, 0);
    float2 lens = float2(dot(p, axis), dot(p, float2(-axis.y, axis.x)));
    // Field-dependent coma tilts the ghost train slightly and stretches the far
    // reflection across the axis. The defocused images merge as the Sun centres.
    lens.y += .05 * (offset - lens.x);
    float3 ghosts = float3(.094, .051, .043) * OrbisSoftReflection(lens, float2(.22 * offset, 0), float2(36, 35));
    ghosts += float3(.057, .037, .020) * OrbisSoftReflection(lens, float2(-.29 * offset, 0), float2(12.5, 15));
    ghosts += float3(.113, .080, .056) * OrbisSoftReflection(lens, float2(-1.29 * offset, 0), float2(8.0, 4.6));
    ghosts += float3(.054, .042, .033) * OrbisSoftReflection(lens, float2(-.50 * offset, 0), float2(1.90, 1.48));
    return OrbisLensGrain((uint2)i.pos.xy) * i.incident *
           (OrbisSolarGlare(d) + smoothstep(.035, .26, offset) * ghosts);
}
#endif
