# River, local navigation and workers implementation plan

> For agentic workers: use superpowers:subagent-driven-development for the independent worker pipeline task; navigation and river changes share the primary agent's visual validation.

Goal: camera-centred unfolding without return-drag jumps, curved channels with bounded discharge-dependent width, and a working parallel data preparation architecture now.

Architecture: retain authoritative WorldData and Built-in presentation. Capture a view-aligned geodesic chart before morphing, use one quaternion for globe orientation. River width depends on raw conserved discharge (not normalized stream selection thresholds); share channel faces while adding bounded interior meanders. Use Unity Job System/Burst/NativeArray for parallel render-data packing, publish completed versioned snapshots on the main thread, and bound managed generation workers.

Tech Stack: Unity 6000.0.55f1, existing Burst/Collections/Jobs dependencies, C# core tests, GPU probes and live camera captures. No repository present; direct workspace changes are authorized.

- [x] Navigation: first add off-centre wheel / viewport-centre regression and zero/small drag after return regression in LocalNavigationRegression. Run failures; fix FlatScrollNavigator capture order, chart frame alignment and quaternion drag; rerun zoom reversal and polar/date-line tests.
- [x] Rivers: replace the old extraction-threshold width floor in FlatTerrainAnalysis with a bounded monotonic discharge law; test headwater < confluence < mouth and maximum width. Modify FlatWater's shared-face channel curves to add bounded meanders with identical shared-face endpoints and connected mouths. GPU test finite values and shared-face continuity; inspect default river images.
- [x] Workers: introduce tested versioned job results and persistent native buffers for costly tile data preparation, bounded worker counts for managed terrain stages, explicit dispose/cancel/completion ownership. Integrate the active FlatMapRenderer path, not an unused utility. Compare serial/parallel data and stale-result handling; measure preparation cost without promising an unmeasured FPS gain.
- [x] Final: Unity shader compilation, core conservation/save/determinism tests, existing zoom/drag regressions, worker lifetime tests and real runtime captures; update integration notes and report remaining limits honestly.

- [x] Additional user request: replace remaining high/legacy cloud morphology and map cloud cache with HP adaptation, retire UniStorm from Assets, reduce cloud altitude and validate bounds above both flat and native terrain.
- [x] Additional user request: polar sea ice and land snow, consistent across globe/local/atlas views, smooth geographic edge; GPU poles/temperate exclusion checks. Visual layer only, not a new ice simulation.
