# ORBIS 升级后技术路线 — Astra 实施版
## 行星大气 / 动态天气 / 体积云 / 水文河流 / 海洋 / 水体渲染 / 行星导航统一架构

> **用途**
>
> 本文是 ORBIS 当前升级路线的工程实施文档，可直接交给 Astra 作为后续开发依据。
>
> Astra 应把本文视为：
>
> 1. 当前 ORBIS 的技术方向；
> 2. 模块职责边界；
> 3. 实施顺序；
> 4. 不允许随意推翻的架构原则；
> 5. 每个阶段的验收标准。
>
> 本路线综合参考并吸收以下源码/方案中的有效部分：
>
> - `World Atlas Engine`
>   - 全球气候基线
>   - 水文
>   - Priority-Flood
>   - D8 / MFD
>   - 河网提取
>   - 湖泊与汇流
>
> - `UniStorm 5.3.4`
>   - 体积云 Raymarch
>   - Base / Detail / Curl Noise
>   - 云层自阴影
>   - Temporal Reconstruction
>   - 天气视觉状态
>   - 雷电 / 雨雪 / 雾 / 风等表现
>
> - `Stylized Water 3 v3.0.1`
>   - URP 水体渲染框架
>   - Wave Profile
>   - Gerstner Waves
>   - Flow Map Shader 接口
>   - 泡沫
>   - 折射 / 反射
>   - 水面高度查询
>   - 河流视觉模式
>
> - `A Pathfinding Project Pro v5.3.5`
>   - 曲面世界导航
>   - `AIPathAlignedToSurface`
>   - `ProceduralGraphMover`
>   - Grid / Recast 局部动态导航
>
> - `Where the River Goes`
>   - 河床进入地形
>   - Flow Field
>   - Flow-driven normal
>   - 河流泡沫
>
> - `Seascape`
>   - 多尺度海浪
>   - Choppiness
>   - 波浪方向扰动
>   - 海面高频细节设计思想
>
> ---
>
> **最终目标：**
>
> ORBIS 不再由多个互不关联的“视觉插件”拼接，而是由一个统一的行星状态系统驱动：
>
> ```text
> Planet
>   ↓
> Climate
>   ↓
> Weather
>   ↓
> Clouds
>   ↓
> Rain / Snow
>   ↓
> Hydrology
>   ↓
> Rivers / Lakes
>   ↓
> Estuary
>   ↓
> Ocean
>   ↓
> Water Rendering
> ```
>
> 所有可视系统都只是这个世界状态的消费者。

---

# 0. Astra 执行原则

Astra 在实现本文时必须遵守以下原则。

## 0.1 不要把插件当成“世界逻辑真相”

以下插件不拥有 ORBIS 的核心状态：

```text
UniStorm
Stylized Water 3
A* Pathfinding Project
```

它们分别是：

```text
UniStorm = 天气与云的视觉执行器

SW3 = 水体视觉执行器

A* = 局部导航执行器
```

真正的数据源必须来自：

```text
ORBIS Planet Core
ORBIS Climate
ORBIS Hydrology
ORBIS WaterState
```

---

## 0.2 数据层必须先于表现层

任何新效果加入前，先问：

```text
它依赖什么世界数据？
```

例如：

```text
云
```

不能先做 Noise，然后再问哪里应该有云。

应该：

```text
Humidity
Pressure
Wind
CloudWater
        ↓
Cloud Coverage
        ↓
Volumetric detail
```

同理：

```text
河流
```

不能先画 spline 再假装有水文。

必须：

```text
Terrain
↓
Runoff
↓
Flow Routing
↓
River Graph
↓
Spline
```

---

## 0.3 不允许模块重复拥有同一份状态

错误：

```text
UniStorm 有一份 Wind
Ocean 有另一份 Wind
Vegetation 再有一份 Wind
```

正确：

```text
ORBISWindField
     │
     ├── Clouds
     ├── Ocean
     ├── Vegetation
     ├── Particles
     └── Weather FX
```

---

## 0.4 球形行星优先

所有新模块不得只假设：

```text
Y = Up
XZ = 地面
```

除非该模块被明确限制为局部切平面。

必须支持：

```text
PlanetCenter
PlanetRadius
LocalUp
LocalEast
LocalNorth
```

---

## 0.5 平面局部世界与球体必须共享同一个物理世界

ORBIS 的目标不是：

```text
平面地图 = 一个世界
球体 = 另一个世界
```

而是：

```text
同一个 Planet State
↓
不同 Projection
```

因此：

```text
地形
水体
云
导航
河流
天气
```

都要引用统一的 Planet Coordinates。

---

# 1. ORBIS 最终总架构

```text
                        ORBIS PLANET CORE
                               │
         ┌─────────────────────┼─────────────────────┐
         │                     │                     │
      Terrain               Climate               Ocean
         │                     │                     │
         │             Temperature/Pressure          │
         │             Humidity/Wind/CloudWater      │
         │                     │                     │
         │                     ▼                     │
         │                 WeatherState              │
         │                     │                     │
         │          ┌──────────┼──────────┐          │
         │          │          │          │          │
         │       Clouds      Rain      Snow/Ice      │
         │          │          │          │          │
         │          └──────────┼──────────┘          │
         │                     ▼                     │
         │                 Hydrology                 │
         │                     │                     │
         │         ┌───────────┼───────────┐         │
         │         │           │           │         │
         │       Rivers       Lakes      Runoff      │
         │         │           │                     │
         │         └───────────┼──────────────┐      │
         │                     ▼              │      │
         │                  Estuary           │      │
         │                     │              │      │
         └─────────────────────┼──────────────┘      │
                               ▼                     │
                           WaterState ←───────────────┘
                               │
                   ┌───────────┼───────────┐
                   │           │           │
                  SW3       Gameplay      Physics
                   │           │           │
              Rendering     Boats/AI    Buoyancy
                               │
                               ▼
                              GAME
```

---

# 2. 模块职责边界

## 2.1 ORBIS Planet Core

负责：

```text
PlanetCenter
PlanetRadius
AtmosphereRadius

Rotation
AxialTilt
OrbitalSunDirection

World ↔ Planet coordinates
Planet ↔ Local tangent
Flat projection ↔ Sphere projection
Floating Origin
```

Planet Core 不负责：

```text
天气细节
河流
云
水面材质
```

---

## 2.2 ORBIS Climate

负责：

```text
Temperature
Pressure
Humidity
Wind
CloudWater
PrecipitationPotential
SnowPotential
SoilMoisture
StormPotential
```

