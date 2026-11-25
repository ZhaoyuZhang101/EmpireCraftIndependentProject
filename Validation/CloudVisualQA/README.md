# ORBIS Climate-Cloud Visual QA (Paired Physical Views & `79e6fa2` Visual Baseline)

Independent Unity Editor visual acceptance suite and CPU-only lightweight verification harness for climate-driven clouds in ORBIS.

- **Conflict-free parallel isolation**: All new code lives exclusively under `Assets/ORBIS/Editor/CloudVisualQA/` and `Validation/CloudVisualQA/`. Zero edits to shared regressions (`ClimateCloudRegression.cs`, `HPCloudHeightRegression.cs`, `WaterCloudRegression.cs`, `LightningRegression.cs`, `CloudRenderBenchmark.cs`), runtime scripts, or production shaders.
- **Reuses existing probes**: Invokes the existing 4 shared GPU regressions in `full` mode and compares live full-disk captures directly against the existing `79e6fa2` left-column PNG baselines in `Documentation/CloudEarth80Showcase/NasaUpgrade3Col/`.

---

## 1. Entry Points

### A. Unity Real-Machine Acceptance (for serial scheduling in the integration worktree)

**Unity Editor Menu:**
- `Tools/ORBIS/Cloud Visual QA/Run Full Paired & 79e6fa2 Baseline Acceptance`
- `Tools/ORBIS/Cloud Visual QA/Run Paired Climate-Cloud Views Only`
- `Tools/ORBIS/Cloud Visual QA/Run 79e6fa2 Visual Baseline Audit Only`

**Unity Batch CLI (`-batchmode -force-d3d11`):**
```powershell
# Full acceptance (reuses shared GPU regressions + 7 paired views + 5 full-disk 79e6fa2 baseline views)
Unity.exe -batchmode -force-d3d11 -projectPath <repo> `
  -executeMethod Orbis.Editor.CloudVisualQA.ClimateCloudVisualQA.RunBatch `
  -orbisCloudQaOut ValidationOutput/CloudVisualQA `
  -orbisCloudQaMode full `
  -orbisCloudQaQuality 2 `
  -orbisCloudQaEarthWidth 512 `
  -orbisCloudQaRunSharedRegressions true

# Paired climate-cloud views only
Unity.exe -batchmode -force-d3d11 -projectPath <repo> `
  -executeMethod Orbis.Editor.CloudVisualQA.ClimateCloudVisualQA.RunPairsBatch `
  -orbisCloudQaOut ValidationOutput/CloudVisualQA_Pairs `
  -orbisCloudQaQuality 2 -orbisCloudQaEarthWidth 512

# 79e6fa2 visual baseline audit only
Unity.exe -batchmode -force-d3d11 -projectPath <repo> `
  -executeMethod Orbis.Editor.CloudVisualQA.ClimateCloudVisualQA.RunBaselineBatch `
  -orbisCloudQaOut ValidationOutput/CloudVisualQA_Baseline `
  -orbisCloudQaQuality 2 -orbisCloudQaEarthWidth 512
```

**Artifacts produced per run (`<outDir>/`):**
- `cloud-visual-qa-report.md` & `cloud-visual-qa-report.json` (single-run bound forcing, `FlatSkyRenderer.Texture`, `FlatSkyRenderer.ColumnTexture`, rendered screenshot metrics, anti-decorative-noise verdicts, and `79e6fa2` visual baseline verdicts)
- `pairs/<scenario>_sheet.png` (`[State A | State B | Signed Diff Heatmap (Red=+B, Blue=-B)]`) plus `_A.png` / `_B.png`
- `baseline_79e6fa2/<view>_vs_79e6fa2.png` (`[79e6fa2 Left-Col | Candidate Full-Disk | Clear/Thin-Veil/Thick Diagnostic Map]`) plus `_candidate.png`
- `status.txt` (`PASS` / `FAIL`, exit code `0` / `1`)

### B. Standalone CPU-Only Lightweight Verification (Zero Unity / Zero GPU Load)

Safe to run concurrently while another session is capturing Unity GPU frame timings:

