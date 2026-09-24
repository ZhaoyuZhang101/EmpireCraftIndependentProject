# ORBIS celestial visuals and HP cloud design

## Scope

In the existing globe view, show one Sun, one phase-aware Moon, and a space skybox. The Sun follows `OrbisDemo.VisualSun`; the Moon follows `OrbisNightLighting.EvaluateMoon`. Their orbital movement uses the existing calendar. The orbital path itself stays hidden in the globe view. The 2D map retains its current presentation.

Import only the selected SolarSystem artwork and its Unity metadata. Do not import its camera, time, UI, or orbit-controller scripts, and do not replace project render settings. Keep the Built-in Render Pipeline.

## Rendering

The selected space skybox is the background only in globe view. Use the package's Sun and Moon appearance as camera-facing celestial discs synchronized with the existing sunlight and moonlight. Replace, rather than duplicate, the current procedural Sun disc. The Moon is visual only: ORBIS remains authoritative for phase and night lighting. Keep orbital source artwork for reference, but do not use its calendar controller.

## HP clouds

The supplied HPVolumeCloud archive contains an HDRP HLSL reading edition, not a compile-ready renderer. Port the relevant density shaping and isotropic multiple-scattering math into ORBIS's existing Built-in volumetric cloud shader and bind it to existing ORBIS weather textures, cloud shadows, and quality settings. Preserve the current visual fallback and credit HanPi Volume Cloud in project notices.

## Vendor packages

UniStorm's shape/detail noise and cloud profile are already bridged into ORBIS. Stylized Water 3 v3.0.1 requires URP; only its wave textures are currently reused. Crest v5.6.3 supports Built-in, but full integration would replace ORBIS's ocean/river geometry and is outside this celestial/cloud change. Do not import the complete packages here.

## Acceptance

Globe camera displays one aligned Sun, a Moon whose position and illuminated side match ORBIS's calendar, and the selected star background. Map mode remains unchanged. No missing-script or pink-material imports. Cloud rendering compiles in Unity 6000.0.55f1 Built-in, preserves weather-driven coverage, and its added scattering respects the cloud-quality budget. Verification records Unity compile and visual limitations separately.
