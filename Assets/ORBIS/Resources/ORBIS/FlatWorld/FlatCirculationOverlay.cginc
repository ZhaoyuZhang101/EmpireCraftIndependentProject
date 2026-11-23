#ifndef ORBIS_FLAT_CIRCULATION_OVERLAY
#define ORBIS_FLAT_CIRCULATION_OVERLAY

UNITY_DECLARE_TEX2D_NOSAMPLER(_GlebaWindFlowTex);
UNITY_DECLARE_TEX2D_NOSAMPLER(_GlebaCurrentFlowTex);
UNITY_DECLARE_TEX2D_NOSAMPLER(_GlebaBandsTex);
// x: off / surface winds / ocean currents / Rossby jets / storm potential;
// y: real-time seconds, z: interpolated month fraction, w: ready.
float4 _GlebaCirculation;

bool GlebaDrawsCoasts()
{
    return _GlebaCirculation.x > .5 && _GlebaCirculation.x < 2.5 && _GlebaCirculation.w > .5;
}

struct GlebaFlowCoast
{
    float gridBed, gridShore, basin, marine, shorePixels;
};

GlebaFlowCoast GlebaFlowShore(float2 cell, sampler2D semantic, float kind,
                              float gridBed, float gridShore, float seaShore, float allShore,
                              float aa, float surveyMarine, float surveyLake)
{
    GlebaFlowCoast coast;
    coast.gridBed = gridBed;
    coast.gridShore = gridShore;
    coast.basin = smoothstep(-aa, aa, allShore);
    // seaShore is captured before the lake survey, so an inland freshwater
    // shoreline cannot acquire a marine outline even in a survey overlap.
    float2 dx, dy;
    FlatCellScreenDerivatives(cell, dx, dy);
    float2 ground = float2(cos((cell.y * _MapSize.w - .5) * UNITY_PI), .5 * _MapSize.x * _MapSize.w);
    float2 metric = lerp(float2(1, 1), ground, surveyMarine);
    float footprint = max(1e-7, max(length(dx * metric), length(dy * metric)));
    // A true signed distance can change by at most one ground displacement.
    // The sparse survey switches to a longer model distance in open water at
    // its page edge. That jump has no zero crossing and cannot mark a coast.
    float change = length(float2(ddx(seaShore), ddy(seaShore)));
    float perPixel = max(1e-7, min(change, 1.5 * footprint));
    float jump = smoothstep(2 * footprint, 3.5 * footprint, change);
    coast.shorePixels = max(abs(seaShore) / perPixel, 3 * jump);
    surveyMarine *= step(-2 * perPixel - aa, seaShore);
    kind = floor(kind + .5);
    bool needsKind = coast.shorePixels < 2 || (_GlebaCirculation.x > 1.5 && coast.basin > .001);
    if (needsKind && (kind < .5 || kind > 4.5) && surveyMarine < .999)
        kind = FlatAreaWaterKind(cell, semantic, kind);
    // Rivers have their own channel distance. Extend only neighbouring
    // marine classes or a measured strait through a dry simulation tile.
    coast.marine = max(step(.5, kind) * saturate(4 - kind), surveyMarine) * (1 - surveyLake);
    return coast;
}

float3 GlebaFlowHash(float2 cell)
{
    float3 h = frac(float3(cell.xyx) * float3(.1031, .1030, .0973));
    h += dot(h, h.yxz + 33.33);
    return frac((h.xxy + h.yzz) * h.zyx);
}

float2 GlebaVelocity(float2 uv, bool ocean)
{
    float4 pair = ocean ? FlatPresentationSample(_GlebaCurrentFlowTex, uv, 0)
                        : FlatPresentationSample(_GlebaWindFlowTex, uv, 0);
    return lerp(pair.rg, pair.ba, saturate(_GlebaCirculation.z));
}

// Reciprocal tangents map a geographic displacement to screen pixels. The
// sphere is continuous across the date line and a rotating polar local chart.
struct GlebaFlowView
{
    float3 radial, toPixelX, toPixelY;
    float radiansPerPixel;
};