```powershell
# 1. Roslyn compile-check of all Unity Editor QA scripts (Assets/ORBIS/Editor/CloudVisualQA/*.cs)
dotnet build Validation/CloudVisualQA/EditorCompileCheck.csproj -c Release -nologo

# 2. Pure-C# Orbis.Flat.Core paired forcing + natural cyclone + anti-decorative-noise + 79e6fa2 PNG audit
dotnet run --project Validation/CloudVisualQA/CloudVisualQA.Lightweight.csproj -c Release -nologo
```

---

## 2. Controlled Paired Views & End-to-End Coverage Matrix

Every pair locks **camera pose/FOV/distance**, **planet-local sun direction**, **exposure (`1.0`)**, **noise seed (`ORBIS-2D-2026`)**, and **visual clock (`24.0`, non-zero so advection & dual-phase LIC remain active yet frozen)**, reads directly from production `FlatSkyRenderer.Texture` + `FlatSkyRenderer.ColumnTexture`, and binds cloud-column metrics to the rendered game-camera screenshot.

| Scenario ID | Single Physical Input / Config Changed | Primary Verification Target | E2E Status on `aaa9625` | Explicit Coverage / Gap Notes |
| :--- | :--- | :--- | :---: | :--- |
| `00_lock_control` | None (`A == B`) | Zero drift in `Texture`, `ColumnTexture`, and rendered pixels | `FullEndToEnd` | Confirms locked camera, sun, exposure, noise seed, and `FlatSkyRenderer` visual clock. |
| `01_moist_warm_vs_dry_stable` | Moisture & convective instability (RH, Rain, T, SLP, CAPE/CIN) in Indo-Pacific ROI | Cloud cover (`Texture.r/g`, `Column.r/g`), convective cloud-top metres (`OrbisCloudVerticalSpan(weather.b)`), rain column, screenshot ROI luma | `FullEndToEnd` | Flows end-to-end through `FlatSkyRenderer` (`_ClimateTex`, `_CloudSynopticTex`, `_RegionalCloudResponse` -> `FlatSkyBake` -> `HPCloudColumn` -> volume cloud). |
| `02_flow_reversal_orographic_andes` | Surface wind flow vector (`Moisture.East`: `+0.95` westerly vs `-0.95` easterly) across Andes | Windward/leeward cloud dipole flip across mountain crest + zero control-ROI leakage | `PartialWithExplicitNote` | **Wired in `aaa9625`**: wind-slope `orogCrest` (`aux.g`), dynamic `leeDownslope` in `leeShadow` (`aux.b`), `_ClimateWindTex` advection/LIC combing, `_ScrollCloudWindTex` drift. **Gaps in `aaa9625`**: (1) `Gleba.RainShadowiness` and `WorldData.Precipitation` stay pre-baked unless `FlatMoisture` re-solves; (2) `BuildCloudSynopticFields` line 3298 overwrites `windConv`-coupled `synopticLift`; (3) `_UpperWindTex` (`GlebaRossbyWaveField`) ignores `Moisture.East/North`. |
| `03_sst_cold_vs_warm_marine_sc` | Ocean SST anomaly (`-2.6 °C` cold upwelling vs `+2.6 °C` warm) in Peru/Humboldt box | Marine stratocumulus deck (`aux.r` -> `scProtect`/`scDeckDirect`) and low-altitude inversion tier (`weather.b <= 0.18` -> 3D Voronoi Sc) | `PartialWithExplicitNote` | **Wired in `aaa9625`**: `OceanCurrents.SeaAnomaly` drives `marineSc` (`0.59 -> 0.14`) and low-tier Sc end-to-end through `FlatSkyRenderer`. **Boundary in `aaa9625`**: `FlatMoistureState.OceanCurrents` is `internal` and `coolUpwelling` reads `SeaAnomaly` rather than `WorldData.Temperature` alone. |
| `04a_season_jan_vs_jul_itcz` | Calendar season (`Day 15` Jan vs `Day 195` Jul) on unmodified real Earth (Africa–Indian Ocean) | Northward migration of ITCZ/monsoon cloud & column centroid (`+21.7°` in core forcing) at locked equatorial sun | `FullEndToEnd` | 100% real Earth normals + `FlatRegionalCloudField`, zero synthetic edits, zero lighting confound. |
| `04b_rotation_1x_vs_045x_doublecell` | `PlanetClimate.RotationRateRatio` (`1.0×` ThreeCell vs `0.45×` DoubleCell) via full `FlatGenerator.Rebuild` | Hadley terminus (`30° -> 42°`), Rossby wavenumber (`2.6 -> 1.6`), poleward expansion of tropical convective belt (`\|lat\| 8.5° -> 10.8°`) | `PartialWithExplicitNote` | **Wired in `aaa9625`**: `FlatWindBelts`, `FlatClimate`, `FlatMoisture`, `ExoPlaSim`, `GetCloudCirculationParams`, and tropical Hadley/ITCZ expansion. **Gap in `aaa9625`**: `GlebaClimateSupplement.Build` does not scale `SthzNorth/SouthLatRad` or `PolarNorth/SouthLatRad` with `RotationRateRatio`, and `FlatSkyBake.shader` reads `polarLatRad` from `aux.a` rather than `_GlebaCirculationParams.y`. |
| `05_natural_cyclone_t0_vs_t1` | Natural `FlatSimulation` time-step (`t0 -> t1`) on the same tracked tropical cyclone on real Earth | Cloud peak follows tracked `(U, V)`, hemispheric logarithmic spiral chirality (`inward < outward`), signed azimuthal `Phase` rotation, far-field stability | `FullEndToEnd` | Dynamically locates the strongest natural Earth cyclone (zero synthetic storm injection); also runs a pure-phase diagnostic check to isolate rainband rotation from translation. |

