"""Generate Earth with either pinned control's runtime and geographic input assets."""

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
CORE = "Assets/ORBIS/Runtime/FlatWorld/Core"


def export_baseline(output, width, seed, before_seasons=False):
    output = output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    dem = f"Assets/ORBIS/Resources/ORBIS/FlatWorld/Earth{width}.bytes"
    assets = [dem]
    revision = BEFORE_SEASONS if before_seasons else REVISION
    if before_seasons:
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
        flags = [] if before_seasons else ["-p:LegacyEarthDem=true"]
        subprocess.run(["dotnet", "run", "--project", str(project / "Export.csproj"), "-c", "Release",
                        *flags, "--", str(output), str(width), seed], cwd=stage, check=True)
    with output.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    print(f"{revision}, SHA-256 {digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--width", type=int, choices=(256, 512, 1024, 1536, 2048), default=256)
    parser.add_argument("--before-seasons", action="store_true", help="Use the immediate pre-seasonal Earth generation control.")
    parser.add_argument("--seed", default="ORBIS-2D-2026")
    args = parser.parse_args()
    if not args.before_seasons and args.width > 1024:
        parser.error("The historical control provides widths 256, 512 and 1024.")
    name = "before-seasons" if args.before_seasons else "baseline"
    output = args.output or REPO / f"ValidationOutput/EarthClimate/{name}.bin"
    export_baseline(output, args.width, args.seed, args.before_seasons)
