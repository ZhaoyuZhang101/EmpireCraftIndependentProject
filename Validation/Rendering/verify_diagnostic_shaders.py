"""Compile the actual diagnostic passes with D3D11 and inspect the bound resources.

Run on Windows; an optional first argument overrides the Unity CGIncludes directory.
"""
import ctypes
import re
import struct
import time

from verify_d3d_shaders import ROOT, blob_bytes, compiler, expand, release


SURFACES = ("FlatWorld/FlatMap.shader", "FlatWorld/LocalSurface.shader", "FlatWorld/ScrollSurface.shader", "Surface.shader")
FLOW_TEXTURES = {"_GlebaWindFlowTex", "_GlebaCurrentFlowTex", "_GlebaBandsTex", "_VisualTex"}
NATURAL_TEXTURES = {"_HydroA", "_HydroB", "_SkyTex", "_VegetationSeasonTex", "_RiverAtlas", "_EarthMarineDepth"}


def bindings(binary):
    chunks = struct.unpack_from("<I", binary, 28)[0]
    offsets = struct.unpack_from(f"<{chunks}I", binary, 32)
    start = 8 + next(offset for offset in offsets if binary[offset:offset + 4] == b"RDEF")
    count, table = struct.unpack_from("<II", binary, start + 8)
    resources = set()
    slots = 0
    for index in range(count):
        name, kind, _, _, _, register, size, _ = struct.unpack_from("<8I", binary, start + table + 32 * index)
        begin = start + name
        resources.add(binary[begin:binary.index(b"\0", begin)].decode())
        if kind == 3:  # D3D_SIT_SAMPLER
            slots = max(slots, register + size)
    return resources, slots


def main():
    for filename in SURFACES:
        path = ROOT / "Assets/ORBIS/Resources/ORBIS" / filename
        passes = re.findall(r"CGPROGRAM(.*?)ENDCG", path.read_text(encoding="utf-8-sig"), re.S)
        source = next(p for p in passes if "#pragma fragment frag" in p)
        if not re.search(r"^ *#pragma multi_compile_local_fragment [^\n]*\bORBIS_DIAGNOSTIC\b", source, re.M):
            raise AssertionError(f"{filename} cannot select the diagnostic variant")
        source = expand(source, path.parent)
        for gamma in (False, True):
            defines = ["SHADER_API_D3D11 1", "SHADER_TARGET 50", "UNITY_COMPILER_HLSL 1",
                       "UNITY_VERSION 600000", "UNITY_PASS_FORWARDBASE 1", "ORBIS_DIAGNOSTIC 1"]
            if gamma:
                defines.append("UNITY_COLORSPACE_GAMMA 1")
            data = ("".join("#define " + define + "\n" for define in defines) + source).encode()
            shader, errors = ctypes.c_void_p(), ctypes.c_void_p()
            try:
                start = time.monotonic()
                status = compiler.D3DCompile(data, len(data), filename.encode(), None, None, b"frag", b"ps_5_0",
                                             0x1000, 0, ctypes.byref(shader), ctypes.byref(errors))
                if status < 0:
                    raise RuntimeError(blob_bytes(errors).decode(errors="replace"))
                resources, slots = bindings(blob_bytes(shader))
                assert FLOW_TEXTURES <= resources, f"Missing flow textures: {FLOW_TEXTURES - resources}"
                assert not (resources & NATURAL_TEXTURES), f"Natural-only textures in chart: {resources & NATURAL_TEXTURES}"
                assert slots <= 16, f"D3D11 pixel sampler limit exceeded: {slots}"
                print(f"PASS {filename}, {'gamma' if gamma else 'linear'}, {slots} samplers, {time.monotonic()-start:.1f}s")
            finally:
                release(shader)
                release(errors)


if __name__ == "__main__":
    main()
