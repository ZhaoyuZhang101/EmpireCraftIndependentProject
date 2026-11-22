"""Generate Earth with a pinned control's runtime and geographic input assets."""

import argparse
import hashlib
import io
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile


HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
REVISION = "fcb360fd872b6a7621868006ac72e1cd00b0ee37"
BEFORE_SEASONS = "06bfd500e642b5242ca5ccbeb3bc85eb7b54efec"
BEFORE_MONSOON = "1bee7ae203eb765ab118af11031392b88fa45cd2"
BEFORE_CIRCULATION = "2d05a937549fbc4dab04ab245e14527cd1df6e99"
BEFORE_BOHAI = "e560df56e44d9a43f61eba3fa68d73281de0097e"
BEFORE_SURFACE_CLIMATE = "4aa93fc340c6028f934ae78d43ec38176a379d57"
BEFORE_SUBCLOUD = "8d311abf90eebd9ee603d1725c15ce7a116031bb"
CORE = "Assets/ORBIS/Runtime/FlatWorld/Core"


def export_baseline(output, width, seed, revision=REVISION):
    output = output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    dem = f"Assets/ORBIS/Resources/ORBIS/FlatWorld/Earth{width}.bytes"
    assets = [dem]
    geographic_water = revision != REVISION
    if geographic_water:
        assets += [dem.replace("Earth", "EarthWater"), str(Path(dem).with_name("EarthRivers.bytes").as_posix())]
    archive = subprocess.check_output(["git", "archive", "--format=zip", revision, CORE, *assets], cwd=REPO)
    with tempfile.TemporaryDirectory(prefix="baseline-", dir=output.parent) as temp:
        stage = Path(temp)
        with zipfile.ZipFile(io.BytesIO(archive)) as source:
            for name in source.namelist():
                if name in assets or (name.startswith(CORE + "/") and name.endswith(".cs")):
                    target = stage / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(source.read(name))
        project = stage / "Validation/EarthClimate"
        project.mkdir(parents=True)
        for filename in ("Export.cs", "Export.csproj", "CompareKoppen.cs"):
            shutil.copyfile(HERE / filename, project / filename)
        flags = [] if geographic_water else ["-p:LegacyEarthDem=true"]
        subprocess.run(["dotnet", "run", "--project", str(project / "Export.csproj"), "-c", "Release",
                        *flags, "--", str(output), str(width), seed], cwd=stage, check=True)
    with output.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    print(f"{revision}, SHA-256 {digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--width", type=int, choices=(256, 512, 1024, 1536, 2048, 4096), default=256)
    control = parser.add_mutually_exclusive_group()
    control.add_argument("--before-seasons", dest="revision", action="store_const", const=BEFORE_SEASONS, help="Use the immediate pre-seasonal Earth generation control.")
    control.add_argument("--before-monsoon", dest="revision", action="store_const", const=BEFORE_MONSOON, help="Use the six-width control from before the monsoon circulation repair.")
    control.add_argument("--before-circulation", dest="revision", action="store_const", const=BEFORE_CIRCULATION, help="Use the player model before seasonal wind belts and wind-driven ocean heat transport.")
    control.add_argument("--before-bohai", dest="revision", action="store_const", const=BEFORE_BOHAI, help="Use the player model before the poleward coastal monsoon repair.")
    control.add_argument("--before-surface-climate", dest="revision", action="store_const", const=BEFORE_SURFACE_CLIMATE, help="Use the control after the Bohai repair, before surface climate feedback.")
    control.add_argument("--before-subcloud", dest="revision", action="store_const", const=BEFORE_SUBCLOUD, help="Use the player model before subcloud evaporation and seasonal snow storage.")
    parser.set_defaults(revision=REVISION)
    parser.add_argument("--seed", default="ORBIS-2D-2026")
    args = parser.parse_args()
    if args.revision == REVISION and args.width > 1024:
        parser.error("The historical control provides widths 256, 512 and 1024.")
    if args.revision == BEFORE_SEASONS and args.width > 2048:
        parser.error("The pre-seasonal control provides widths up to 2048.")
    name = {REVISION: "baseline", BEFORE_SEASONS: "before-seasons", BEFORE_MONSOON: "before-monsoon",
            BEFORE_CIRCULATION: "before-circulation", BEFORE_BOHAI: "before-bohai",
            BEFORE_SURFACE_CLIMATE: "before-surface-climate", BEFORE_SUBCLOUD: "before-subcloud"}[args.revision]
    output = args.output or REPO / f"ValidationOutput/EarthClimate/{name}.bin"
    export_baseline(output, args.width, args.seed, args.revision)