Climate 是天气系统的数据源。

---

## 2.3 ORBIS Weather

负责将 Climate State 分类成可视天气。

例如：

```text
Clear
Cloudy
Overcast
Rain
HeavyRain
Thunderstorm
Snow
Blizzard
Fog
Dust
```

但这些标签只是“表现分类”。

真实世界状态仍是连续数值。

---

## 2.4 ORBIS Clouds

负责：

```text
Global cloud coverage
Regional cloud structures
Volumetric cloud detail
Cloud optical depth
Cloud shadow
```

---

## 2.5 ORBIS Hydrology

负责：

```text
Runoff
Flow direction
Flow accumulation
Lakes
River graph
River discharge
River width
River depth
River flow velocity
Sediment
River erosion
```

---

## 2.6 ORBIS Water

负责统一：

```text
River
Lake
Estuary
Ocean
```

输出统一：

```text
WaterState
```

---

## 2.7 SW3

负责：

```text
Water surface shader
Gerstner
Normal
Foam
Reflection
Refraction
Caustics
Height query
```

但不负责决定：

```text
河在哪里
流向哪里
流量是多少
```

---

## 2.8 UniStorm

负责：

```text
Volumetric cloud rendering
Weather transitions
Rain FX
Snow FX
Lightning
Fog FX
Wind FX
Audio
```

但不负责决定天气本身。

---

## 2.9 A*

负责：

```text
Local movement
Regional navigation
Curved surface alignment
Dynamic graph streaming
```

不负责全球行星战略路径。

---

# 3. Planet Coordinate System

这是所有后续系统的底座。

必须首先统一。

---

## 3.1 核心坐标

```csharp
struct PlanetFrame
{
    double3 planetCenter;
    double planetRadius;

    double3 worldPosition;

    float3 localUp;
    float3 localEast;
    float3 localNorth;
}
```

定义：

```text
LocalUp =
normalize(WorldPosition - PlanetCenter)
```

然后计算：

```text
East
North
```

构成切平面基。

---

## 3.2 所有局部模拟使用 Tangent Space

例如：

```text
Wind.xy
RiverFlow.xy
OceanWaveDirection.xy
Navigation.xy
```

都应是：

```text
Planet Tangent Space
```

而不是固定：

```text
World XZ
```

---

# 4. 全球 Climate Grid

推荐使用球面网格。

可以优先考虑：

```text
Cube Sphere
```

而不是 equirectangular texture。

原因：

```text
纬极失真小
适合 Compute Shader
适合 clipmap/LOD
```

---

## 4.1 Climate Cell

```csharp
struct ClimateCell
{
    float temperature;
    float pressure;

    float humidity;
    float cloudWater;

    float2 wind;

    float precipitation;
    float soilMoisture;

    float snow;
    float stormPotential;
}
```

---

# 5. Climate 初始化

World Atlas Engine 的 `physical/climate.py` 可作为算法参考。

初始化因素：

```text
Latitude
Solar Insolation
Axial Tilt
Season
Elevation
Ocean Distance
Terrain Slope
Land/Sea
```

输出：

```text
Temperature baseline
Humidity baseline
Wind baseline
Precipitation baseline
```

---

# 6. 动态气候更新

建议采用较低频率模拟：

```text
0.2 Hz ~ 1 Hz
```

而不是每帧。

更新：

```text
Temperature
Pressure
Wind
Humidity
CloudWater
Rain
```

---

# 7. 温度模型

至少包含：

```text
Solar input
Latitude
Altitude
Day/Night
Ocean moderation
Cloud shading
Greenhouse baseline
```

概念：

\[
T_{next}
=
T
+
Heating
-
Cooling
+
Advection
\]

---

# 8. 太阳照射

必须直接使用：

```text
dot(LocalUp, SunDirection)
```

得到太阳高度。

夜侧：

```text
SolarInput = 0
```

不能再出现：

```text
没太阳仍持续高温
```

---

# 9. 温室效应

不要第一版就做复杂辐射传输。

先使用：

```text
GreenhouseRetention
```

控制夜间长波散热。

例如：

```text
CoolingRate *= 1 - greenhouseFactor
```

以后再升级。

---

# 10. Pressure / Wind

Pressure 可以先来自：

```text
Temperature anomaly
Latitude circulation baseline
Storm systems
```

风：

\[
Wind \propto -\nabla Pressure
\]

再加入 Coriolis：

```text
Northern hemisphere
Southern hemisphere
```

方向相反。

---

# 11. 水汽输送

必须使用：

```text
Wind Advection
```

移动 humidity。

否则永远会出现：

```text
某些格子永久潮湿
某些格子永久干燥
```

---

# 12. 蒸发

海洋：

```text
Evaporation ∝
WindSpeed
×
Temperature
×
(1 - Humidity)
```

湖泊 / 河流同样可贡献，但权重更小。

---

# 13. 山脉降雨

World Atlas 的 Orographic Rain 思路保留：

```text
Wind
↓
Mountain slope
↓
Air rises
↓
Condensation
↓
Rain
```

背风侧：

```text
Rain shadow
```

---

# 14. 云水

建议：

```text
CloudWater += Condensation
CloudWater -= Precipitation
CloudWater += Advection
```

云层覆盖：

```text
CloudCoverage =
function(CloudWater, Humidity, Pressure)
```

---

# 15. UniStorm 云重构路线

保留：

```text
Volumetric Raymarch
Base Noise
Detail Noise
Curl Noise
Temporal Reconstruction
Lighting
Self Shadow
```

替换：

```text
固定 Earth radius
固定 world-up
Weather random driver
```

---

# 16. Planet Cloud Shell

新增：

```text
PlanetCenter
PlanetRadius
CloudBaseAltitude
CloudTopAltitude
```

云壳半径：

```text
R0 = PlanetRadius + CloudBaseAltitude
R1 = PlanetRadius + CloudTopAltitude
```

---

# 17. 云密度公式

最终：

\[
Density =
GlobalCoverage
\times
RegionalCoverage
\times
ProceduralShape
\]

其中：

```text
GlobalCoverage
← ClimateGrid

RegionalCoverage
← Weather fronts / terrain / storms

ProceduralShape
← UniStorm noise
```

---

# 18. 云的全球分布

不能再使用：

```text
一个 Noise texture
```

决定云存在与否。

必须：

```text
ClimateGrid
↓
CloudCoverageMap
```

---

# 19. Regional Cloud Clipmap

玩家附近建立：

