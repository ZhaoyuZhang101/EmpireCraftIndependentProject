# Third-party references and notices

## 1. HanPi Volume Cloud / AshenOneArt

Project: https://github.com/AshenOneArt/HPVolumeCloud
Derivation: https://github.com/AshenOneArt/HPVolumeCloud/blob/main/Docs/PhiFwd_FromRTE.md
Upstream license: https://github.com/AshenOneArt/HPVolumeCloud/blob/main/LICENSE

Copyright (c) 2026 AshenOneArt.

ORBIS V2's cloud shader uses an independently written, reduced approximation inspired by the isotropic diffuse-field ideas documented by **HanPi Volume Cloud / AshenOneArt**. It does not contain the complete HDRP implementation or claim numerical equivalence with it. This visible credit and project link are retained in accordance with the upstream attribution request.

The upstream LICENSE contains MIT terms plus an additional visible-attribution requirement identifying “HanPi Volume Cloud” / AshenOneArt and its project link. Retain that attribution and link when redistributing this integration; inspect the linked upstream LICENSE when taking additional upstream code. The MIT text below is retained for the referenced work.

## 2. afl_ext — Very fast procedural ocean / MdXyzX

Original: https://www.shadertoy.com/view/MdXyzX
Accessible code reference: https://gist.github.com/rngtm/c91cfd99a70062ee5732b6078b4488ba

The accessible code reference identifies the original Shadertoy ID, **afl_ext 2017–2023**, and **MIT License**. The original Shadertoy page itself could not be retrieved in this environment; the attribution/license evidence is the header of that public code reference, not a claim to have downloaded the latest original page.

The exponential-sine wave family and derivative-based domain warp were adapted for ORBIS. Changes include a 3D spherical domain, a local analytic warp Jacobian, individually bounded CPU wave phases, depth proxies, downstream river directions, and the existing ORBIS terrain renderer. The chocolate color palette, full-screen camera, planar ray marcher, and sky implementation of the reference are not imported.

Copyright (c) 2017–2023 afl_ext. MIT License.

## MIT terms retained for the referenced works

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

## 3. Other references, not imported code packages

Garrett Gunnell's Water repository, https://github.com/GarrettGunnell/Water,
was consulted as an independent Unity water-rendering reference. Its entire
project, FFT implementation, assets, and libraries are not bundled here.

TDM / Alexander Alekseev's Seascape source explicitly lists CC BY-NC-SA 3.0:
https://github.com/tdmaav/shadertoy/blob/master/Seascape.shader . Its source was
not selected for this integration; do not assume that another MIT-labeled
port overrides the original author's license.

## 4. User-supplied project

The source demo, terrain atlas, and the two design documents were supplied
by the user. These notices do not relicense the user's existing project or
assets, and do not assert third-party approval of this integration.

Checked: 2026-09-11. Before shipping additional upstream code, pin the exact
upstream revision and retain its then-applicable notices.


## 5. V3 atmosphere — Dimev / Dimas Leenman

Reference: https://github.com/Dimev/atmospheric-scattering-explained
Code and license header: https://github.com/Dimev/atmospheric-scattering-explained/blob/main/atmosphere.glsl

Copyright (c) 2019 Dimas Leenman. MIT License; the MIT permission and disclaimer reproduced above are retained for this reference as well. The copyright and MIT header were visible in the source file. A separate fetch of the LICENCE page failed; no claim is made to have pinned the current repository revision.

V3 independently implements the explained Rayleigh/Mie/absorption model in HLSL, with game-scaled layer heights, scene-depth clipping and ordered cloud/air interval integration. It does not bundle the complete GLSL scene, original camera, or original demonstration objects. Attribution is retained even though the integration code is independently written.

## 6. User-mentioned Shadertoy references — NOT imported

https://www.shadertoy.com/view/MdGBWG
https://www.shadertoy.com/view/lslXDr

Attempts on 2026-09-11 encountered robots access restrictions and/or HTTP 402. Complete source, authorship and applicable licenses were not retrieved. These are unverified user references, not imported code. No inference is made that they share the license of Dimev or the different V2 water reference MdXyzX. OrbisTransportView.cs and Transport.shader are newly written model-driven diagnostic tracers, not a claimed port of either linked shader.

## 7. V3 retained input resources and fonts

The incoming terrain atlas, original scene and browser reference are retained as user-provided resources. No new worldwide redistribution license is asserted for them. Both source archives remain subject to their existing rights and notices.

Font binaries are excluded from this delivery. OrbisSystemFont attempts to use fonts installed on the user's own operating system. Historical font-license text is retained solely as input provenance, not as a promise that the corresponding font is distributed.