GlebaFlowView GlebaFlowProjection(float2 uv)
{
    GlebaFlowView view;
    view.radial = FlatRadial(uv);
    float3 dx = ddx(view.radial), dy = ddy(view.radial);
    float3 x = cross(dy, view.radial), y = cross(view.radial, dx);
    float determinant = dot(dx, x);
    float reciprocal = (determinant < 0 ? -1 : 1) / max(1e-20, abs(determinant));
    view.toPixelX = x * reciprocal;
    view.toPixelY = y * reciprocal;
    // Largest singular value of the two screen derivatives. A glyph must
    // fit along the foreshortened axis as well as the long one at the limb.
    float xx = dot(dx, dx), yy = dot(dy, dy), xy = dot(dx, dy);
    view.radiansPerPixel = sqrt(max(1e-16, .5 * (xx + yy + length(float2(xx - yy, 2 * xy)))));
    return view;
}

float2 GlebaScreenVector(GlebaFlowView view, float3 displacement)
{
    return float2(dot(displacement, view.toPixelX), dot(displacement, view.toPixelY));
}

// Only the containing cell is needed. Cap both the stroke and its travel
// inside the cell so a neighbouring fragment never has to guess its velocity.
float2 GlebaArrowField(float2 uv, GlebaFlowView view, float rows, bool ocean,
                       float speedScale, float2 calm, float cycleRate)
{
    float row = min(floor(uv.y * rows), rows - 1);
    float ringCos = cos(((row + .5) / rows - .5) * UNITY_PI);
    float columns = max(3, floor(2 * rows * ringCos + .5));
    float stagger = frac(row * .61803399);
    float column = floor(frac(uv.x) * columns + stagger);
    float3 noise = GlebaFlowHash(float2(column - floor(column / columns) * columns, row) + .5);
    float2 jitter = (noise.xy - .5) * .12;
    float2 seed = float2(frac((column + .5 - stagger + jitter.x) / columns),
                         (row + .5 + jitter.y) / rows);

    // Both months are sampled at the same fixed geographic seed for every
    // pixel of this arrow. Gradients in the field cannot split its wings.
    float2 velocity = GlebaVelocity(seed, ocean);
    float speed = saturate(length(velocity) * speedScale);
    float spacing = UNITY_PI / (rows * view.radiansPerPixel);
    float visibility = smoothstep(calm.x, calm.y, speed) * smoothstep(12, 23, spacing);
    if (visibility <= 0) return 0;

    float sl, cl, sp, cp;
    sincos((seed.x - .5) * (2 * UNITY_PI), sl, cl);
    sincos((seed.y - .5) * UNITY_PI, sp, cp);
    float3 seedRadial = float3(sl * cp, sp, cl * cp);
    float3 east = float3(cl, 0, -sl), north = float3(-sl * sp, cp, -cl * sp);
    float2 direction = GlebaScreenVector(view, east * velocity.x + north * velocity.y);
    direction *= rsqrt(max(1e-16, dot(direction, direction)));
    float2 position = GlebaScreenVector(view, view.radial - seedRadial);

    // Travel distance expresses speed. Keeping the period independent of
    // changing monthly velocity also keeps a long-running clock continuous.
    float phase = frac(_GlebaCirculation.y * cycleRate + noise.z);
    float travel = min(spacing * .25, 4 + 12 * speed);
    float along = dot(position, direction) - (phase - .5) * travel;
    float across = abs(dot(position, float2(-direction.y, direction.x)));
    float glyphLength = min(lerp(15, 23, speed), spacing * .50);
    float halfLength = .5 * glyphLength;
    float shaft = length(float2(max(abs(along) - halfLength, 0), across)) - .70;
    // A pointed, open V. Its two upstream tips stay several pixels apart
    // from the shaft; the dark casing follows both wings and the shaft.
    float2 arm = float2(-.33 * glyphLength, .21 * glyphLength);
    float2 fromTip = float2(along - halfLength, across);
    float head = length(fromTip - arm * saturate(dot(fromTip, arm) / dot(arm, arm))) - .93;
    float distance = min(shaft, head);
    float life = smoothstep(0, .11, phase) * (1 - smoothstep(.89, 1, phase));
    return saturate(float2(.55, 1.48) - distance) * (visibility * life);
}

