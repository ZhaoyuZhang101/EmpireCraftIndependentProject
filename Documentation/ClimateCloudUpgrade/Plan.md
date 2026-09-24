# Climate, ecotones and planetary cloud alignment

Goal: Improve ORBIS toward the supplied PlanetVolumetricCloudsV3 visual structure, with moisture-driven clouds and continuous ecology. Preserve Built-in, saves and bounded workers.

Reference: Gleba common/model.ron exposes pressure/prevailing-wind weights and seasonal transport controls. PlanetCloudDemo is a compiled URP demo with independent shape/detail 3D volumes, cloud profiles, eight light samples and ambient top/bottom controls. It is a reference, not a directly importable project.

- [x] Add a bounded monthly moisture-column transport solver with evaporation, soil recycling, conservative advection, lifting/condensation and water-budget diagnostics; couple precipitation to existing drainage.
- [x] Feed humidity/condensation and prevailing winds into the common planetary cloud weather cache.
- [x] Blend ecological albedo through continuous soil water/canopy fields with spherical domain-warp noise, preserving water/ice/semantic overlays.
- [x] Improve cloud body/erosion scale and light-depth shading using separate detail volume, denser light integration and distinct cloud types.
- [x] Add evolving 12-layer atmospheric weather to the flat-world presentation; drive both views from its cloud condensate, rain and charged-storm lightning. Seed cyclone disturbances from the monthly climate and actual offshore cells.
- [x] Shape generated ocean-bed elevation into shelves, continental slopes, ridges and trenches; enhance depth-gradient relief in the water shader.
- [x] Validate water budgets, dry-air cloud dissipation, rain/lightning gating, serial/parallel equivalence, climate-selected cyclones, polar/dateline seams, water/ice exclusions, shader compile, representative GPU renders and current Unity errors.

The monthly climate is a coarse equilibrium column model with prescribed circulation. Its derived weather presentation now evolves through the existing 12-layer conservative atmosphere on a 64×32 sphere, with a bounded background worker; it does not overwrite the authoritative monthly climate, rivers or saves. The ocean-floor generation change applies to newly generated worlds. The bundled atlas and old saves retain their own elevation values, which already include non-flat beds. PlanetVolumetricCloudsV3 is a compiled URP demo; matching its appearance pixel for pixel is unverified.

Evidence: `gpu-regression.txt`, `../LocalPlanetUpgrade/validation.txt`, `../LocalPlanetUpgrade/water-cloud-regression.txt`, `runtime-after-carving.png`. The editor Play view ran the dynamic cache at 512×256 with no console errors; its sampled 64×32 cloud state had mean 0.256 coverage and 26% of cells above 0.5 before edge carving. The synthetic atmosphere test observed cloud condensate falling from 68.45 to zero in dry air, no rain or lightning with the atmospheric water removed, and water-budget error below 1e-9 in its ordinary evolving case.
