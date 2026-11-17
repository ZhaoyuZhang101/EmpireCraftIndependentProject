# Lightning and material diagnostics

Repair the reported lightning visuals and current material-property errors in the Built-in project.

The SW3 audit confirmed all five textures on the visible local spherical material and matching hashes in the supplied package. The audit also read undeclared properties on a hidden legacy surface material, producing seven Console errors. Declare that surface material's shared water properties and validate safe resource round trips.

The current map lightning uses a floor-quantized sinusoidal brightness pulse. The four shared flash slots stay empty. Replace this with four bounded transient visual events, warm/wet climate candidate selection, a short two-pulse envelope and actual cloud/rain masks in the shaders. Both globe and local views consume the same event directions and strengths. Pausing or disabling clouds clears the flashes.

1. Add failing material-property and lightning lifetime/gating checks.
2. Declare the missing surface properties and implement the bounded visual flash source.
3. Replace quantized map pulses with the shared geographic flash shader. Limit cloud illumination to dense storm support.
4. Run core/GPU/shader checks and native Play-mode flash captures. Confirm fresh Console state and push verified changes to EmpireCraftIndependentProject main.

Diagnostic logs are retained under ignored ValidationOutput/Lightning. The patch preserves the current lightweight visual weather and existing world-save data.

The Console audit also found a theme warning emitted while constructing an unconfigured PanelSettings instance, an unused legacy-atlas warning in the atmosphere-only view and mixed line endings in the water include. Instantiate a themed panel template, restrict the atlas diagnostic to views that use it and normalize edited shader line endings.

## Verification on 2026-11-17

- The original shader produced lightning in clear columns, with clouds disabled and with no active event. GPU regressions now return zero in each case. A dense storm produces a localized positive flash, covering four pixels in the fixed global probe.
- The material regression originally failed on ORBIS/Surface. All five supplied SW3 texture properties now survive bind/clone/read-back checks on all four water shaders.
- Core regression passes 1,311,672 assertions, including event lifetime, double pulses, dry-climate exclusion, spherical directions, seed repeatability and pause/disable clearing. A separate 0.1-second-step test reproduced a skipped flash peak, then passed after interval-maximum envelope sampling.
- All 64 native Gamma/Linear D3D11 shader variants compile. Unity lightning, water, coast, river, polar, shadow, night and shader checks pass. Local projection regression passes 48,030 checks.
- Native controlled captures use an existing cached storm column with full rain support. The same geographic flash appears locally and on the globe. Captures are under ignored ValidationOutput/Lightning. Normal frame counters advance during verification.
- The runtime HUD loads OrbisTheme through the panel template. Fresh Play-mode and post-test Console ground truth report zero errors and zero warnings. The scene is clean after leaving Play mode.
- Temporary background-player settings and Editor serialization changes were restored to the tracked project configuration.
