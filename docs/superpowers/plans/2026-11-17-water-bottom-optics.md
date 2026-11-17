# Water-bottom visibility

Implement above-water views of submerged river/lake/coastal beds in the current Built-in renderer. Use the existing bathymetry, material fields and supplied SW3 wave normals. All effects share geographic coordinates across globe and local views.

The supplied SW3 package contains Refraction.hlsl, Caustics.hlsl and depth-dependent absorption in ForwardPass.hlsl. Its rendering integration uses URP opaque/depth textures. ORBIS reconstructs its underwater bed from the existing map fields. New optical code uses standard refraction with the already bound wave normals and procedural geographic light-band patterns.

1. Capture a current water view and add GPU regressions for shallow bed contrast, attenuation with depth/turbidity, animated versus stationary water and coordinate stability.
2. Reconstruct visible bed artwork with sand/gravel detail. Offset its coordinates using surface normals and the refracted view direction. Bound displacement near shorelines.
3. Add geographic light-band patterns warped by the sun's refraction through the SW3 normal field. Gate them by sunlight, water depth, suspended sediment and screen footprint. Preserve wave phase continuity and ice suppression.
4. Run native shader compilation, Unity water/coast/river/polar checks and core regressions. Inspect and capture representative lakes/rivers in Play mode. Measure the rendering cost.
5. Review, commit and push the verified changes to EmpireCraftIndependentProject main.

The implementation uses derived presentation fields and applies when existing worlds render. The effect retains the current strategic-map scale and artwork.

## Verification on 2026-11-17

- Baseline one-second calm-water animation changed mean RGB by 0.000000066. The final seven-second optical fixture changes it by 0.0150. Stationary water has zero change.
- Shallow-bed contrast is 0.0447, falling to 0.0000173 at 40 metres and 0.0197 with high suspended sediment.
- Reversing screen direction has zero error. Actual longitude-wrapped coordinates have maximum RGB error 0.000262. Polar and grazing-angle refraction stays inside the wet-boundary displacement limit.
- Native compilation passes all 64 Gamma/Linear D3D11 shader variants. Unity water, coast, river, polar, shadow, night, cloud, navigation and shader checks pass. Core regression passes 1,302,412 assertions, with 48,030 projection checks also passing.
- Play-mode views inspected include warm lake 78712, highland lake 30612, a clear river at 74119 and the returned globe view. Frame counters advance during verification. Captures are under the ignored ValidationOutput/RiverLake directory.
- Fixed 1280 by 720 warm-lake camera rendering with clouds off measures 1.68 ms before and 1.92 ms after on the RTX 5070 Laptop GPU. These synchronous medians include GPU readback.
- The final live Console has zero current errors and two existing interface/legacy-atlas warnings.

Review replaced screen-derivative caustic focusing with geographic procedural patterns after reproducing view-direction and longitude-seam errors. The corrected wrapped-coordinate and reversed-screen tests pass. The final light-band gain is limited to preserve bed readability in bright sunlight.
