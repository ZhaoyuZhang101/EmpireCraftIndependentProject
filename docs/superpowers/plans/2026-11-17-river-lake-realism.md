# River and lake improvements

Improve natural river/lake behaviour at the existing strategy-map scale. Preserve monthly climate, bounded generation workers and the save schema.

1. Establish baseline lake outlet counts, channel coverage and representative Unity views.
2. Route each connected equal-level lake through one main spill and rebuild the downstream-first order. Check conservation, downhill heads and connected outlets.
3. Derive river meander strength and depth from discharge and confinement. Share the resulting shape field between CPU coverage and shaders, preserving common tile-face sections.
4. Improve shallow banks/deep channel shading, sediment entering lakes, calm lake ripples and depth-based freshwater colour. Keep SW3 resources and the cached visual weather.
5. Run hydrology/core/save/determinism tests, Unity shader and water regressions, then inspect river/lake views in Play mode. Commit and push the verified changes to EmpireCraftIndependentProject.

Water generation runs during world creation or an explicit terrain rebuild. Derived visual fields may refresh when older saves load. Existing saved drainage remains intact until a rebuild.

## Verification on 2026-11-17

- Default atlas has 371 connected lake basins. Basins with multiple outlets decreased from 14 to 0. Four generated terrain presets also pass downhill routing, conservation and outlet checks. Save round trips retain drainage.
- Core regression passes 1,302,412 assertions. Environment regression and 48,030 local projection checks pass.
- Actual Unity Job System output matches the serial oracle across all map layers in 270,639 assertions, including signed meanders, dirty updates and snapshot invalidation.
- All twelve existing Unity validation entry points pass, including water, coasts, river geometry, shadows, polar detail, navigation and cloud checks. All 64 native D3D11 shader variants compile in Gamma and Linear.
- CPU/GPU river distance error is below 0.000001. Shore detail error is below 0.00001. Underwater material boundaries blend colours continuously.
- The river-cell depth regression reproduces a 0.1040066 colour jump inside adjacent lake/ocean water before the fix. The corrected shader produces zero difference.
- Play-mode inspections cover highland lake 30612, warm lake 78712, lowland river 37212 and main mouth 68227. Frame counters advance during inspection. Captures are under the ignored ValidationOutput/RiverLake directory.
- Final mouth fixture with clouds disabled measures 1.63 ms median synchronous camera rendering at 1280 by 720 on the RTX 5070 Laptop GPU. The measurement includes GPU readback and represents that fixture.

The existing strategic-map artwork is retained. Shoreline detail, river depth and meanders are derived presentation fields. Generic terrain generation now retains metre-scale elevations for drainage. Brush height presets retain their existing meanings.
