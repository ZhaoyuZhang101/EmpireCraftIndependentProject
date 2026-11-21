"""Build a 100 m ground-sample grid or a self-contained ORBIS Earth viewer package."""
import argparse
import datetime
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import tempfile
from urllib.parse import urlparse

import numpy as np
from pyproj import Proj, CRS, Transformer
import rasterio
from rasterio.windows import Window
import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry


STEP = 100.0
BASE_PACKAGE = Path(__file__).resolve().parents[2] / "Examples" / "EarthScale100"
COP = "https://copernicus-dem-30m.s3.amazonaws.com"
ETOPO = "https://www.ngdc.noaa.gov/mgg/global/relief/ETOPO2022/data/15s/15s_surface_elev_gtif"
COP_LICENCE = "https://dataspace.copernicus.eu/sites/default/files/media/files/2025-06/copernicus_contributing_mission_data_access_v2_cop_dem_licenses.pdf"
NOTICE = ("produced using Copernicus WorldDEM-30 © DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 provided under "
          "COPERNICUS by the European Union and ESA; all rights reserved.")
DISCLAIMER = "The organisations in charge of the Copernicus programme by law or by delegation do not incur any liability for any use of the Copernicus WorldDEM-30."


def sha(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


class TileCache:
    def __init__(self, directory):
        self.directory = Path(directory)
        self.directory.mkdir(parents=True, exist_ok=True)
        self.sources = {}
        self.datasets = {}
        self.session = requests.Session()
        retry = Retry(total=3, backoff_factor=.8, status_forcelist=(429, 500, 502, 503, 504))
        self.session.mount("https://", HTTPAdapter(max_retries=retry))

    def close(self):
        for ds in self.datasets.values():
            if ds is not None:
                ds.close()
        self.session.close()

    def open(self, url, optional=False):
        if url in self.datasets:
            return self.datasets[url]
        path = self.directory / Path(urlparse(url).path).name
        if not path.exists():
            with self.session.get(url, timeout=(12, 120), stream=True) as response:
                if optional and response.status_code == 404:
                    self.datasets[url] = None
                    self.sources[url] = {"status": "not published (HTTP 404)"}
                    return None
                response.raise_for_status()
                with tempfile.NamedTemporaryFile(dir=self.directory, delete=False) as output:
                    tmp = Path(output.name)
                try:
                    with tmp.open("wb") as output:
                        for chunk in response.iter_content(1024 * 1024):
                            output.write(chunk)
                    expected = response.headers.get("Content-Length")
                    if expected and not response.headers.get("Content-Encoding") and int(expected) != tmp.stat().st_size:
                        raise IOError("Incomplete response for "+url)
                    with rasterio.open(tmp) as ds:
                        if ds.count != 1 or not ds.crs:
                            raise ValueError("Unexpected source grid "+url)
                    os.replace(tmp, path)
                finally:
                    tmp.unlink(missing_ok=True)
        ds = rasterio.open(path)
        self.datasets[url] = ds
        self.sources[url] = {"sha256": sha(path), "bytes": path.stat().st_size}
        return ds


def hemisphere(value, positive, negative, digits):
    return f"{positive if value >= 0 else negative}{abs(int(value)):0{digits}d}"


def url_for(product, north, west):
    east_code = hemisphere(west, "E", "W", 3)
    if product == "cop":
        south_code = hemisphere(north-1, "N", "S", 2)
        tile = f"Copernicus_DSM_COG_10_{south_code}_00_{east_code}_00_DEM"
        return f"{COP}/{tile}/{tile}.tif"
    name = "ETOPO_2022_v1_15s_"+hemisphere(north,"N","S",2)+east_code+"_surface.tif"
    return f"{ETOPO}/{name}"


def node_values(cache, product, north, west, x, y, width, height):
    src = cache.open(url_for(product, north, west), optional=(product == "cop"))
    if src is None:
        return np.full(len(x), np.nan)
    # Validate both native registration conventions before directly indexing the tile.
    lon, lat = src.xy(0, 0)
    dlon = 1 / width if product == "cop" else 15 / width
    first_lon = west if product == "cop" else west + dlon / 2
    first_lat = north if product == "cop" else north - (15 / height) / 2
    if (src.width, src.height) != (width, height) or abs(lon-first_lon)>1e-7 or abs(lat-first_lat)>1e-7:
        raise ValueError(f"Unexpected native size or registration for {src.name}")
    x0, x1, y0, y1 = int(x.min()), int(x.max()), int(y.min()), int(y.max())
    data = src.read(1, window=Window(x0,y0,x1-x0+1,y1-y0+1))
    values = data[y-y0, x-x0].astype(np.float64)
    if src.nodata is not None:
        values[values == src.nodata] = np.nan
    return values


def cop_width(north):
    # Longitudinal resolution changes in latitude bands. Each interpolation
    # row uses its own native spacing, including when a sample straddles a band.
    absolute = abs(north - .5)
    for limit, columns in ((50,3600),(60,2400),(70,1800),(80,1200),(85,720),(91,360)):
        if absolute < limit:
            return columns


def sample(cache, product, lon, lat):
    shape = lon.shape
    lon, lat = lon.ravel(), lat.ravel()
    is_cop = product == "cop"
    span = 1 if is_cop else 15
    height = 3600
    offset = 0 if is_cop else .5
    y = np.clip((90-lat)*(height/span)-offset, 0, 180*height//span-1)
    y0 = np.floor(y).astype("int32")
    fy = y-y0
    blended = np.zeros(lat.size, dtype=np.float64)
    valid_weight = np.zeros_like(blended)
    zero_weight = np.zeros_like(blended)
    for yi, wy in ((y0, 1-fy), (np.minimum(y0+1, 180*height//span-1), fy)):
        bands = yi//height
        for band in np.unique(bands):
            ids = np.flatnonzero(bands == band)
            north = 90-int(band)*span
            width = cop_width(north) if is_cop else 3600
            x = (lon[ids]+180)*width/span-offset
            x0 = np.floor(x).astype("int32")
            fx = x-x0
            for xi, wx in ((x0,1-fx),(x0+1,fx)):
                xi = xi % (360*width//span)
                columns = xi//width
                for column in np.unique(columns):
                    which = columns == column
                    points = ids[which]
                    west = int(column)*span-180
                    w = wy[points]*wx[which]
                    values = node_values(cache, product, north, west, xi[which]%width, yi[points]%height, width, height)
                    valid = np.isfinite(values)
                    blended[points] += np.where(valid,values,0)*w
                    valid_weight[points] += valid*w
                    zero_weight[points] += (values==0)*w
    return [a.reshape(shape) for a in (blended, valid_weight, zero_weight)]


def make_projection(lon, lat, epsg=None):
    if epsg is not None:
        crs = CRS.from_epsg(epsg)
        if not crs.is_projected or not math.isclose(crs.axis_info[0].unit_conversion_factor, 1):
            raise ValueError("--epsg must be a projected CRS measured in metres")
        return crs
    if -80 <= lat < 84:
        zone = min(60,1+int((lon+180)//6))
        return CRS.from_epsg((32600 if lat>=0 else 32700)+zone)
    return CRS.from_proj4(f"+proj=aeqd +lat_0={lat} +lon_0={lon} +ellps=WGS84 +units=m +type=crs")


def make_grid(args):
    if not (-180 <= args.center_lon <= 180 and -90 <= args.center_lat <= 90):
        raise ValueError("Centre outside WGS84 longitude/latitude bounds")
    if not (2 <= args.rows <= 4097 and 2 <= args.columns <= 4097 and args.rows*args.columns <= 4194304):
        raise ValueError("Use 2..4097 vertices per side and at most 4194304 vertices per local viewer patch")
    crs = make_projection(args.center_lon,args.center_lat,args.epsg)
    forward = Transformer.from_crs(4326, crs, always_xy=True)
    inverse = Transformer.from_crs(crs, 4326, always_xy=True)
    cx, cy = forward.transform(args.center_lon,args.center_lat)
    # Anchor vertices to the same 100 m lattice for both even and odd shapes.
    # The geometric centre of an even-sized side then lies between its two centre vertices.
    west = round(cx/STEP - (args.columns-1)/2) * STEP
    north = round(cy/STEP + (args.rows-1)/2) * STEP
    x = west + np.arange(args.columns) * STEP
    y = north - np.arange(args.rows) * STEP
    east, north = np.meshgrid(x,y)
    lon,lat = inverse.transform(east,north)
    if not np.all(np.isfinite(lon)&np.isfinite(lat)):
        raise ValueError("Requested sample window leaves the valid projection domain")
    return crs,x,y,lon,lat


def terrain(cache, lon, lat, bathy_only):
    coarse,total,_ = sample(cache,"etopo",lon,lat)
    if np.any(total < .999999):
        raise ValueError("ETOPO has a missing sample in this window")
    if bathy_only:
        return np.rint(coarse).astype("<i2"), np.full(coarse.shape,3,dtype="u1")
    land, cop, zero = sample(cache,"cop",lon,lat)
    # Copernicus flattens oceans to zero. Preserve negative measured land DSM;
    # only water-level (0 m) nodes take negative ETOPO bed. At source edges
    # interpolate across adjacent native tiles or contribute ETOPO for missing nodes.
    weight = np.clip(1-cop+np.where(coarse<0,zero,0),0,1)
    surface = land+weight*coarse
    provenance = np.where(cop<1e-6,3,np.where(weight>1-1e-6,2,np.where(weight<1e-6,1,4))).astype("u1")
    if not np.all(np.isfinite(surface)) or surface.min() < -12000 or surface.max() > 10000:
        raise ValueError("Invalid physical elevations")
    return np.rint(surface).astype("<i2"), provenance


def write_atomic(path, data):
    with tempfile.NamedTemporaryFile(dir=path.parent,delete=False) as f:
        tmp = Path(f.name)
        f.write(data)
    try:
        # Claim the destination first. This also works on drives without hard links.
        with path.open("xb"):
            pass
        try:
            os.replace(tmp,path)
        except OSError:
            path.unlink(missing_ok=True)
            raise
    finally:
        tmp.unlink(missing_ok=True)


def build_raw(args, output):
    mask_path = output.with_suffix(".source.u8")
    metadata_path = output.with_suffix(".json")
    for path in (output,mask_path,metadata_path):
        if path.exists():
            raise FileExistsError(f"Output exists: {path}")
    crs,x,y,lon,lat = make_grid(args)
    cache = TileCache(args.cache)
    try:
        h,mask = terrain(cache,lon,lat,args.etopo_only)
    finally:
        cache.close()
    output.parent.mkdir(parents=True,exist_ok=True)
    write_atomic(output,h.tobytes())
    write_atomic(mask_path,mask.tobytes())
    sources = [{"url":key,**value} for key,value in sorted(cache.sources.items())]
    bounds = {"west":float(x[0]),"east":float(x[-1]),"south":float(y[-1]),"north":float(y[0])}
    corners = {name:[float(lon[i,j]),float(lat[i,j])] for name,i,j in (("nw",0,0),("ne",0,-1),("sw",-1,0),("se",-1,-1))}
    ids,counts = np.unique(mask,return_counts=True)
    def scale(l,p):
        f=Proj(crs).get_factors(l,p)
        return [f.meridional_scale,f.parallel_scale]
    center=Transformer.from_crs(crs,4326,always_xy=True).transform((x[0]+x[-1])/2,(y[0]+y[-1])/2)
    metadata = {
      "format":"ORBIS_EARTH_PATCH_1","file":output.name,"width":args.columns,"height":args.rows,
      "dtype":"int16 little-endian","layout":"row-major; north to south, west to east; boundary-inclusive vertex samples",
      "projection":crs.to_string(),"epsg":crs.to_epsg(),"wkt":crs.to_wkt(),"boundsProjectedMeters":bounds,
      "centerLonLat":list(center),"cornersLonLat":corners,"spacingProjectedMeters":STEP,
      "centerScaleFactorsMeridianParallel":scale(*center),
      "cornerScaleFactorsMeridianParallel":{n:scale(*v) for n,v in corners.items()},
      "realMetersPerGameMeter":100,"verticalDatum":"metres relative to EGM2008; nearest whole metre",
      "minElevationM":int(h.min()),"maxElevationM":int(h.max()),"sha256":sha(output),
      "sourceMask":{"file":mask_path.name,"sha256":sha(mask_path),"dtype":"uint8","counts":dict(zip(map(str,ids),map(int,counts))),
       "legend":{"1":"GLO-30 surface","2":"ETOPO bathymetry over zero GLO-30","3":"ETOPO available without GLO-30 or selected alone","4":"blend at coast or tile availability boundary"}},
      "generatedUTC":datetime.datetime.now(datetime.timezone.utc).date().isoformat(),
      "sources":sources,"copernicusAttribution":NOTICE, "copernicusLiability":DISCLAIMER,
      "copernicusLicence":COP_LICENCE,
      "etopoCitation":"NOAA National Centers for Environmental Information. 2022. ETOPO 2022 15 Arc-Second Global Relief Model. https://doi.org/10.25921/fd45-gt74",
      "sampling":"Bilinear at fixed projected vertex coordinates. GLO-30 is a surface model, nominal 1 arc-second north/south; east/west sample spacing varies with latitude. ETOPO surface elevations include land, ice surface and ocean floor at 15 arc-seconds. The 100 m output spacing is in projected coordinates."}
    write_atomic(metadata_path,json.dumps(metadata,ensure_ascii=False,indent=2,allow_nan=False).encode("utf-8"))
    return metadata


def build_viewer_package(args, destination):
    if destination.exists():
        raise FileExistsError(f"Select a new package directory: {destination}")
    if args.cache.resolve().is_relative_to(destination.resolve()):
        raise ValueError("Choose a tile cache outside the new package directory")
    template = json.loads((BASE_PACKAGE/"manifest.json").read_text(encoding="utf-8"))
    world = BASE_PACKAGE/"global"/"earth-global.i16"
    world_meta = json.loads((BASE_PACKAGE/"global"/"metadata.json").read_text(encoding="utf-8"))
    dimensions = [template["globalWidth"], template["globalHeight"]]
    if (template["globalFile"] != "global/earth-global.i16" or world_meta["overview"]["dimensions"] != dimensions
            or world.stat().st_size != 2*dimensions[0]*dimensions[1] or sha(world) != world_meta["overview"]["sha256"]):
        raise ValueError("The bundled global overview differs from its manifest or checksum")
    destination.parent.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(dir=destination.parent,prefix=".orbis-earth-") as temp:
        staging = Path(temp)
        local = build_raw(args, staging/"local"/"region-100m.i16")
        shutil.copytree(BASE_PACKAGE/"global", staging/"global")
        shutil.copytree(BASE_PACKAGE/"licenses", staging/"licenses")
        shutil.copy2(BASE_PACKAGE/"NOTICE.txt", staging/"licenses"/"base-package-notice.txt")
        notices = [f"ORBIS 1:100 Earth terrain. Regional CRS: {local['projection']}.", local["etopoCitation"],
                   f"Regional grid generated {local['generatedUTC']}; source URLs and checksums are in local/region-100m.json.",
                   "The global ETOPO attribution and source details are in global/metadata.json."]
        if not args.etopo_only and any(k in local["sourceMask"]["counts"] for k in ("1","2","4")):
            notices.extend((NOTICE, DISCLAIMER, COP_LICENCE))
        (staging/"NOTICE.txt").write_text("\n\n".join(notices)+"\n",encoding="utf-8")
        lon,lat = local["centerLonLat"]
        template.update(localName=args.name or f"{lat:.3f}°, {lon:.3f}°",localWidth=local["width"],
                        localHeight=local["height"],localSpacingMeters=STEP,localCenterLatitude=lat,
                        localCenterLongitude=lon,localFile="local/region-100m.i16")
        (staging/"manifest.json").write_text(json.dumps(template,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
        # mkdir claims the destination without replacing even an empty existing directory.
        # Publish the manifest last so a partial copy cannot be opened as a complete kit.
        destination.mkdir()
        for item in ("local","global","licenses","NOTICE.txt","manifest.json"):
            (staging/item).rename(destination/item)
    return local


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--center-lon", type=float, default=121.5360545072164)
    p.add_argument("--center-lat", type=float, default=24.095716849595306)
    p.add_argument("--columns", type=int, default=1025)
    p.add_argument("--rows", type=int, default=513)
    p.add_argument("--epsg", type=int, help="Optional metre-based CRS, for matching adjoining patches")
    p.add_argument("--etopo-only", action="store_true",help="Request only the global 15 arc-second source")
    dest = p.add_mutually_exclusive_group(required=True)
    dest.add_argument("--output", type=Path, help="Write a new .i16 grid plus georeferencing and source mask")
    dest.add_argument("--package", type=Path, help="Create a new folder for the Unity Earth viewer")
    p.add_argument("--name", help="Display name in the Earth viewer")
    p.add_argument("--cache", type=Path, default=Path.home()/".cache"/"orbis-earth")
    args = p.parse_args()
    if args.package:
        local=build_viewer_package(args,args.package)
    else:
        if args.output.suffix.lower() != ".i16":
            p.error("--output must end in .i16")
        local=build_raw(args,args.output)
    print(f"{args.package or args.output}: {local['width']}x{local['height']}, "
          f"{local['minElevationM']} .. {local['maxElevationM']} m, sources {local['sourceMask']['counts']}")


if __name__ == "__main__":
    main()