### Anti-Decorative-Noise Detection
Each pair binds Layer 1 (physical forcing), Layer 2 (`FlatSkyRenderer.Texture` + `ColumnTexture`), and Layer 3 (ray-backprojected rendered screenshot pixels) in the same run and fails explicitly on:
- `DECORATIVE_NOISE_RENDER_ONLY`: screen pixels churn while `ColumnTexture` does not respond.
- `UNBOUND_SKY_OR_WRONG_IMAGE_SIGN`: `ColumnTexture` changes while rendered screen ROI stays flat or has wrong sign.
- `DECORATIVE_ZERO_MEAN_NOISE` / `UNCORRELATED_NOISE_CHANGE`: high pixel churn (`MeanAbsDelta`) with near-zero `DirectionalCoherence` or low `PearsonR(ΔForcing, ΔColumn)`.
- `GLOBAL_NOISE_LEAKAGE`: unperturbed control ROI changes at a high fraction of target ROI.
- `CLOCK_OR_SEED_DRIFT`: non-zero diff on the `00_lock_control` locked repeat.

---

## 3. `79e6fa2` Visual Baseline Calibration (`NasaUpgrade3Col` Left Column)

Evaluates live full-disk renders against the exact `760×760` left-column crops (`x=16..776, y=130..890`) of `Documentation/CloudEarth80Showcase/NasaUpgrade3Col/compare_*.png` on:
1. **Long cloud-band continuity** (`Top3MeanSpan`, `LargeBandShare`, `SpeckleCount`) across both the **Pacific** (`01`, `02`) and the **other hemisphere** (`03`, `04`),
2. **Thin-cloud transition** (`ThinVeilFraction`, `TransitionEdgeSharpness`),
3. **Natural clear sky** (`ClearSkyFraction`, `LargestClearBasinFraction`),
4. **Zoomed-out major storm readability** (`StormProminence`, `StormCoreMean` on `08_atlantic_hurricane_globe`).

Lightweight calibration verified on the checked-in `NasaUpgrade3Col` PNGs (`79e6fa2` left column vs `aaa9625` middle column):
- `79e6fa2` self-baseline: **5 / 5 PASS**
- `aaa9625` vs `79e6fa2`: **4 / 5 FLAGGED** (surfaces Pacific Apr/Jul over-clearing `+0.123` / `+0.178`, harder transition sharpness in Pacific Apr, higher speckle fragmentation in Africa/Southern Ocean `76 -> 114` and South Atlantic `98 -> 127`, and shortened South Atlantic long bands `0.737 -> 0.645`; hurricane readability passes on `08`, and a synthetic storm-erasure negative test fails as expected).
