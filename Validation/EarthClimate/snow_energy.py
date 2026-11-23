"""Seasonal snow-water and surface energy budgets, from monthly generated fields."""
import numpy as np


def smooth(value):
    f = np.clip(value, 0, 1)
    return f*f*(3-2*f)


def snow_balance(snow, demand):
    """Periodic seasonal store. All storage, overflow and melt are mm water equivalent."""
    storage = np.where((snow-demand).sum(1) > 0, 600., 0.)
    for month in range(12):
        storage = np.clip(storage + snow[:,month] - demand[:,month], 0, 600)
    initial = storage.copy()
    cover = np.empty_like(demand)
    melt = np.empty_like(demand)
    overflow = np.zeros_like(initial)
    for month in range(12):
        available = storage + snow[:,month]
        melt[:,month] = np.minimum(available, demand[:,month])
        remaining = available - melt[:,month]
        storage = np.minimum(600, remaining)
        overflow += remaining - storage
        duration = np.minimum(1, available/np.maximum(.001, demand[:,month]))
        cover[:,month] = duration*(-np.expm1(-(available+storage)/50))
    if not np.allclose(storage, initial, atol=1e-5) or not np.allclose(snow.sum(1), melt.sum(1)+overflow, atol=1e-5):
        raise ValueError('Seasonal snow-water budget did not close.')
    return cover, melt


def snow_response(temperature, rain, cloud, sunlight):
    """Seasonal W/m2 forcing per unit fitted boundary-layer temperature response."""
    t = temperature.astype(float,copy=False)
    cold, heat = np.maximum(0,-t).sum(1), np.maximum(0,t).sum(1)
    frozen = np.divide(cold, cold+heat, out=np.zeros_like(cold), where=cold+heat>0)
    # Rain and cloud use separate transported water fields. Sunlight is in units of 300 W/m2.
    solid = smooth((2-t)/4)
    positive_days = 15*(np.sqrt(t*t+4)+t)*smooth((t+8)/8)
    demand = 3*positive_days*(.65+.35*sunlight*np.maximum(.65,1-.35*cloud))
    cover, melt = snow_balance(solid*rain, demand)
    uniform_cover, uniform_melt = snow_balance(solid*rain.mean(1,keepdims=True), demand)
    growing = 30*np.maximum(0,t-5).sum(1)
    annual_rain = rain.sum(1)
    canopy = growing/(growing+500)*annual_rain/(annual_rain+400)
    reflected = (cover-uniform_cover)*300*sunlight*(.74-.34*cloud)*(.42-.20*canopy[:,None])
    fusion = (melt-uniform_melt)*334000/(30*86400)
    forcing = -reflected-fusion
    exposure = np.maximum(cover,uniform_cover)
    mean_cover = exposure.mean(1,keepdims=True)
    snow_season = np.divide(exposure,mean_cover,out=np.zeros_like(exposure),where=mean_cover>0)
    spread = frozen[:,None] + (1-frozen[:,None])*snow_season
    forcing -= forcing.mean(1,keepdims=True)*spread
    memory = np.exp(-30/22.5)
    periodic = sum((1-memory)*memory**age*np.roll(forcing,age,axis=1) for age in range(12))/(1-memory**12)
    return periodic*frozen[:,None]