```text
Cloud Weather Clipmap
```

包含：

```text
Coverage
CloudWater
Wind
Storm
VerticalDevelopment
```

---

# 20. 云高度

根据：

```text
Temperature
Humidity
Pressure
StormPotential
```

选择：

```text
Low cloud
Mid cloud
High cloud
Convective cloud
```

后期可扩展多层云。

---

# 21. 云运动

不要：

```text
CloudUV += ConstantDirection * Time
```

使用：

```text
Climate Wind
```

进行 advection。

---

# 22. 云阴影

输出：

```text
CloudTransmittance
```

而不仅是 screen-space shadow。

推荐：

```text
CloudShadowClipmap
```

位于地表局部切平面。

---

# 23. Cloud Shadow Consumers

以下系统全部采样统一 CloudShadow：

```text
Terrain
Ocean
River
Vegetation
Atmosphere
Fog
```

---

# 24. 上帝光

Atmosphere/Fog Raymarch 时：

```hlsl
sunLight *= SampleCloudTransmittance(samplePosition);
```

形成真实云遮挡体积光。

---

# 25. ORBIS Atmosphere

不使用 UniStorm Skybox 作为最终大气核心。

ORBIS Atmosphere 必须支持：

```text
Ground
Aircraft altitude
Upper atmosphere
Orbit
Space
```

连续。

---

# 26. Atmosphere 参数

```csharp
struct AtmosphereParameters
{
    float planetRadius;
    float atmosphereRadius;

    float3 rayleighScattering;
    float rayleighScaleHeight;

    float3 mieScattering;
    float mieScaleHeight;
    float mieG;

    float3 ozoneAbsorption;
}
```

---

# 27. 推荐 LUT

至少：

```text
Transmittance LUT
Sky View LUT
Multi Scattering LUT
Aerial Perspective LUT
```

---

# 28. Atmosphere API

Shader 中必须能够：

```hlsl
GetSunTransmittance(...)
GetSkyRadiance(...)
GetAerialPerspective(...)
```

供：

```text
Cloud
Water
Terrain
```

统一使用。

---

# 29. Hydrology 总路线

World Atlas Engine 作为主要算法参考。

```text
Terrain Height
        ↓
Climate Runoff
        ↓
Priority-Flood
        ↓
Lake handling
        ↓
D8
        ↓
MFD
        ↓
Accumulation
        ↓
River selection
        ↓
River Graph
```

---

# 30. Priority-Flood

必须实现。

目的：

```text
修复微小人工洼坑
保证排水连续
保留真正湖泊
```

---

# 31. Lake Handling

湖泊不能全部填平。

必须区分：

```text
closed basin
overflow lake
ocean-connected basin
```

---

# 32. D8

用途：

```text
明确主 downstream
建立 River Graph
```

每格一个主要下游。

---

# 33. MFD

用途：

```text
平原
宽谷
三角洲
```

允许流量分给多个邻居。

---

# 34. Flow Accumulation

每格汇流：

\[
A_i =
Runoff_i
+
\sum upstream
\]

这个值最终决定：

```text
river existence
river width
river depth
discharge
```

---

# 35. River Extraction

达到阈值：

```text
Accumulation > RiverThreshold
```

生成候选河段。

同时进行：

```text
headwater pruning
main-river retention
```

避免噪声河网。

---

# 36. River Graph

```csharp
struct RiverReach
{
    int id;

    int fromNode;
    int toNode;

    float discharge;
    float length;

    float width;
    float depth;

    int streamOrder;

    RiverOutletType outlet;
}
```

---

# 37. River Spline

River Graph cell path：

```text
Grid path
↓
World coordinates
↓
Valley constrained smoothing
↓
Spline
```

不能单纯 spline smooth 导致穿山。

---

# 38. River Meander

根据：

```text
Slope
Discharge
Floodplain width
Sediment
```

决定蛇曲强度。

山地：

```text
低 meander
```

平原：

```text
高 meander
```

---

# 39. River Width

建议：

\[
W = k_w Q^b
\]

其中：

```text
Q = discharge
```

第一版先用调参经验关系。

---

# 40. River Depth

类似：

\[
D = k_d Q^f
\]

保证：

```text
大河更深
支流更浅
```

---

# 41. River Bed Carving

真正修改 Terrain。

横截面：

```text
Channel
Bank
Floodplain
```

至少三部分。

---

# 42. River Flow Velocity

第一版：

\[
V \approx k\sqrt{Slope}
\]

再结合：

```text
Discharge
Width
Depth
```

后续可改 Manning。

---

# 43. River Flow Field

每段 spline rasterize 到 Flow Map：

```text
R = Flow X
G = Flow Y
B = Speed
A = WaterDepth / validity
```

---

# 44. SW3 Flow Map Integration

SW3 已有 Flowmap Shader 接口。

直接让：

```text
ORBIS FlowMap
↓
SW3 Normal movement
↓
Foam
↓
Surface detail
```

---

# 45. River Foam

来源：

```text
Flow speed
Flow gradient
Slope
Shallow depth
Obstacle
```

统一生成：

```text
FoamPotential
```

由 SW3 渲染。

---

# 46. River Sediment

新增：

```text
Sediment
```

来源：

```text
erosion
runoff
slope
discharge
```

随 Flow advect。

---

# 47. 河口

当：

```text
River Outlet = Ocean
```

进入 Estuary。

必须生成：

```text
OceanWeight
```

---

# 48. OceanWeight

```text
River = 0
Estuary = 0~1
Ocean = 1
```

根据：

```text
mouth distance
depth
river width
coast distance
```

综合。

---

# 49. 河流 → 海洋水面

统一：

\[
H =
H_{base}
+
(1-W_o)H_{river}
+
W_oH_{ocean}A_{depth}
+
H_{shore}
\]

---

# 50. SW3 Wave System

SW3 v3 使用更完整 WaveProfile。

可保留：

```text
Directional Waves
Radial Waves
Amplitude
Steepness
Wave Length
Direction Variation
```

---

# 51. Seascape 的作用

仅作为设计思想：

```text
multi-scale wave
direction variation
choppiness
```

不要搬完整 raymarch。

---

# 52. Ocean Wind Coupling

SW3 波浪方向由：

```text
ORBIS Wind
```

驱动。

```text
Wind speed
→ Wave amplitude

Wind direction
→ Wave direction
```

---

# 53. Storm Coupling

Storm：

```text
Wave amplitude ↑
Foam ↑
Choppiness ↑
Wave spectrum ↑
```

---