float GlebaBandStroke(float uvY, float bandV, float halfPixels, float dash)
{
    float delta = uvY - bandV;
    float perPixel = max(1e-7, length(float2(ddx(delta), ddy(delta))));
    return saturate(halfPixels + .5 - abs(delta) / perPixel) * dash;
}

float3 FlatApplyCirculationOverlay(float3 col, float2 uv, float pixelsPerTile, GlebaFlowCoast coast, float shoreClass)
{
    [branch] if (_GlebaCirculation.x < .5 || _GlebaCirculation.w < .5) return col;

    bool ocean = _GlebaCirculation.x > 1.5 && _GlebaCirculation.x < 2.5;
    bool rossby = _GlebaCirculation.x > 2.5 && _GlebaCirculation.x < 3.5;
    float2 wrapped = float2(frac(uv.x), saturate(uv.y));
    GlebaFlowView view = GlebaFlowProjection(wrapped);

    if (ocean)
    {
        // The diagnostic texture is a tile map. Fill resolved dry banks with
        // its own dry elevation ramp, and give newly resolved saltwater the
        // neutral ocean colour where the simulation has no ocean palette.
        float lift = 18 * saturate(coast.gridBed / 4000);
        float3 dry = FlatColor((float3(68, 72, 78) + float3(lift, lift, .9 * lift)) / 255);
        float exposed = 1 - smoothstep(-.85, .35, coast.gridShore);
        col = lerp(col, FlatColor(float3(.16, .25, .31)), exposed * coast.marine);
        col = lerp(dry, col, coast.basin);
    }
    else
    {
        float4 bands = FlatPresentationSample(_GlebaBandsTex, float2(wrapped.x, .25), 0);
        float southPolar = FlatPresentationSample(_GlebaBandsTex, float2(wrapped.x, .75), 0).r;
        float repeats = exp2(clamp(round(log2(max(1, _MapSize.y * pixelsPerTile / 17))), 5, 13));
        float cycles = wrapped.x * repeats;
        float dash = smoothstep(.32, .44, abs(frac(cycles) - .5) * 2);
        float polarDash = smoothstep(.28, .42, abs(frac(cycles * 1.25 + .25) - .5) * 2);
        float sub = max(GlebaBandStroke(wrapped.y, bands.g, .90, dash),
                        GlebaBandStroke(wrapped.y, bands.b, .90, dash));
        float polar = max(GlebaBandStroke(wrapped.y, bands.a, .90, rossby ? 1 : polarDash),
                          GlebaBandStroke(wrapped.y, southPolar, .90, rossby ? 1 : polarDash));
        if (rossby)
        {
            col = lerp(col, FlatColor(float3(.42, .86, 1)), sub * .48);
            col = lerp(col, FlatColor(float3(.92, .62, 1)), polar * .56);
        }
        else
        {
            col *= 1 - .26 * GlebaBandStroke(wrapped.y, bands.r, 2.0, 1);
            col = lerp(col, FlatColor(float3(1, .44, .16)), .86 * GlebaBandStroke(wrapped.y, bands.r, 1, 1));
            col = lerp(col, FlatColor(float3(.34, .88, 1)), sub * .72);
            col = lerp(col, FlatColor(float3(.82, .66, 1)), polar * .68);
        }
    }

    float speedScale = ocean ? 1 / .18 : (rossby ? 1 / .022 : 1 / 1.10);
    float2 calm = ocean ? float2(.02, .14) : (rossby ? float2(.04, .22) : float2(.03, .16));
    float receiverSpeed = saturate(length(GlebaVelocity(wrapped, ocean)) * speedScale);
    float coverage = smoothstep(calm.x, calm.y, receiverSpeed) * (ocean ? coast.basin * coast.marine : 1);
    [branch] if (coverage > .005)
    {
        float target = clamp(UNITY_PI / (42 * view.radiansPerPixel), 4, 32768);
        float level = log2(target / 4);
        float rows = 4 * exp2(floor(level));
        float blend = smoothstep(.35, .65, frac(level));
        float rate = ocean ? .52 : (rossby ? .78 : .60);
        float2 marks = 0;
        [branch] if (blend < 1) marks = (1 - blend) * GlebaArrowField(wrapped, view, rows, ocean, speedScale, calm, rate);
        [branch] if (blend > 0) marks = max(marks, blend * GlebaArrowField(wrapped, view, 2 * rows, ocean, speedScale, calm, rate));
        float3 slow = ocean ? float3(.67, .94, 1) : (rossby ? float3(.68, .92, 1) : float3(.93, .97, 1));
        float3 fast = rossby ? float3(1, .86, .38) : float3(1, .95, .76);
        col = lerp(col, FlatColor(float3(.035, .075, .12)), .88 * marks.y * coverage);
        col = lerp(col, FlatColor(lerp(slow, fast, receiverSpeed)), .97 * marks.x * coverage);
    }

    // The zero contour of the continuous basin distance follows the same
    // surveyed banks as the water view. Channels and tile/category changes
    // cannot draw a grid. This final hairline survives crossing wind arrows.
    if (GlebaDrawsCoasts())
    {
        // The packed alpha distinguishes onshore wet wind, offshore dry wind
        // and unclassified regions. RGB remains the Gleba pressure palette.
        float windClass = ocean ? 0 : clamp((shoreClass * 255 - 253) * .5, -1, 1);
        float3 bank = lerp(float3(.72, .80, .81), windClass > 0 ? float3(.31, .62, 1) : float3(1, .49, .36), abs(windClass));
        float width = lerp(1.02, 1.36, abs(windClass));
        col = lerp(col, FlatColor(float3(.055, .11, .15)), .82 * coast.marine * saturate(1.95 - coast.shorePixels));
        col = lerp(col, FlatColor(bank), .92 * coast.marine * saturate(width - coast.shorePixels));
    }
    return col;
}

