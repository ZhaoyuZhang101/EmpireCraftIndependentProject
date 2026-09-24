# Celestial visuals and HP clouds implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Sun, Moon, orbital movement, and skybox to ORBIS globe view; adapt HP cloud scattering to ORBIS Built-in volumetric clouds.

**Architecture:** Keep ORBIS as the source of solar and lunar direction. Selectively copy SolarSystem art with `.meta` files; render celestial appearance through ORBIS's existing camera and optical composite. Port HP math into a bounded shader helper, leaving ORBIS weather and cloud geometry authoritative.

**Tech Stack:** Unity 6000.0.55f1, Built-in Render Pipeline, C#, ShaderLab/Cg, PowerShell, Python archive inspection.

---

### Task 1: Select and import artwork

- [ ] Compute dependency closure for selected SolarSystem Sun, Moon, skybox, and orbit assets using Unity GUIDs.
- [ ] Copy only required textures, meshes, materials, and metadata to a dedicated ORBIS vendor-art directory; retain source GUIDs.
- [ ] Verify every copied serialized GUID resolves either to a copied file or a Unity built-in resource.

### Task 2: Globe-only celestial appearance

- [ ] Add a focused celestial visual component that receives current `VisualSun`, visual day, camera, and map state from `OrbisDemo`.
- [ ] Bind the space skybox only for globe mode, preserving map mode's clear behavior.
- [ ] Use the Sun source appearance in the existing optical Sun disc path, preventing double rendering.
- [ ] Draw the Moon at `OrbisNightLighting.EvaluateMoon`'s direction and derive its lit hemisphere from the same Sun vector.
- [ ] Confirm orbital movement freezes when paused and resumes with the ORBIS calendar.

### Task 3: HP cloud adaptation

- [ ] Add a deterministic shader check covering thin-edge suppression and thick-cloud diffuse saturation, then observe it fail against the current reduced model.
- [ ] Implement a bounded, documented HP-derived density/scattering helper in the existing volume pass without introducing HDRP dependencies.
- [ ] Preserve direct light, shadow atlas, and quality budgets; verify the shader check passes.
- [ ] Add required HanPi attribution and an honest description of the port's scope.

### Task 4: Verify and document

- [ ] Run static GUID, package, and source checks.
- [ ] Import and compile with the installed Unity Editor; inspect Editor log for errors.
- [ ] Run relevant ORBIS validation commands and capture actual results.
- [ ] Document how UniStorm, SW3, and Crest relate to this implementation and remaining full-integration work.