# 54. Shallow Water

Ocean wave 在浅水衰减：

```text
DepthAttenuation
```

避免：

```text
4m 海浪直接进入 2m 宽河流
```

---

# 55. Shore Wave

单独一层：

```text
ShoreWave
```

负责：

```text
shore breaking
estuary surge
shallow oscillation
```

---

# 56. Final Normal

优先从：

```text
Final Water Height
```

重新计算。

不要仅：

```text
lerp(RiverNormal, OceanNormal)
```

---

# 57. SW3 Ocean Geometry

SW3 自带 camera-follow ocean mesh 不能作为最终 Planet Ocean。

保留：

```text
wave/shader
```

替换：

```text
geometry
```

---

# 58. Planet Water Geometry

推荐：

```text
Planet Water Clipmap
```

LOD：

```text
LOD0 local dense
LOD1
LOD2
LOD3
Orbit
```

---

# 59. 水面局部坐标

Ocean Wave 使用：

```text
Planet Tangent Coordinates
```

而不是：

```text
World XZ
```

---

# 60. WaterState

最终统一：

```csharp
struct WaterState
{
    float surfaceHeight;
    float depth;

    float2 flowVelocity;

    float discharge;

    float oceanWeight;
    float coastDistance;

    float waveEnergy;

    float foamPotential;

    float sediment;
    float turbidity;
}
```

---

# 61. Water Query API

游戏逻辑统一：

```csharp
WaterSample SampleWater(double3 worldPosition);
```

返回：

```text
Height
Normal
Flow
Depth
Type weight
```

---

# 62. Buoyancy

CPU 查询必须尽量与 GPU 水面一致。

船不能出现：

```text
画面在波峰
物理却在波谷
```

---

# 63. Weather → River

必须建立真实链路：

```text
Rain
↓
Soil
↓
Runoff
↓
River Discharge
```

---

# 64. Flood

后期：

```text
Heavy Rain
↓
Discharge ↑
↓
River Level ↑
↓
Floodplain
```

架构必须允许扩展。

---

# 65. Drought

同理：

```text
Low precipitation
↓
soil moisture ↓
↓
runoff ↓
↓
river discharge ↓
```

---

# 66. Snow

高海拔：

```text
Snow accumulation
```

温度上升：

```text
Snow melt
↓
Runoff
```

---

# 67. A* 行星导航

不创建全球巨大 GridGraph。

最终：

```text
Global Strategic Graph
+
Regional Graph
+
Local Recast
```

---

# 68. Global Strategic Graph

节点：

```text
city
road hub
bridge
port
mountain pass
river crossing
```

---

# 69. Local Navigation

玩家附近：

```text
ProceduralGraphMover
```

跟随角色更新：

```text
Tiled Recast
或
GridGraph
```

---

# 70. Curved Planet AI

角色移动使用：

```text
AIPathAlignedToSurface
```

LocalUp：

```text
normalize(position - PlanetCenter)
```

---

# 71. 河流对导航的影响

River Graph 直接生成：

```text
navigation cost
```

规则：

```text
small stream
→ crossable

medium river
→ ford / bridge

large river
→ bridge / ferry only
```

---

# 72. Unity 项目建议目录

```text
Assets/
├── ORBIS/
│   │
│   ├── Core/
│   │   ├── Planet/
│   │   ├── Coordinates/
│   │   └── FloatingOrigin/
│   │
│   ├── Atmosphere/
│   │   ├── Runtime/
│   │   ├── Shaders/
│   │   ├── Compute/
│   │   └── Debug/
│   │
│   ├── Climate/
│   │   ├── Runtime/
│   │   ├── Compute/
│   │   └── Data/
│   │
│   ├── Clouds/
│   │   ├── Runtime/
│   │   ├── Shaders/
│   │   ├── Compute/
│   │   └── Bridge/
│   │
│   ├── Hydrology/
│   │   ├── Runtime/
│   │   ├── Compute/
│   │   ├── River/
│   │   ├── Lake/
│   │   └── Debug/
│   │
│   ├── Water/
│   │   ├── Runtime/
│   │   ├── Shaders/
│   │   ├── SW3Bridge/
│   │   └── Debug/
│   │
│   └── Navigation/
│       ├── Runtime/
│       └── AStarBridge/
│
├── ThirdParty/
│   ├── UniStorm/
│   ├── StylizedWater3/
│   └── AstarPathfindingProject/
```

---

# 73. 不要把自研逻辑直接写进插件目录

原则：

```text
ThirdParty 尽可能不改
ORBIS 使用 Bridge / Adapter
```

只有无法避免时才 patch。

目的：

```text
未来升级插件时减少冲突
```

---

# 74. 推荐核心 C# 类

```text
ORBISPlanet.cs
ORBISCoordinateSystem.cs

ORBISClimateSystem.cs
ORBISClimateGrid.cs

ORBISWeatherSystem.cs
ORBISUniStormBridge.cs

ORBISCloudSystem.cs
ORBISCloudShadowSystem.cs

ORBISHydrologySystem.cs
ORBISRiverGraph.cs
ORBISRiverSplineBuilder.cs
ORBISRiverCarver.cs

ORBISWaterSystem.cs
ORBISWaterStateProvider.cs
ORBISSW3Bridge.cs

ORBISNavigationSystem.cs
ORBISAStarBridge.cs
```

---

# 75. 推荐核心 HLSL

```text
ORBISPlanetCoordinates.hlsl
ORBISAtmosphere.hlsl

ORBISClimateSampling.hlsl

ORBISCloudShell.hlsl
ORBISCloudLighting.hlsl
ORBISCloudShadow.hlsl

ORBISRiverFlow.hlsl

ORBISWaterState.hlsl
ORBISOceanWave.hlsl
ORBISRiverWave.hlsl
ORBISShoreWave.hlsl
ORBISWaterSurface.hlsl
```

---

# 76. Compute Shader

建议：

```text
ClimateAdvection.compute
PressureWind.compute
Humidity.compute
CloudWater.compute

PriorityFlood.compute
FlowDirection.compute
FlowAccumulation.compute
RiverRaster.compute

CloudShadow.compute
```

Priority-Flood 是否放 GPU 第一版可根据复杂度决定。

允许先 CPU/Burst 做正确性版本。

---

# 77. Debug 系统

必须从一开始实现。

不要到最后再补。

---

# 78. Climate Debug

```text
Temperature
Pressure
Humidity
Wind
CloudWater
Rain
Snow
StormPotential
```

---

# 79. Hydrology Debug

