"""Import only the supplied SW3 water artwork used by the Built-in adapter."""
from pathlib import Path
import tarfile

root = Path(__file__).resolve().parents[1]
package = Path('D:/BaiduNetdiskDownload/Stylized Water 3 v3.0.1.unitypackage')
selected = {'SmoothWaves.png', 'SharpWaves.png', 'OceanFoam.png', 'DirectionalFoam.png'}
target = root / 'Assets/ThirdParty/Resources/ORBISVendor/SW3'
target.mkdir(parents=True, exist_ok=True)
with tarfile.open(package) as archive:
    for member in archive:
        if not member.name.endswith('/pathname'):
            continue
        source = archive.extractfile(member).read().decode('utf-8').splitlines()[0]
        name = source.rsplit('/', 1)[-1]
        if name not in selected:
            continue
        key = member.name.rsplit('/', 1)[0]
        for entry, suffix in [('asset', ''), ('asset.meta', '.meta')]:
            destination = target / (name + suffix)
            if not destination.exists():
                destination.write_bytes(archive.extractfile(key + '/' + entry).read())
        print(source + ' -> ' + str(target / name))
