"""Rasterize the production animal artwork through native D3D11, without Unity."""
import ctypes
from pathlib import Path
import math
import struct
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "ValidationOutput/wildlife-actors"
SIDE = 32
CASES = 10
PIXELS = 4 * CASES * SIDE * SIDE
VOID = ctypes.c_void_p
UINT = ctypes.c_uint
HRESULT = ctypes.c_long


def method(pointer, index, returns, *arguments):
    table = ctypes.cast(pointer, ctypes.POINTER(ctypes.POINTER(VOID)))[0]
    return ctypes.WINFUNCTYPE(returns, VOID, *arguments)(table[index])


def check(result, operation):
    if result < 0:
        raise RuntimeError(f"{operation} failed with HRESULT 0x{result & 0xffffffff:08x}")


def release(pointer):
    if pointer:
        method(pointer, 2, UINT)(pointer)


def blob_data(pointer):
    address = method(pointer, 3, VOID)(pointer)
    size = method(pointer, 4, ctypes.c_size_t)(pointer)
    return ctypes.string_at(address, size)


class BufferDescription(ctypes.Structure):
    _fields_ = [(name, UINT) for name in ("size", "usage", "bind", "cpu", "misc", "stride")]


class ViewDescription(ctypes.Structure):
    _fields_ = [(name, UINT) for name in ("format", "dimension", "first", "count", "flags")]


class MappedResource(ctypes.Structure):
    _fields_ = [("data", VOID), ("row_pitch", UINT), ("depth_pitch", UINT)]