The historical V2 notices above were carried forward from the user-provided V2 package. This integration does not independently certify every historical third-party license claim or grant permission for commercial publication of the complete project.


## 8. V4.1 shared optical-depth cache, atlas columns and external noise bridge

Checked: 2026-09-11. New Built-in code is independently implemented, not a copy of the complete HDRP render graph or cloud shadow compute shader.

Unity's primary documentation describes volumetric clouds using a Beer shadow map for cloud self-shadowing and ground shadows:
https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/HDRP-Features.md

V4.1 extends the local implementation with eight near-cloud optical-depth layers and samples cloud visibility inside the air-scattering source. Exceptional far-side shell intervals use direct tracing. This is not numerical or architectural equivalence to Unity HDRP. No additional HDRP package code is redistributed in this update.

HanPi Volume Cloud / AshenOneArt: https://github.com/AshenOneArt/HPVolumeCloud
The existing reduced isotropic-source approximation is retained and adjusted with thin-density gating and separate indirect sky fill. The full phi_fwd implementation is NOT ported. Upstream acknowledges HDRP-derived portions and its visible-attribution requirement; do not discard its licenses when importing additional upstream code.

3DTextureExportTools / AshenOneArt: https://github.com/AshenOneArt/3DTextureExportTools
The PNG-slice bridge is new local importer code. It does not bundle the upstream generator, noise assets, DDS libraries or DirectXTex executables. The author's README specifies testing on Unity 2022.3.62f1 / DX11 / HDRP; this is not evidence of compatibility with Unity 6, Built-in, or other APIs. Generated third-party assets remain subject to their own applicable rights.

Additional user reference https://www.shadertoy.com/view/wlBXWK was also not retrieved as verifiable complete source. None of lslXDr, wlBXWK or MdGBWG is represented as imported or licensed source in V4.1.
Zhihu https://zhuanlan.zhihu.com/p/2057055806038844943 was not successfully read in this environment. The cloud discussion is based on the accessible author repository instead.

Unity API references consulted for output and validation behavior:
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GL-sRGBWrite.html
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderUtil.GetShaderMessages.html
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture3D.html


## 9. V4.1.1 reference audit and loop hotfix

Sean O'Neil, GPU Gems 2 Chapter 16, NVIDIA: https://developer.nvidia.com/gpugems/gpugems2/part-ii-shading-lighting-and-shadows/chapter-16-accurate-atmospheric-scattering
The chapter was consulted for atmosphere geometry, scale assumptions and composition. Its Cg listings, fast fitted scale function and assets are NOT copied or relicensed here.

The user's uploaded Dimas Leenman `atmosphere.glsl` is retained unmodified at `Reference/atmosphere.glsl`, outside Unity Assets, with its complete MIT header. It is a reference copy, not a claim that Unity executes GLSL directly.

The loop refactor, scalar/vector cut sorting, bounded counts, patch installer and native compile-report utility are newly written local changes. Existing notices remain in force.


## V4.1.2 Active adaptation of user-uploaded atmosphere.glsl

Copyright (c) 2019 Dimas Leenman, MIT License. The full original source is retained at `Reference/atmosphere.glsl` (SHA-256 `708ea4a9141a7653ea0085d7f26ae09ca09bb14c88e339d63da74e16439300bd`). The original license is retained in the active adaptation `Assets/ORBIS/Resources/ORBIS/OrbisAtmosphereGLSL.cginc` and the standalone `Preview/AtmosphereTransit.html`. Numerical and implementation changes are documented in `Documentation/GLSL-Port-V4_1_2.md`; this is not a pixel-identical port or a transfer of all other reference licenses.

NVIDIA GPU Gems is used as a conceptual reference, not copied as a complete shader or declared MIT. Preview terrain and clouds are simplified demonstration code; generated static optical preview images are not Unity screenshots. No font binaries are redistributed.


## V4-2 repair: additional provenance

Active atmospheric formulas are adapted from GLtracy code supplied by the user; credit retained in Reference/GLtracy_User_V4_2.glsl. No license was supplied for that source, and no license is inferred. The old Dimas reference is preserved as historical material and is not the active optical include.

HPWater, HPVolumeCloud and 3DTextureExportTools are referenced for concepts/data semantics as documented in Documentation/V4_2_Repair.md. This package does not redistribute their complete HDRP pipelines or external DDS executable toolchains. The reduced Built-in optical routines and the supplied LUT/noise baker are independent adaptations, not a claim of upstream feature parity. Existing third-party notices above continue to apply to inherited files.
