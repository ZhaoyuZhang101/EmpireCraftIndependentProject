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