// Both sphere renderers and both flat charts use the same field, projection
// and shoreline survey. Only the zero contour enters these diagnostics; no
// terrain lighting, river transport, plants or water optics are evaluated.
float3 FlatDiagnosticSurface(float4 display, float2 uv, float2 cell, sampler2D semantic,
                             float pixelsPerTile, out float areaWater)
{
    float4 field = FlatField(cell);
    float aa = clamp(.65 / max(1, pixelsPerTile), .008, .28);
    areaWater = smoothstep(-aa, aa, field.g);
    GlebaFlowCoast coast = (GlebaFlowCoast)0;
    if (GlebaDrawsCoasts() || _KoppenMap > .5)
    {
        float4 info = tex2Dlod(semantic, float4((floor(cell) + .5) * _MapSize.zw, 0, 0)) * 255;
        float gridBed = field.r, gridShore = field.g;
        float2 unusedGradient = 0, surveyCoast;
        FlatEarthRelief(uv, field.r, unusedGradient, surveyCoast);
        float strait = FlatSurveyBasinShore(uv, cell, semantic, info.g, field.g, surveyCoast);
        float seaShore = field.g;
        float lake = FlatEarthLake(uv, info.g, strait, field.g);
        aa = FlatEarthShoreAA(uv, aa, strait);
        areaWater = smoothstep(-aa, aa, field.g);
        if (_KoppenMap > .5)
            return FlatKoppenCoast(display.rgb, info, seaShore, aa, surveyCoast.y * (1 - lake));
        coast = GlebaFlowShore(cell, semantic, info.g, gridBed, gridShore, seaShore, field.g, aa, surveyCoast.y, lake);
    }
    return FlatApplyCirculationOverlay(display.rgb, uv, pixelsPerTile, coast, display.a);
}
#endif
