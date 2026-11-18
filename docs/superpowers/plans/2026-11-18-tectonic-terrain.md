# Plate-tectonic terrain and fluvial erosion

New worlds are generated in C# for every preset and seed. The baked World Atlas bed stays as a test fixture and import path. Generation no longer needs Python.

1. `FlatTectonics` builds the bed on the sphere.
   - Plates are a warped spherical Voronoi, from 11 to 18 plates per preset. Each plate rotates about its own Euler pole.
   - The relative velocity across each margin classifies it:
     - continental collision: a range, a plateau on the indented side and a foreland basin;
     - ocean–continent subduction: a trench, a coastal range and a volcanic arc;
     - ocean–ocean convergence: an island arc;
     - fast continental extension: a rift;
     - spreading: a mid-ocean ridge, with seafloor depth from ridge age;
     - transform: a fault trough.
   - Hotspots leave volcano chains upstream of plate motion.
   - Continental interiors get shallow sags and old eroded orogens.
   - Large enclosed sub-sea basins get a strait to the ocean.
   - The land share is a preset quantile of the crust field.
   - `World.Tectonics` records the plate per tile, the margin setting, seismicity from 0 to 1 and volcano sites with their kind and activity. These are inputs for later eruption and earthquake events. The record is not saved yet, and saves load with `Tectonics == null`.
2. `FlatErosion` runs once at world creation, never on load or brush rebuilds.
   - It runs 80 iterations of implicit stream-power incision with hillslope diffusion. Rock strength varies and the initial surface is slightly roughened.
   - Inland seas and deep basins from 10 to 160 reference tiles stay lakes. Every other closed basin is sediment-filled.
   - The result is remapped onto the source hypsometry by rank, so height bands, snow lines and biome shares keep their proportions.
   - The final integer bed descends strictly to water.
   - It also runs after reading the baked atlas.

## Verification on 2026-11-18

- All existing core suites pass on the changed code: self tests, river discharge, forest stands, wildlife, bathymetry, weather, moisture, environment, hydrology and cyclones.
- New `--tectonics` regression covers all four presets.
  - Determinism, plate ownership, volcano sites and hotspots.
  - Spreading and converging margins, seismic belts and land share against target.
  - Every dry tile has an outlet. Most land drains to the sea, and lakes cover less than 1/6 of land.
  - High ground exists, and the dateline seam is continuous.
  - 256×128: 0.6–1.1 s per world. 1024×512 full generation: 9.7 s.
- Default atlas with erosion:
  - Longest main stem grows from 17 to 53 tiles, and the median from 3 to 5.
  - Land draining first into a lake falls from 47% to 20%.
  - Lakes fall from 343 to 28. The large basins keep their area and depth.
- Unity 6000.0.55f1 Play-mode capture works, and the shaders compile without errors.
  - Run `-executeMethod Orbis.EditorTools.OrbisMapCapture.Run -orbisCaptureOut <dir>`.
  - It writes six views of the default world: atlas overview, regional atlas, atlas river zoom, local river, local mountain and globe.
  - The earlier Unity regression entry points were not rerun.

## Map readability

Two shader globals are set by `FlatMapRenderer`. Each is disabled at 0.

- `_RiverMinPixels` (2.4): when zoomed out, main channels keep about 2.4 screen pixels of width. The minimum is weighted by channel width and capped at 0.28 tile, inside the river atlas field. It has no effect at local zoom.
- `_RiverTint` (0.38): river channels blend toward clear blue, so they read against the land.

Seafloor noise drops from 220 m to 70 m at a lower frequency. The overview ocean is calmer.