def render():
    art = (ROOT / "Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatWildlifeArt.cginc").read_text(encoding="utf-8-sig")
    source = """
float PDBox(float2 p,float2 lo,float2 hi)
{return step(lo.x,p.x)*step(lo.y,p.y)*(1-step(hi.x,p.x))*(1-step(hi.y,p.y));}
""" + art + f"""
RWStructuredBuffer<float4> output : register(u0);
[numthreads(64,1,1)] void Probe(uint3 thread : SV_DispatchThreadID)
{{
 uint index=thread.x;if(index>={PIXELS})return;
 uint sprite=index/{SIDE * SIDE},sample=sprite%{CASES};
 float species=1+sprite/{CASES};
 float clocks[{CASES}]={{.5,2.2,.5,.8,.5,1.0,.5,2.2,.5,.5}};
 float behaviours[{CASES}]={{0,0,1,1,3,3,5,5,5,0}};
 float speed=sample==2||sample==3?.2:0;
 float2 p=float2(index%{SIDE},{SIDE - 1}-(index/{SIDE})%{SIDE})-{SIDE / 2};
 float4 actor=float4(species,1,clocks[sample],.31);
 float4 state=float4(behaviours[sample],speed,1,.4);
 float2 environment=sample==8?float2(0,-6):float2(1,15);
 output[index]=PDWildlifeActorArt(p,actor,state,environment);
}}
"""
    compiler = ctypes.WinDLL("d3dcompiler_47.dll")
    compiler.D3DCompile.argtypes = [VOID, ctypes.c_size_t, ctypes.c_char_p, VOID, VOID,
                                  ctypes.c_char_p, ctypes.c_char_p, UINT, UINT,
                                  ctypes.POINTER(VOID), ctypes.POINTER(VOID)]
    compiler.D3DCompile.restype = HRESULT
    bytecode, errors = VOID(), VOID()
    device, context, shader, output, view, staging = (VOID() for _ in range(6))
    data = source.encode()
    try:
        status = compiler.D3DCompile(data, len(data), b"WildlifeArt", None, None,
                                     b"Probe", b"cs_5_0", 0x1000, 0,
                                     ctypes.byref(bytecode), ctypes.byref(errors))
        if status < 0:
            raise RuntimeError(blob_data(errors).decode(errors="replace"))
        d3d = ctypes.WinDLL("d3d11.dll")
        d3d.D3D11CreateDevice.argtypes = [VOID, UINT, VOID, UINT, VOID, UINT, UINT,
                                        ctypes.POINTER(VOID), ctypes.POINTER(UINT), ctypes.POINTER(VOID)]
        d3d.D3D11CreateDevice.restype = HRESULT
        level = UINT()
        status = d3d.D3D11CreateDevice(None, 1, None, 0, None, 0, 7,
                                      ctypes.byref(device), ctypes.byref(level), ctypes.byref(context))
        if status < 0:
            status = d3d.D3D11CreateDevice(None, 5, None, 0, None, 0, 7,
                                          ctypes.byref(device), ctypes.byref(level), ctypes.byref(context))
        check(status, "D3D11 device")
        binary = blob_data(bytecode)
        check(method(device, 18, HRESULT, VOID, ctypes.c_size_t, VOID, ctypes.POINTER(VOID))(
            device, binary, len(binary), None, ctypes.byref(shader)), "compute shader")
        description = BufferDescription(PIXELS * 16, 0, 0x80, 0, 0x40, 16)
        check(method(device, 3, HRESULT, ctypes.POINTER(BufferDescription), VOID, ctypes.POINTER(VOID))(
            device, ctypes.byref(description), None, ctypes.byref(output)), "output buffer")
        view_description = ViewDescription(0, 1, 0, PIXELS, 0)
        check(method(device, 8, HRESULT, VOID, ctypes.POINTER(ViewDescription), ctypes.POINTER(VOID))(
            device, output, ctypes.byref(view_description), ctypes.byref(view)), "output view")
        description = BufferDescription(PIXELS * 16, 3, 0, 0x20000, 0, 0)
        check(method(device, 3, HRESULT, ctypes.POINTER(BufferDescription), VOID, ctypes.POINTER(VOID))(
            device, ctypes.byref(description), None, ctypes.byref(staging)), "readback buffer")
        method(context, 69, None, VOID, VOID, UINT)(context, shader, None, 0)
        method(context, 68, None, UINT, UINT, ctypes.POINTER(VOID), VOID)(context, 0, 1, ctypes.byref(view), None)
        method(context, 41, None, UINT, UINT, UINT)(context, (PIXELS + 63) // 64, 1, 1)
        method(context, 47, None, VOID, VOID)(context, staging, output)
        mapped = MappedResource()
        check(method(context, 14, HRESULT, VOID, UINT, UINT, UINT, ctypes.POINTER(MappedResource))(
            context, staging, 0, 1, 0, ctypes.byref(mapped)), "GPU readback")
        try:
            result = struct.unpack(f"{PIXELS * 4}f", ctypes.string_at(mapped.data, PIXELS * 16))
        finally:
            method(context, 15, None, VOID, UINT)(context, staging, 0)
        return [result[start:start + SIDE * SIDE * 4] for start in range(0, len(result), SIDE * SIDE * 4)]
    finally:
        for item in (view, staging, output, shader, context, device, errors, bytecode):
            release(item)


def difference(first, second):
    return sum(abs(a - b) for a, b in zip(first, second)) / len(first)


def visible(pixels):
    return sum(pixels[index] > .5 for index in range(3, len(pixels), 4))


def alpha_difference(first, second):
    return sum(abs(first[index] - second[index]) for index in range(3, len(first), 4))


def main():
    sprites = render()
    labels = ("Idle", "Idle later", "Travel", "Travel later", "Eating", "Eating later", "Sleeping", "Breathing", "Cold rest")
    species_names = ("Rabbit", "Deer", "Bird", "Butterfly")
    board = Image.new("RGB", (len(labels) * 128 + 80, 4 * 150 + 30), (32, 40, 36))
    draw = ImageDraw.Draw(board)
    for index, label in enumerate(labels):
        draw.text((85 + index * 128, 8), label, fill=(225, 225, 207))
    checks = 0
    for species in range(4):
        frames = sprites[species * CASES:(species + 1) * CASES]
        assert all(math.isfinite(value) and 0 <= value <= 1 for frame in frames for value in frame), "nonfinite or unbounded art"
        assert difference(frames[0], frames[9]) == 0, "same paused clock changes pixels"
        assert difference(frames[0], frames[1]) > .00001, "idle animal lacks small living motions"
        assert difference(frames[2], frames[3]) > .00001, "travel gait does not animate"
        assert alpha_difference(frames[2], frames[3]) > 0, "travel only changes colour without moving body parts"
        assert difference(frames[4], frames[5]) > .00001, "feeding pose does not animate"
        assert alpha_difference(frames[4], frames[5]) > 0, "feeding lacks articulated movement"
        assert difference(frames[6], frames[7]) > .000001, "sleeping animal lacks breathing"
        assert alpha_difference(frames[6], frames[7]) > 0, "sleeping body does not breathe"
        assert visible(frames[6]) < visible(frames[2]), "sleep silhouette is not compact"
        colours = {tuple(round(frame[index + c] * 255) for c in range(3))
                   for frame in frames[:4] for index in range(0, len(frame), 4) if frame[index + 3] > .5}
        assert len(colours) >= 8, "animal lacks layered pixel shading"
        assert 20 < visible(frames[2]) < SIDE * SIDE / 2, "animal silhouette exceeds sprite bounds"
        checks += 12
        draw.text((6, 33 + species * 150), species_names[species], fill=(225, 225, 207))
        for case, pixels in enumerate(frames[:9]):
            rgba = bytes(round(min(1, max(0, channel)) * 255) for channel in pixels)
            sprite = Image.frombytes("RGBA", (SIDE, SIDE), rgba).resize((128, 128), Image.Resampling.NEAREST)
            cell = Image.new("RGBA", (128, 128), (78, 93, 74, 255))
            cell.alpha_composite(sprite)
            board.paste(cell.convert("RGB"), (80 + case * 128, 30 + species * 150))
        print(f"PASS {species_names[species]} native GPU poses, paused equality, pixel shading, gait and compact rest")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    board.save(OUTPUT / "artwork-contact.png")
    print(f"PASS {checks} native D3D11 animal-art checks; preview={OUTPUT / 'artwork-contact.png'}")


if __name__ == "__main__":
    main()