```text
Raw Height
Filled Height
D8 Direction
MFD
Flow Accumulation
River Threshold
River Graph
Discharge
```

---

# 80. Water Debug

```text
Depth
Flow Direction
Flow Speed
OceanWeight
WaveHeight
Foam
Sediment
FinalNormal
```

---

# 81. Cloud Debug

```text
Global Coverage
Regional Coverage
Density
Optical Depth
Cloud Shadow
Raymarch steps
Temporal history
```

---

# 82. Planet Debug

```text
LocalUp
East
North
Projection Blend
Floating Origin Offset
```

---

# 83. 性能总原则

行星尺度系统必须：

```text
Global low resolution
+
Local high resolution
```

不能整个星球都高精度。

---

# 84. Climate LOD

全球：

```text
低分辨率球面网格
```

玩家附近：

```text
区域细化
```

---

# 85. Hydrology LOD

河网拓扑：

```text
global
```

局部：

```text
high-res river mesh
flow map
foam
```

---

# 86. Cloud LOD

地表附近：

```text
完整 volumetric
```

远处：

```text
lower raymarch resolution
```

轨道：

```text
planet-scale cloud shell / simplified density
```

---

# 87. Water LOD

### LOD0

```text
Flow
Full wave
Foam
Reflection
Refraction
Caustics
```

### LOD1

```text
Reduced wave
Reduced foam
```

### LOD2

```text
Macro wave
Simplified reflection
```

### Orbit

```text
Ocean BRDF
Cloud shadow
Sun glitter
```

---

# 88. 实施顺序总览

必须按以下顺序推进。

---

# Phase 0 — 基础整理

目标：

```text
项目可稳定运行
第三方插件独立
ORBIS 代码分层
```

任务：

```text
建立 Assets/ORBIS
建立 ThirdParty 边界
确认 Unity 版本
确认 URP
确认 Floating Origin
```

验收：

```text
项目无编译错误
SW3 Demo 可运行
UniStorm Demo 可运行
A* Demo 可运行
```

---

# Phase 1 — Planet Coordinate Core

实现：

```text
PlanetCenter
PlanetRadius
LocalUp
East/North
Tangent conversion
```

验收：

```text
任意星球表面位置
都能正确得到 local tangent basis
```

---

# Phase 2 — Climate Baseline

从 World Atlas 思路建立：

```text
temperature baseline
humidity baseline
wind baseline
precipitation baseline
```

暂时不做动态天气。

验收：

```text
赤道更热
高纬更冷
高山更冷
海洋附近更湿
山脉有雨影
```

---

# Phase 3 — Hydrology Offline / Static

实现：

```text
Priority-Flood
D8
MFD
Flow accumulation
River extraction
River graph
```

输入：

```text
Terrain
Climate runoff baseline
```

验收：

```text
河流顺坡
支流汇入主河
主河最终进入海洋/湖泊
无大量断河
```

---

# Phase 4 — River Geometry

实现：

```text
River spline
width
depth
terrain carving
```

验收：

```text
河流不是蓝线
河床真实进入地形
山地河较窄
平原河较宽
```

---

# Phase 5 — River Flow + SW3

实现：

```text
FlowVelocity
FlowMap
SW3 Flow integration
```

验收：

```text
水面流动方向与下游一致
弯道方向正确
泡沫与流速相关
```

---

# Phase 6 — Ocean + Estuary

实现：

```text
OceanWeight
River/Ocean wave blending
Depth attenuation
Shore wave
```

验收：

```text
河口无高度接缝
无 normal seam
海浪不会直接进入上游
```

---

# Phase 7 — Dynamic Climate

实现：

```text
temperature
pressure
humidity
wind
cloudWater
rain
```

验收：

```text
天气状态随时间移动
云区不固定
降雨区域随风变化
```

---

# Phase 8 — UniStorm Cloud Integration

重构：

```text
Planet cloud shell
Climate coverage
Climate wind
```

保留：

```text
UniStorm volumetric detail
```

验收：

```text
云位置由 Climate 决定
云形状仍有高质量程序化细节
```

---

# Phase 9 — Cloud Shadow + Atmosphere

实现：

```text
CloudTransmittance
Atmosphere sampling cloud shadow
```

验收：

```text
地面有云影
水面有云影
空气中出现与云对应的上帝光
```

---

# Phase 10 — Planet Atmosphere

实现：

```text
ground-to-space scattering
```

验收：

```text
地面
飞机
高空
轨道
太空
```

全部连续。

---

# Phase 11 — Navigation

实现：

```text
global strategic graph
local moving Recast/Grid
AIPathAlignedToSurface
```

验收：

```text
单位能在球面行走
Nav graph 能随玩家移动
河流影响路径成本
```

---

# Phase 12 — Simulation Coupling

建立闭环：

```text
Rain
→ Runoff
→ River

Wind
→ Cloud
→ Ocean

Cloud
→ Shadow
→ Temperature

Temperature
→ Snow melt
→ Runoff
```

验收：

```text
天气变化确实影响水文
而不是只影响画面
```

---

# 89. Astra 每阶段工作要求

每完成一个 Phase，必须提供：

```text
1. 修改文件清单
2. 新增文件清单
3. 核心算法说明
4. 关键代码
5. Unity Inspector 设置
6. Debug 方法
7. 已知限制
8. 下一阶段接口
```

---

# 90. Astra 不应一次重写整个系统

每次优先：

```text
小模块
可运行
可验证
可回滚
```

例如：

不要：

```text
一次同时重写 Climate + Cloud + Water
```

而应该：

```text
先 Hydrology static
确认正确
再 River rendering
```

---

# 91. 代码质量要求

所有 ORBIS 系统应：

```text
避免 GameObject.Find
避免每帧 FindObject
避免大量 GC allocation
避免频繁 new Texture
避免每帧 CPU/GPU sync
```

---

# 92. 数据驱动

配置应尽量：

```text
ScriptableObject
```

例如：

```text
PlanetProfile
ClimateProfile
AtmosphereProfile
HydrologyProfile
WaterProfile
```

---

# 93. Compute / CPU 分工

CPU：

```text
Topology
River graph
High-level state
```

GPU：

```text
Field simulation
Advection
Rendering
Clipmap
```

---

# 94. 双精度

行星世界坐标建议：

```text
double
```

局部渲染：

```text
float
```

通过 Floating Origin。

---

# 95. 商业插件许可边界

不要：

```text
把 SW3
UniStorm
A*
商业源码复制到公开仓库
```

