#ifndef ORBIS_FLAT_CIRCULATION_OVERLAY
#define ORBIS_FLAT_CIRCULATION_OVERLAY

UNITY_DECLARE_TEX2D_NOSAMPLER(_GlebaWindFlowTex);
UNITY_DECLARE_TEX2D_NOSAMPLER(_GlebaCurrentFlowTex);
UNITY_DECLARE_TEX2D_NOSAMPLER(_GlebaBandsTex);
// x: mode (0 = off, 1 = SurfaceWinds, 2 = OceanCurrents, 3 = RossbyJets), y: real-time clock, z: month blend [0,1], w: ready
float4 _GlebaCirculation;

float3 GlebaFlowHash3(float2 p)
{
    float3 q = float3(
        dot(p, float2(127.1, 311.7)),
        dot(p, float2(269.5, 183.3)),
        dot(p, float2(419.2, 371.9)));
    return frac(sin(q) * 43758.5453);
}

void GlebaEvalStreamOctave(
    float2 uv, float2 dir, float spd, float nLat, float clock, float rateScale,
    out float inkOut, out float haloOut)
{
    inkOut = 0;
    haloOut = 0;
    float yScaled = clamp(uv.y, 0.001, 0.999) * nLat;
    float gyCenter = floor(yScaled);

    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
        float ry = gyCenter + dy;
        if (ry >= 0.0 && ry < nLat)
        {
            float ringLat = ((ry + 0.5) / nLat - 0.5) * 3.14159265;
            float cosRing = max(0.14, cos(ringLat));
            float nLon = max(6.0, floor(2.0 * nLat * cosRing + 0.5));
            float xScaled = frac(uv.x) * nLon;
            float gxCenter = floor(xScaled);

            [unroll]
            for (int dx = -1; dx <= 1; dx++)
            {
                float rx = gxCenter + dx;
                float wrapX = rx - floor(rx / nLon) * nLon;
                float3 h = GlebaFlowHash3(float2(wrapX + 0.37, ry + 0.73));

                // Isotropic local cell coordinates relative to jittered particle seed
                float2 jitter = (h.xy - 0.5) * 0.52;
                float2 rel = float2(xScaled - (rx + 0.5) - jitter.x, yScaled - (ry + 0.5) - jitter.y);

                float phase = frac(clock * rateScale * (0.36 + 0.58 * spd) + h.z);
                float lifeFade = sin(phase * 3.14159265);
                lifeFade = smoothstep(0.0, 0.78, lifeFade);

                float travel = 0.85 + 0.80 * spd;
                float headPos = (phase - 0.5) * travel;
                float2 perp = float2(-dir.y, dir.x);
                float along = dot(rel, dir) - headPos;
                float across = abs(dot(rel, perp));

                // Tapered trailing comet body behind headPos (along <= 0)
                float tailLen = 0.36 + 0.62 * spd;
                float tailT = saturate(1.0 + along / tailLen) * step(along, 0.035);
                float halfW = lerp(0.020, 0.068, tailT);
                float body = smoothstep(halfW + 0.032, max(0.004, halfW - 0.014), across) * pow(tailT, 1.35);

                // Crisp directional arrowhead chevron at leading tip
                float headWindow = step(-0.20, along) * step(along, 0.045);
                float armTarget = max(0.0, -along) * 0.62;
                float chevron = smoothstep(0.058, 0.014, abs(across - armTarget)) * headWindow * smoothstep(-0.20, -0.02, along);

                float shape = max(body, chevron * (0.65 + 0.35 * spd)) * lifeFade;
                float softHalo = smoothstep(halfW + 0.095, halfW * 0.4, across) * step(-tailLen - 0.04, along) * step(along, 0.07) * lifeFade;

                inkOut = max(inkOut, shape);
                haloOut = max(haloOut, softHalo);
            }
        }
    }
}

