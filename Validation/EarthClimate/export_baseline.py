"""Build the pre-calibration code and Earth DEM from the pinned Git object in a temporary directory."""

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
CORE = "Assets/ORBIS/Runtime/FlatWorld/Core"


def export_baseline(output, width, seed):
    output = output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    dem = f"Assets/ORBIS/Resources/ORBIS/FlatWorld/Earth{width}.bytes"
    archive = subprocess.check_output(["git", "archive", "--format=zip", REVISION, CORE, dem], cwd=REPO)
    with tempfile.TemporaryDirectory(prefix="baseline-", dir=output.parent) as temp:
        stage = Path(temp)
        with zipfile.ZipFile(io.BytesIO(archive)) as source:
            for name in source.namelist():
                if name == dem or (name.startswith(CORE + "/") and name.endswith(".cs")):
                    target = stage / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(source.read(name))
        project = stage / "Validation/EarthClimate"
        project.mkdir(parents=True)
        for filename in ("Export.cs", "Export.csproj"):
            shutil.copyfile(HERE / filename, project / filename)
        subprocess.run(["dotnet", "run", "--project", str(project / "Export.csproj"), "-c", "Release",
                        "-p:LegacyEarthDem=true", "--", str(output), str(width), seed], cwd=stage, check=True)
    with output.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    print(f"{REVISION}, SHA-256 {digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=REPO / "ValidationOutput/EarthClimate/baseline.bin")
    parser.add_argument("--width", type=int, choices=(256, 512, 1024), default=256)
    parser.add_argument("--seed", default="ORBIS-2D-2026")
    args = parser.parse_args()
    export_baseline(args.output, args.width, args.seed)