ORBIS 自研代码可以单独管理。

---

# 96. 当前优先级

最高：

```text
P0 Planet Coordinates
P0 Hydrology
P0 River Flow
```

其次：

```text
P1 Water integration
P1 Climate
```

然后：

```text
P2 Clouds
P2 Atmosphere
```

最后：

```text
P3 Navigation integration
P3 advanced flood/sediment
```

---

# 97. 当前最重要的实际任务

如果 Astra 现在开始开发，第一批任务按此顺序：

### Task 1

实现：

```text
ORBISPlanetCoordinateSystem
```

### Task 2

把 World Atlas Hydrology 的核心算法重写成 Unity 可用版本：

```text
Priority-Flood
D8
MFD
Accumulation
```

### Task 3

输出 RiverGraph。

### Task 4

生成 RiverSpline。

### Task 5

RiverCarver 修改地形。

### Task 6

生成 FlowMap。

### Task 7

把 FlowMap 接入 SW3。

---

# 98. 第一阶段成功标准

完成前七个 Task 后，必须看到：

```text
山地
↓
溪流
↓
支流汇合
↓
主河
↓
蛇曲平原河
↓
河口
↓
海洋
```

并且：

```text
河床是真实地形
水沿下游流
河流宽度变化
泡沫随流速变化
```

达到这一点后，再进入气候云层大改。

---

# 99. 第二阶段重点

再开始：

```text
Climate Grid
Humidity Advection
CloudWater
Rain
UniStorm cloud coverage bridge
```

目标：

```text
云和雨不再固定
```

---

# 100. 第三阶段重点

最后把：

```text
Atmosphere
Cloud Shadow
Ocean
Climate
```

全部连接。

形成：

```text
Sun
 ↓
Atmosphere
 ↓
Cloud
 ↓
Cloud Shadow
 ↓
Surface heating
 ↓
Climate
```

闭环。

---

# 101. 最终视觉目标

从地表：

```text
真实河谷
动态河流
云影
降雨
体积云
真实天空
```

从高空：

```text
天气系统区域结构
海洋云影
大陆气候差异
```

从太空：

```text
球形大气
全球云层
海洋高光
昼夜分界
天气尺度结构
```

三种尺度必须属于同一个世界。

---

# 102. 最终系统逻辑

```text
                 SUN
                  │
          Solar Insolation
                  │
                  ▼
          Surface Temperature
                  │
                  ▼
              Pressure
                  │
                  ▼
                Wind
          ┌───────┴────────┐
          │                │
      Evaporation      Heat transport
          │
          ▼
       Humidity
          │
          ▼
       CloudWater
          │
          ▼
         Clouds
          │
     ┌────┴────┐
     │         │
  Shadow      Rain
     │         │
     │         ▼
     │       Runoff
     │         │
     │         ▼
     │      Rivers
     │         │
     │         ▼
     │       Ocean
     │         │
     └─────────┴─────→ Evaporation
```

---

# 103. 最终技术判断

ORBIS 后续不应该继续按：

```text
再找一个天气插件
再找一个水插件
再找一个河插件
```

这种方式扩展。

现在已经拥有足够多的成熟参考模块。

下一阶段的重点是：

> **把这些模块降级为“执行器”，把真正的世界状态收回 ORBIS 自己控制。**

---

# 104. 最终分工表

| 系统 | 主要来源 | ORBIS 中的职责 |
|---|---|---|
| Planet Coordinates | ORBIS | 核心 |
| Terrain | ORBIS | 核心 |
| Climate Baseline | World Atlas 思路 | ORBIS 重写 |
| Dynamic Climate | ORBIS | 核心 |
| Volumetric Cloud | UniStorm | 改造后复用 |
| Weather FX | UniStorm | 复用 |
| Atmosphere | ORBIS | 重写 |
| Hydrology | World Atlas | 重写 |
| River Rendering | SW3 | 复用 |
| Ocean Rendering | SW3 | 改造 |
| River Flow | ORBIS | 核心 |
| Wave Detail | SW3 + Seascape 思路 | 复用/增强 |
| Navigation | A* Pro | 局部执行 |
| Global Nav | ORBIS | 自研 |

---

# 105. Astra 最终执行指令

Astra 在后续开发时遵循：

```text
1. 不推翻本文模块边界。
2. 优先建立数据层，再做视觉层。
3. 所有新系统必须支持 PlanetCenter / LocalUp。
4. 不把固定 XZ 平面作为最终方案。
5. Hydrology 优先于 River Shader。
6. Climate 优先于 Weather Type。
7. Cloud coverage 必须来自 Climate。
8. SW3 Flow 必须来自 ORBIS Flow Field。
9. 第三方源码尽量通过 Bridge 扩展。
10. 每次修改必须保持项目可运行。
```

如果某一阶段必须改变本文架构，应先说明：

```text
当前方案问题
替代方案
风险
迁移成本
兼容影响
```

再修改。

---

# 106. 当前立即执行的开发指令

Astra 从这里开始：

```text
Step 1
检查现有 ORBIS 的 Planet / Terrain / Floating Origin 架构。

Step 2
创建：
Assets/ORBIS/Hydrology/

Step 3
实现静态 Hydrology Prototype：

PriorityFlood
D8
MFD
FlowAccumulation

Step 4
增加 Debug Texture：

FlowDirection
Accumulation
RiverMask

Step 5
提取 RiverGraph。

Step 6
RiverGraph → Spline。

Step 7
用 Spline carve terrain。

Step 8
计算 Width / Depth / Velocity。

Step 9
生成 FlowMap。

Step 10
接入 Stylized Water 3 River Material。
```

第一轮不要同时动：

```text
Cloud
Atmosphere
Navigation
```

先完成完整的：

```text
Terrain
→
Hydrology
→
River
→
SW3
```

链路。

完成后再进入：

```text
Climate
→
Cloud
→
Rain
→
Hydrology dynamic coupling
```

---

# 107. 一句话路线

> **先让星球拥有真正的“水循环骨架”，再让天气、云、海洋和 AI 围绕这套世界状态工作。**
---

# 108. 全球地图投影与实际游戏世界投影决策

本节为 ORBIS 当前正式投影技术决策，Astra 后续实现 Planet / Map / Climate / Hydrology / UI 时必须遵循。

## 108.1 结论

ORBIS 不使用 Mollweide、Eckert IV、Dymaxion、AuthaGraph 中任何一种作为最终 3D 游戏世界底层坐标。

最终采用：