float GlebaBandStroke(float uvY, float bandV, float vPerPixel, float halfPixels, float dashMask)
{
    float distPx = abs(uvY - bandV) / max(1e-6, vPerPixel);
    return smoothstep(halfPixels + 0.85, max(0.15, halfPixels - 0.45), distPx) * dashMask;
}

float3 FlatApplyCirculationOverlay(float3 col, float2 uv, float pixelsPerTile, float waterMask, float waterKind)
{
    [branch]
    if (_GlebaCirculation.x < 0.5 || _GlebaCirculation.w < 0.5)
        return col;

    bool isWindMode = _GlebaCirculation.x < 1.5;
    bool isRossbyMode = _GlebaCirculation.x > 2.5;
    float2 wrappedUV = float2(frac(uv.x), saturate(uv.y));
    float blend = saturate(_GlebaCirculation.z);
    float clock = _GlebaCirculation.y;

    // 1. Sample monthly-interpolated velocity field (single LOD-0 lookup)
    float4 packedFlow = (isWindMode || isRossbyMode)
        ? FlatPresentationSample(_GlebaWindFlowTex, wrappedUV, 0)
        : FlatPresentationSample(_GlebaCurrentFlowTex, wrappedUV, 0);
    float2 vel = lerp(packedFlow.rg, packedFlow.ba, blend);
    float rawMag = length(vel);

    // Zoom-adaptive octave selection (~26 screen pixels per spherical flow cell)
    float pxPerGlobeHeight = _MapSize.y * max(0.45, pixelsPerTile);
    float targetCells = clamp(pxPerGlobeHeight / 26.0, 12.0, 384.0);
    float lod = log2(targetCells / 12.0);
    float lvl0 = floor(lod);
    float lvlFade = frac(lod);
    float nLat0 = floor(12.0 * exp2(lvl0) + 0.5);
    float nLat1 = nLat0 * 2.0;

    float spd = 0.0;
    float rateScale = 0.55;
    float vis = 0.0;
    float haloDim = 0.34;
    float inkGain = 0.64;
    float3 streamTint = float3(1.0, 1.0, 1.0);

    if (isWindMode)
    {
        // Draw Gleba's 5 dynamic atmospheric circulation fronts (ITCZ, N/S STHZ, N/S Polar Front)
        float vPerPixel = _MapSize.w / max(0.45, pixelsPerTile);
        float4 bandsA = FlatPresentationSample(_GlebaBandsTex, float2(wrappedUV.x, 0.25), 0);
        float4 bandsB = FlatPresentationSample(_GlebaBandsTex, float2(wrappedUV.x, 0.75), 0);

        float lonCycles = wrappedUV.x * max(24.0, nLat0 * 1.5);
        float dashSthz = smoothstep(0.32, 0.44, abs(frac(lonCycles) - 0.5) * 2.0);
        float dashPolar = smoothstep(0.28, 0.42, abs(frac(lonCycles * 1.25 + 0.25) - 0.5) * 2.0);

        float itczInk = GlebaBandStroke(wrappedUV.y, bandsA.r, vPerPixel, 1.15, 1.0);
        float itczHalo = GlebaBandStroke(wrappedUV.y, bandsA.r, vPerPixel, 2.35, 1.0);
        float sthzInk = max(
            GlebaBandStroke(wrappedUV.y, bandsA.g, vPerPixel, 0.95, dashSthz),
            GlebaBandStroke(wrappedUV.y, bandsA.b, vPerPixel, 0.95, dashSthz));
        float polarInk = max(
            GlebaBandStroke(wrappedUV.y, bandsA.a, vPerPixel, 0.90, dashPolar),
            GlebaBandStroke(wrappedUV.y, bandsB.r, vPerPixel, 0.90, dashPolar));

        col *= 1.0 - 0.26 * itczHalo;
        col = lerp(col, FlatColor(float3(1.00, 0.44, 0.16)), itczInk * 0.86);
        col = lerp(col, FlatColor(float3(0.34, 0.88, 1.00)), sthzInk * 0.72);
        col = lerp(col, FlatColor(float3(0.82, 0.66, 1.00)), polarInk * 0.68);

        spd = saturate(rawMag / 1.10);
        rateScale = 0.55;
        vis = smoothstep(0.03, 0.16, spd);
        haloDim = 0.34;
        inkGain = 0.62 + 0.30 * spd;
        streamTint = lerp(float3(0.92, 0.96, 1.00), float3(1.00, 0.95, 0.80), spd);
    }
    else if (isRossbyMode)
    {
        // Upper-tropospheric Rossby planetary wave jet streams (Polar Front & Subtropical Jets)
        float vPerPixel = _MapSize.w / max(0.45, pixelsPerTile);
        float4 bandsA = FlatPresentationSample(_GlebaBandsTex, float2(wrappedUV.x, 0.25), 0);
        float4 bandsB = FlatPresentationSample(_GlebaBandsTex, float2(wrappedUV.x, 0.75), 0);
        float lonCycles = wrappedUV.x * max(24.0, nLat0 * 1.5);
        float dashJet = smoothstep(0.28, 0.42, abs(frac(lonCycles) - 0.5) * 2.0);
        float subJetRef = max(
            GlebaBandStroke(wrappedUV.y, bandsA.g, vPerPixel, 0.85, dashJet),
            GlebaBandStroke(wrappedUV.y, bandsA.b, vPerPixel, 0.85, dashJet));
        float polJetRef = max(
            GlebaBandStroke(wrappedUV.y, bandsA.a, vPerPixel, 0.95, 1.0),
            GlebaBandStroke(wrappedUV.y, bandsB.r, vPerPixel, 0.95, 1.0));
        col = lerp(col, FlatColor(float3(0.42, 0.86, 1.00)), subJetRef * 0.48);
        col = lerp(col, FlatColor(float3(0.92, 0.62, 1.00)), polJetRef * 0.56);

        spd = saturate(rawMag / 0.022);
        rateScale = 0.78;
        vis = smoothstep(0.04, 0.22, spd);
        haloDim = 0.36;
        inkGain = 0.68 + 0.28 * spd;
        streamTint = lerp(float3(0.68, 0.92, 1.00), float3(1.00, 0.86, 0.38), smoothstep(0.20, 0.88, spd));
    }
    else
    {
        // OceanCurrents mode: mask strictly to marine ocean cells (waterKind 1..3 = Deep/Middle/ShallowOcean)
        float isOceanKind = step(0.5, waterKind) * (1.0 - step(3.5, waterKind));
        float marineMask = saturate(waterMask * isOceanKind);
        spd = saturate(rawMag / 0.18);
        rateScale = 0.44;
        vis = marineMask * smoothstep(0.02, 0.14, spd);
        haloDim = 0.38;
        inkGain = 0.64 + 0.32 * spd;
        streamTint = lerp(float3(0.62, 0.93, 1.00), float3(1.00, 0.93, 0.70), smoothstep(0.25, 0.85, spd));
    }

    [branch]
    if (vis > 0.005)
    {
        float2 dir = vel / max(1e-6, rawMag);
        float ink0, halo0, ink1, halo1;
        GlebaEvalStreamOctave(wrappedUV, dir, spd, nLat0, clock, rateScale, ink0, halo0);
        GlebaEvalStreamOctave(wrappedUV, dir, spd, nLat1, clock, rateScale, ink1, halo1);

        float w0 = 1.0 - smoothstep(0.55, 1.0, lvlFade);
        float w1 = smoothstep(0.0, 0.65, lvlFade);
        float streamInk = max(ink0 * w0, ink1 * w1) * vis;
        float streamHalo = max(halo0 * w0, halo1 * w1) * vis;

        col *= 1.0 - haloDim * streamHalo;
        col = lerp(col, FlatColor(streamTint), streamInk * inkGain);
    }

    return col;
}

#endif