```text
真实世界底层
=
Sphere / Cube Sphere

玩家附近实际游玩
=
Local Tangent Plane

全球 2D 世界地图
=
AuthaGraph

面积/气候科研统计
=
Eckert IV / Mollweide

特殊战略展开视图
=
Dymaxion
```

也就是说：

```text
                 ORBIS Planet State
                        🌍
                         │
                Sphere / Cube Sphere
                         │
             ┌───────────┴───────────┐
             │                       │
      Local Gameplay            Global Map UI
             │                       │
     Local Tangent Plane          AuthaGraph
             │
       玩家附近近似平面
```

---

# 109. 为什么不能直接使用全球地图投影作为游戏世界

Mollweide、Eckert IV、Dymaxion、AuthaGraph 都属于全球地图表达方式。

它们适合：

```text
世界地图
统计
战略界面
气候图
人口图
文明图
```

但不适合作为 ORBIS 的最终地表几何空间。

原因包括：

```text
角度变形
距离变形
方向变形
局部尺度变化
投影接缝
高纬畸变
面片边界
```

如果直接把全球投影当作实际世界：

```text
河流长度
风向
导航
物理距离
海浪传播
地形尺度
```

都会受到投影畸变影响。

因此：

> 物理世界始终保留在球体上，地图投影只是 View / Representation。

---

# 110. ORBIS 实际世界底层：Cube Sphere

推荐 Planet Surface 使用：

```text
Cube Sphere
```

或者兼容现有 Terrain 系统的等价球面分块方案。

优点：

```text
适合 Chunk / Quadtree LOD
适合 GPU
高纬不会像经纬度网格那样严重聚点
方便六面纹理
适合 Climate Grid
适合 Hydrology Tile
适合 Planet Streaming
```

Cube Sphere 主要负责：

```text
Planet Geometry
Terrain Tile
Climate Tile
Hydrology Tile
Ocean Tile
Global Sampling
```

---

# 111. Local Tangent Plane

玩家地表附近不直接以“球面弯曲坐标”进行普通 Gameplay。

而是以玩家当前位置建立：

```text
LocalUp
LocalEast
LocalNorth
```

构成局部切平面。

定义：

```text
LocalUp =
normalize(WorldPosition - PlanetCenter)
```

然后建立：

```text
East
North
```

---

# 112. 局部游玩体验

在玩家附近：

```text
几百米
几公里
几十公里
```

世界可表现为近似平面。

因此：

```text
角色移动
建筑
道路
NavMesh
河流局部 Mesh
树木
物理
```

可以使用熟悉的局部直角坐标。

这不会意味着星球本身变成平面。

真实数据仍绑定球面位置。

---

# 113. 地面到太空连续过渡

ORBIS 的目标：

```text
Ground
  ↓
Low Altitude
  ↓
Aircraft
  ↓
Upper Atmosphere
  ↓
Orbit
  ↓
Space
```

不发生世界切换。

推荐：

```text
低高度
Local Tangent 表现占主导

高度增加
逐渐显示真实 Planet Curvature

高空 / 轨道
完全 Sphere
```

重要：

> 不允许从一张 2D 投影地图突然 Morph 成星球作为主要游戏世界方案。

地图投影与 3D Planet 是两个不同 View。

---

# 114. Flat ↔ Sphere Projection Blend

若现有 ORBIS 已有 FlatWorld 系统，则使用连续 Projection Blend。

定义：

```text
P_sphere = 球面真实位置
P_flat   = 当前区域局部切平面位置
```

最终：

\[
P = lerp(P_{flat}, P_{sphere}, B)
\]

其中：

```text
B = ProjectionBlend
```

可以根据：

```text
Camera Altitude
Camera Distance
Gameplay Mode
```

控制。

---

# 115. Projection Blend 必须全系统一致

以下所有几何必须共享同一 Projection Blend：

```text
Terrain
Ocean
River
Lake
Vegetation
Road
Building
Cloud shadow receiver
Navigation debug
```

禁止：

```text
Terrain 已进入 Sphere
Water 仍是 Flat

River 已 Sphere
Bridge 仍 Flat
```

否则会直接产生错位。

---

# 116. AuthaGraph 的正式用途

四种候选中：

> AuthaGraph 作为 ORBIS 默认全球 2D World Map UI 投影。

用于：

```text
全球世界地图
天气地图
气候地图
国家 / 文明地图
人口地图
资源地图
外交地图
河流总览
航线
全球事件
```

原因：

```text
全球大陆视觉连续性较好
更适合作为矩形 UI
适合世界级信息展示
比传统椭圆投影更适合作为游戏地图界面
```

但 AuthaGraph：

```text
只负责 Map View
```

不能成为：

```text
Gameplay physics space
```

---

# 117. AuthaGraph 数据来源

所有 AuthaGraph 地图信息必须来自真正的 Planet State。

流程：

```text
Sphere/CubeSphere position
        ↓
AuthaGraph projection
        ↓
2D map coordinate
```

例如：

```text
Climate Cell
River Node
City
Cloud Coverage
Army
```

都先存在真实球面位置。

然后投影到 AuthaGraph。

不要反过来：

```text
AuthaGraph XY
→ 当成真实世界位置
```

---

# 118. Eckert IV 的用途

Eckert IV 保留作为：

```text
Climate Debug
Biome statistics
Vegetation coverage
Desert percentage
Snow coverage
Ocean / land area
Population density statistics
```

原因：

```text
Equal-area
```

当开发者需要回答：

```text
沙漠到底占星球多少面积？
森林面积是否过高？
全年积雪面积多少？
```

等积投影更有意义。

---

# 119. Mollweide 的用途

Mollweide同样属于等积投影。

推荐用于：

```text
科研式 Debug
全球气候输出
Heatmap
降水总量图
温度异常图
遥感风格界面
```

不作为玩家默认 World Map。

---

# 120. Dymaxion 的用途

Dymaxion 作为可选战略视图。

推荐：

```text
星球展开
战争战略
文明分布
洲际联系
资源战略
科幻 UI
```

可以作为：

```text
特殊 Map Mode
```

而不是普通 Gameplay。

---

# 121. 投影职责表

| 用途 | 正式方案 |
|---|---|
| 真实 Planet 几何 | Sphere / Cube Sphere |
| 全球物理 | Sphere |
| 玩家附近 Gameplay | Local Tangent Plane |
| Floating Origin | Local Tangent + double world coordinates |
| 地面 → 太空 | Flat/Sphere continuous projection |
| 默认全球地图 UI | AuthaGraph |
| 面积统计 | Eckert IV |
| 科研气候图 | Mollweide / Eckert IV |
| 战略展开图 | Dymaxion |
| Climate Grid | Cube Sphere |
| Hydrology Grid | Cube Sphere / planet surface tiles |
| Navigation | Local Tangent |
| Ocean Wave coordinates | Local Planet Tangent |
| River Flow coordinates | Local Planet Tangent |

---

# 122. Climate Grid 与地图投影必须分离

Climate Simulation 不直接运行在 AuthaGraph 上。

推荐：

```text
Cube Sphere Climate Grid
```

负责真实模拟。

AuthaGraph 只读取：

```text
ClimateGrid
```

然后渲染。

原因：

```text
AuthaGraph 是表示层
Cube Sphere 是模拟层
```

这样：

```text
Wind
Humidity
Temperature
Pressure
```

不会受 2D 地图投影畸变污染。

---

# 123. Hydrology 同样不能跑在 AuthaGraph 上

河流生成必须在：

```text
真实 Planet Surface
```

或者局部近似切平面 Tile 上执行。

流程：

```text
Planet Terrain Height
        ↓
Priority-Flood
        ↓
D8 / MFD
        ↓
River Graph
        ↓
真实球面 River Position
        ↓
AuthaGraph display
```

不能：

```text
AuthaGraph heightmap
↓
直接 D8
```

因为投影会改变距离和方向。

---

# 124. 河流跨 Cube Sphere Tile

Hydrology 必须处理 Tile 边界。

禁止：

```text
每个 Cube Face 独立算河流
```

否则河流到 Face Edge 会断掉。

必须：

```text
Neighbour mapping
Ghost cells / border cells
Cross-face routing
```

保证：

```text
D8 / MFD
```

可以跨 Cube Face。

---

# 125. Wind 跨 Cube Sphere

Wind 存储为：

```text
Local tangent vector
```

不是固定：

```text
world XZ vector
```

每个 Climate Cell 保存：

```text
East/North basis
```

或者能够从球面位置计算 basis。

这样 Wind 可以跨 Face 连续。

---

# 126. Ocean Waves 的坐标

SW3 原生很多逻辑倾向：

```text
World XZ
```

ORBIS 改为：

```text
Planet tangent coordinates
```

近玩家：

```text
East/North
```

作为 Wave XY。

这样即使玩家绕星球移动：

```text
海浪不会因为 World XZ 失效
```

---

# 127. River Flow 的坐标

FlowMap：

```text
R = East velocity
G = North velocity
```

而不是：

```text
R = World X
G = World Z
```

Shader 采样后再转换回世界切向方向。

---

# 128. Navigation 的坐标

A* 局部图：

```text
Local Tangent Plane
```

角色 Up：

```text
Planet LocalUp
```

通过：

```text
AIPathAlignedToSurface
```

保持朝向。

全球路线则使用：

```text
Planet Graph
```

不依赖 AuthaGraph XY 距离。

---

# 129. Projection Debug View

新增 Debug：

```text
Projection Debug 0
Final

Projection Debug 1
Local Up

Projection Debug 2
East

Projection Debug 3
North

Projection Debug 4
Cube Face

Projection Debug 5
Cube UV

Projection Debug 6
ProjectionBlend

Projection Debug 7
AuthaGraph UV

Projection Debug 8
Eckert IV UV

Projection Debug 9
Local Tangent distortion
```

---

# 130. 投影验收标准

## Ground

玩家站在地面：

```text
地面视觉平直
物理稳定
导航稳定
水面正常
```

## Aircraft

高空：

```text
曲率逐渐可见
没有 Terrain/Water 分离
```

## Atmosphere

大气高层：

```text
Planet curvature 明显
Cloud shell 正确包围星球
```

## Orbit

```text
完整球形星球
Ocean
Cloud
Atmosphere
River/terrain macro data
```

都处于同一真实位置。

---

# 131. AuthaGraph 验收

世界地图中：

```text
城市
河流
天气
军队
国家
```

投影位置必须和 3D Planet 对应。

点击地图一点：

```text
AuthaGraph UV
↓
inverse projection
↓
Planet surface position
```

能够定位到正确的 3D 星球位置。

---

# 132. Astra 对地图投影的执行指令

Astra 不要尝试把：

```text
Mollweide
Eckert IV
Dymaxion
AuthaGraph
```

之一替换 Planet Core。

实现顺序：

```text
Step 1
确认 Sphere/Cube Sphere 为真实 Planet State。

Step 2
统一 PlanetCoordinateSystem。

Step 3
实现 Local Tangent Plane。

Step 4
把 Terrain / Water / Navigation 接到同一 Local Frame。

Step 5
实现 ProjectionBlend。

Step 6
确认 Ground → Orbit 连续。

Step 7
再实现 AuthaGraph Map Projection。

Step 8
增加 AuthaGraph inverse mapping。

Step 9
增加 Eckert IV / Mollweide 作为 Debug。

Step 10
Dymaxion 仅作为后续可选战略 UI。
```

---

# 133. 更新后的第一优先级顺序

原 Phase 1 的 Planet Coordinate Core 升级为：

```text
P0
Sphere / Cube Sphere
PlanetCoordinateSystem
Local Tangent Frame
Floating Origin
ProjectionBlend
```

之后才能进入：

```text
Climate
Hydrology
Water
Cloud
Navigation
```

原因：

> 这些系统全部依赖同一个 Planet Space。

如果底层投影后改，后续几乎所有模块都要返工。

---

# 134. 更新后的核心技术路线

```text
                       TRUE PLANET
                       Cube Sphere
                           │
               Planet Coordinate System
                           │
                 Local Tangent Frames
                           │
           ┌───────────────┼────────────────┐
           │               │                │
        Climate         Hydrology          Ocean
           │               │                │
           │           River Graph          │
           │               │                │
           └───────────────┼────────────────┘
                           │
                       WaterState
                           │
                         SW3
                           │
                           ▼
                     Local Gameplay
                           │
                   Projection Blend
                           │
                           ▼
                         Orbit

同时：

TRUE PLANET
    │
    └── Projection
           ├── AuthaGraph → 默认全球地图
           ├── Eckert IV  → 面积统计
           ├── Mollweide  → 科研气候图
           └── Dymaxion   → 战略展开 UI
```

---

# 135. 更新后的一句话路线

> **ORBIS 的世界永远是球；玩家脚下只是这个球的局部切平面；AuthaGraph 是看世界的地图，不是世界本身。**
