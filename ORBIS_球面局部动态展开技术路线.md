# ORBIS 球面—局部平面动态展开技术路线

## 0. 核心结论

ORBIS 不再尝试把整个星球永久展开成一张完整平面地图。

新的核心原则是：

> **球体是唯一真实世界；平面只是玩家当前可见区域的动态局部展开。**

所有地形、河流、道路、城市、单位、天气、云、温度、洋流、寻路等数据始终保存在真实球面坐标中。

平面视图只负责把玩家当前视角附近的一部分球面映射到局部平面。

这套架构同时解决：

- 全球投影扭曲
- 两极严重拉伸
- 180° 经线 seam
- 东西边界 wrap
- 河流跨投影断裂
- 城市/道路距离失真
- Pathfinding seam
- 天气场 seam
- 球体 → 2D 过渡割裂
- 过渡期间无法移动地图
- 镜头穿过云层后 2D 视图失去云层表现的问题

---

# 1. 总体设计

ORBIS 世界分成两层：

```text
真实世界层
Planet / Sphere Space
        │
        ├─ Terrain
        ├─ Ocean
        ├─ River
        ├─ Climate
        ├─ Cloud
        ├─ City
        ├─ Road
        ├─ Unit
        └─ Pathfinding

显示层
Projection / Render Space
        │
        ├─ Globe
        ├─ Curved Regional View
        ├─ Local Flat View
        └─ Strategic Atlas
```

关键原则：

```text
Simulation != Projection
```

模拟永远运行在球面。

投影只改变玩家看到什么。

---

# 2. 球体作为唯一真实坐标

建议 ORBIS 核心坐标：

```csharp
public struct PlanetPosition
{
    public double3 unitSpherePosition;
    public double altitude;
}
```

也可以提供经纬度接口：

```csharp
public struct GeoPosition
{
    public double longitude;
    public double latitude;
    public double altitude;
}
```

但内部推荐统一转换成：

```text
double3 unitSpherePosition
```

优点：

- 无 180° 经线断点
- 极区计算自然
- Great Circle Distance 简单
- 天气与光照直接使用球面法线
- 更适合 GPU / Burst / Jobs
- 更适合未来球形世界扩展

---

# 3. 平面不再是世界坐标

过去：

```text
World = Flat XY Map
```

改成：

```text
PlanetPosition
      ↓
Projection Manager
      ↓
Local Render Position
```

玩家看到的是：

```text
X / Y / Height
```

但真实位置仍然是：

```text
PlanetPosition
```

地图重新展开不会修改世界状态。

---

# 4. Camera-Centered Local Projection

主游戏平面模式采用：

> **Camera-Centered Geodesic Local Projection**

即：

> 摄像机中心球面局部展开

当前玩家/摄像机位置：

```text
Projection Center = C
```

只展开 C 周围玩家能看到的区域。

例如：

```text
玩家实际可见半径：800 km
```

系统只需要生成：

```text
约 1000 ~ 1500 km 半径
```

的局部平面。

完全没有必要同时展开整颗星球。

---

# 5. 球面 Log Map

设：

```text
C = Projection Center
P = 任意球面点
```

球面夹角：

```text
theta = acos(dot(C, P))
```

真实地表距离：

```text
distance = PlanetRadius * theta
```

在 C 上建立局部切空间：

```text
Up    = C
East  = tangent east
North = tangent north
```

然后根据球面方向与弧长映射：

```text
Planet Position
      ↓
Geodesic Direction
      ↓
Arc Distance
      ↓
Local X / Y
```

这样中心附近：

- 距离接近真实
- 方向接近真实
- 河流形状自然
- 城市间距离自然
- 地形不会突然拉伸

---

# 6. 不使用普通正交压平

禁止简单：

```text
Sphere XYZ
   ↓
Orthographic Projection
```

因为远离中心后会产生：

- 距离压缩
- 高纬度比例变化
- 河流长度错误
- 道路尺度错误
- 地形密度异常

ORBIS 局部展开应该基于：

```text
球面弧长
+
球面方位角
```

---

# 7. Dynamic Projection Recenter

玩家不能真正走到地图边缘。

建议：

```text
Local Projection Radius = 1500 km
Comfort Radius          = 800 ~ 1000 km
Recenter Threshold      = 250 ~ 400 km
```

当玩家偏离中心：

```text
Old Projection Center
        ↓
Player Moves
        ↓
New Projection Center
```

系统平滑重定位投影中心。

玩家视觉上几乎不应该察觉地图被重新展开。

---

# 8. Spherical Floating Origin

ORBIS 应升级普通 Floating Origin：

```text
Unity Floating Origin
```

为：

> **Spherical Floating Origin**

结构：

```text
Planet Double Space
        │
        ├─ Geodesic Origin Shift
        │
        └─ Projection Origin Shift
```

## Geodesic Origin Shift

负责：

- 星球级 double 精度
- 超大地图
- 长距离移动
- Unity float 精度问题

## Projection Origin Shift

负责：

- 当前局部地图
- 摄像机附近 float 渲染
- 平面中心动态重定位

---

# 9. 不再存在固定地图边界

新架构中主游戏没有：

```text
West Edge
East Edge
North Edge
South Edge
```

只有：

```text
Current Projection Window
```

玩家向任何方向移动：

```text
Player Moves
     ↓
Projection Window Moves
```

世界永远继续存在。

---

# 10. Globe → Flat 连续过渡

不再把：

```text
3D Globe
```

和：

```text
2D Map
```

做成两个独立世界。

每个顶点同时拥有：

```text
spherePosition
flatPosition
```

Shader：

```hlsl
float3 finalPosition =
    lerp(
        spherePosition,
        flatPosition,
        flattenAmount
    );
```

其中：

```text
flattenAmount = 0.00 → 完全球体
flattenAmount = 0.25 → 大曲面
flattenAmount = 0.50 → 区域弧面
flattenAmount = 0.75 → 接近平面
flattenAmount = 1.00 → 完全局部平面
```

---

# 11. Projection Curvature

建议取消内部固定模式：

```text
GlobeMode
FlatMode
```

改成统一参数：

```text
ProjectionCurvature
```

例如：

```text
1.0  Globe
0.75 Large Curved Region
0.50 Regional Curved Map
0.25 Near Flat
0.0  Local Flat
```

形成：

```text
Space
  ↓
Planet
  ↓
Globe
  ↓
Curved Regional
  ↓
Regional Flat
  ↓
Local Gameplay
```

---

# 12. 过渡期间保持完全可操作

球体压平过程中：

- 玩家可以移动
- 可以拖动地图
- 可以旋转
- 可以缩放
- 单位继续运动
- 河流继续更新
- 海洋继续模拟
- 天气继续运行
- 云层继续运动

流程：

```text
Input
  ↓
Update Planet Position
  ↓
┌─────────────────┐
│ Sphere Position │
│ Flat Position   │
└─────────────────┘
        ↓
      Blend
```

禁止：

```text
开始转换
↓
冻结世界
↓
转换结束
↓
恢复控制
```

---

# 13. 云层系统：云只存在于大气层视角

ORBIS 不再在 Flat Gameplay 中显示 2D 云层。

新的原则是：

> **云的可见几何只属于大气层与高空视角；镜头穿过云层进入近地表后，云体本身完全消失，但云影和天气影响继续存在。**

这样可以避免：

- 地表模式被云遮挡
- 2D 云层显得像贴图
- 球体体积云切换成平面云时产生违和
- 单位、城市、道路和边界信息被遮挡
- 玩家进入地表后仍然感觉自己“隔着一层云看地图”

---

# 14. Cloud Simulation 与 Cloud Visibility 分离

云的物理模拟依然全局运行：

```text
Global Cloud Simulation
        │
        ├─ Humidity
        ├─ Pressure
        ├─ Wind
        ├─ Temperature
        ├─ Condensation
        ├─ Cloud Water
        ├─ Cloud Density
        └─ Precipitation
```

这些数据始终存在于真实球面和大气系统中。

但是：

```text
Cloud Simulation != Visible Cloud Geometry
```

进入地表后只是关闭云的可见渲染，不关闭云的模拟。

---

# 15. 云层显示状态

建议根据摄像机高度与云层关系控制：

```text
Space / High Atmosphere
→ 完总体积云

Above Cloud
→ 完整体积云

Inside Cloud
→ 体积云 + 雾化 / 消光

Below Cloud
→ 云体快速淡出

Near Surface
→ 不显示云体

Flat Gameplay
→ 不显示云体
```

最终：

```text
Visible Cloud Geometry = 0
```

但仍保留：

```text
Cloud Shadow
Rain / Snow
Solar Attenuation
Weather Effects
Temperature Influence
Humidity
Storm State
```

---

# 16. 穿云进入地表的过渡

建议流程：

```text
Space
 ↓
Above Cloud
 ↓
Enter Cloud
 ↓
Inside Cloud
 ↓
Exit Cloud Base
 ↓
Cloud Geometry Fade Out
 ↓
Near Surface
 ↓
Ground / Flat Gameplay
```

云层透明度：

```text
Above Cloud
1.0

Inside Cloud
1.0

Just Below Cloud Base
0.6

Near Surface
0.2

Ground View
0.0
```

不要在进入 2D 地表后切换成另一套 2D 云。

也就是说：

```text
Volumetric Cloud
      ↓
Fade Out
      ↓
No Visible Cloud
```

而不是：

```text
Volumetric Cloud
      ↓
2D Cloud Overlay
```

---

# 17. 地表模式保留云影

虽然云体不可见，但云影必须保留。

流程：

```text
Global Cloud Density
        ↓
Sun Direction
        ↓
Cloud Shadow Projection
        ↓
Terrain / Ocean / City
```

因此玩家在地表可以看到：

```text
阳光
 ↓
大片云影扫过地面
 ↓
光照变暗
 ↓
再次变亮
```

这能让玩家明确感受到天空中仍然有云，而不需要真的把云画在屏幕上。

---

# 18. 云影与天气系统继续影响环境

Cloud Shadow 不只是视觉效果。

建议继续驱动：

```text
Solar Radiation
      ↓
Surface Heating
      ↓
Temperature
      ↓
Evaporation
      ↓
Humidity
```

因此：

```text
厚云区
→ 地表短波辐射降低
→ 地表升温减弱

晴空区
→ 太阳辐射增强
→ 地表升温增强
```

同时保留：

```text
Rain
Snow
Fog
Storm Darkness
Wet Surface
Lightning
Wind
```

所以地表虽然看不到云体，仍然能够看到完整天气结果。

---

# 18.1 地表天气视觉

Ground / Flat Gameplay 推荐使用：

```text
Cloud Geometry       = Off
Cloud Shadow         = On
Rain / Snow          = On
Fog / Mist           = Conditional
Sun Attenuation      = On
Wetness              = On
Lightning            = On
Wind                  = On
Ambient Lighting     = Weather Driven
```

这会形成：

```text
天空云层
    ↓
玩家穿过云
    ↓
进入地表
    ↓
不再显示云
    ↓
地面仍然存在移动云影
    ↓
天气和光照仍持续变化
```

---

# 18.2 与局部平面展开的关系

Flat Gameplay 不再需要：

```text
Projected 2D Cloud Renderer
```

Projection Manager 只需要继续提供：

```text
Cloud Shadow Projection
Weather Sampling
Solar Attenuation
```

因此局部展开系统更简单：

```text
Sphere Cloud Simulation
        ↓
Cloud Shadow Field
        ↓
Local Projection
        ↓
Ground Shadow
```

不需要把云本身重新投影到平面。

---

# 18.3 最终云层设计原则

ORBIS 云系统最终遵循：

### Rule A

```text
Clouds belong to the atmosphere.
```

云体属于大气层，不属于地表地图。

### Rule B

```text
Below the cloud layer, cloud geometry disappears.
```

镜头穿过云层以后关闭云体显示。

### Rule C

```text
Cloud shadows survive the transition.
```

进入地表后仍然保留云影。

### Rule D

```text
Weather simulation never stops.
```

隐藏云体不能影响天气模拟。

### Rule E

```text
Ground view communicates clouds through lighting and weather.
```

地表通过云影、降雨、雾、光照和环境变化表达天空云况。

---

# 19. 河流系统

河流真实数据：

```text
RiverNode[]
```

每个节点：

```text
unitSpherePosition
elevation
width
depth
flow
sediment
```

河流真实形态：

```text
Spherical River Spline
```

显示：

```text
Sphere River
   ↓
Local Projection
   ↓
Local River Spline
   ↓
River Mesh
   ↓
Crest / Stylized Water
```

因此避免：

- 投影拉长
- 河宽变化
- seam 断裂
- 河口错位

---

# 20. Ocean / Crest / Stylized Water

水体系统：

```text
Ocean
River
Lake
Wetland
```

真实地理范围全部基于 Planet Space。

水插件只负责当前可见局部区域。

```text
Planet Water Data
       ↓
Local Projection
       ↓
Visible Water Mesh
       ↓
Crest / Stylized Water
```

不要求插件本身处理全球投影。

---

# 21. WorldAtlas 接入

WorldAtlas 负责加强：

- 高程生成
- 山脉
- 河流
- 湖泊
- 流域
- 地理结构
- 水系

但生成结果最终统一转换：

```text
WorldAtlas
    ↓
Geography Conversion
    ↓
Planet Coordinates
    ↓
ORBIS Planet Database
```

不要让 WorldAtlas 的平面坐标成为 ORBIS 的最终真实世界坐标。

---

# 22. 天气与气候

全部运行在 Sphere Space：

```text
Wind
Humidity
Pressure
Temperature
Cloud
Precipitation
Solar Heating
Greenhouse Effect
Ocean Current
```

太阳照射直接使用：

```text
dot(surfaceNormal, sunDirection)
```

保证：

```text
白天升温
夜间降温
纬度差异
季节差异
地形影响
温室效应
```

不会因为当前地图投影改变。

---

# 23. Pathfinding

全局 Pathfinding 图使用：

```text
Planet Navigation Graph
```

节点：

```text
Sphere Position
```

距离：

```text
Great Circle Distance
```

或者局部 geodesic distance。

当前 Flat Projection 只负责显示。

这样天然解决：

- 日期线跨越
- 极地路径
- seam
- 投影中心变化

---

# 24. 单位系统

单位真实位置：

```text
PlanetPosition
```

显示位置：

```csharp
Vector3 renderPosition =
    projectionManager.PlanetToRender(
        unit.PlanetPosition
    );
```

重新投影：

```text
Unit Planet State
不改变

Unit Render Position
重新计算
```

---

# 25. 城市、道路、边界

城市：

```text
PlanetPosition
```

道路：

```text
Spherical Polyline
```

国境：

```text
Spherical Polygon
```

渲染：

```text
Planet Geometry
      ↓
Projection Manager
      ↓
Render Geometry
```

---

# 26. 两极

玩家到达北极：

```text
Projection Center = North Pole
```

系统重新建立局部 tangent basis。

北极不再是地图顶部边缘。

南极同理。

因此极区可以正常：

- 移动
- 战斗
- 建城
- 河流显示
- 天气模拟

---

# 27. 日期变更线

不再把：

```text
+180°
-180°
```

作为真正空间边界。

内部使用 Sphere Vector 后：

```text
日期线
```

只剩 UI 上的经度表达问题。

游戏世界本身连续。

---

# 28. Terrain Patch

局部世界建议拆成 Patch：

```text
┌─────────┬─────────┬─────────┐
│ Patch   │ Patch   │ Patch   │
├─────────┼─────────┼─────────┤
│ Patch   │ Player  │ Patch   │
├─────────┼─────────┼─────────┤
│ Patch   │ Patch   │ Patch   │
└─────────┴─────────┴─────────┘
```

每块保存：

```text
Sphere Bounds
Terrain Data
Biome
Hydrology
River
Road
LOD
Projection State
```

测试尺寸：

```text
64 km
128 km
256 km
```

根据性能决定最终值。

---

# 29. LOD

摄像机附近：

```text
High Terrain LOD
High River LOD
High Water LOD
High City LOD
High Vegetation LOD
```

远处：

```text
Simplified Geometry
Simplified River
Reduced Vegetation
Reduced City Mesh
```

更远：

```text
Curved Regional Representation
```

最终：

```text
Globe Representation
```

---

# 30. 推荐视距

初始参考：

```text
0 ~ 100 km
Local Gameplay

100 ~ 300 km
Local Flat

300 ~ 800 km
Regional Flat

800 ~ 1500 km
Large Regional Flat

1500 ~ 3000 km
Curved Regional

3000 km+
Globe
```

实际参数由：

- 星球半径
- Camera FOV
- 屏幕尺寸
- gameplay scale

共同决定。

---

# 31. Markley’s Tetrahedral Map 的新定位

Markley 不再负责主游戏平面世界。

它更适合作为：

> **Global Strategic Atlas**

负责：

- 整颗星球一览
- 全球政治
- 全球军事
- 人口
- 气候
- 贸易
- 资源
- 外交
- Debug
- 世界编辑器

架构：

```text
Planet Database
      │
      ├─ Local Dynamic Projection
      │          ↓
      │     Main Gameplay
      │
      └─ Markley Atlas
                 ↓
           Global Overview
```

结论：

```text
Local Dynamic Projection = 玩游戏
Markley Atlas            = 看全球
```

---

# 32. Projection Manager

建议新增：

```text
OrbisProjectionManager
```

职责：

```text
Projection Center
Tangent Basis
Planet → Flat
Flat → Planet
Sphere → Render
Curvature
Flatten Blend
Recenter
Heading
LOD
```

建议 API：

```csharp
PlanetPosition ScreenToPlanet(Vector2 screenPosition);

Vector3 PlanetToRender(PlanetPosition position);

Vector3 PlanetToFlat(PlanetPosition position);

Vector3 PlanetToSphere(PlanetPosition position);

void SetProjectionCenter(PlanetPosition center);

void SetProjectionCurvature(float curvature);

void RecenterIfNeeded();
```

---

# 33. Shader Global Data

建议统一：

```text
_PlanetRadius
_ProjectionCenter
_LocalEast
_LocalNorth
_LocalUp
_ProjectionCurvature
_FlattenAmount
_CameraPlanetPosition
_CloudProjectionWeight
_VolumetricCloudWeight
```

供：

- Terrain
- River
- Road
- City
- Border
- Vegetation
- Cloud
- Water

共同使用。

---

# 34. GPU 路线

后期应将：

```text
Planet → Flat
```

大量放到：

```text
Vertex Shader
Compute Shader
```

CPU 保存：

```text
Planet Data
```

GPU 负责：

```text
Projection
LOD
Visible Patch
Vertex Transform
```

优势：

- Recenter 成本低
- 不必 CPU 重建全图
- Globe ↔ Flat 顺滑
- 支持 GPU Driven Rendering

---

# 35. 精度

Simulation：

```text
double
double3
```

Rendering：

```text
float
float3
```

流程：

```text
Double Planet Position
        ↓
Relative Position
        ↓
Float Render Position
```

不要把整颗星球直接放进 Unity float World Space。

---

# 36. 最终 ORBIS 坐标架构

```text
                 ORBIS PLANET
                       │
               Double Planet Space
                       │
      ┌────────────────┼────────────────┐
      │                │                │
   Terrain          Climate          Entity
      │                │                │
      └────────────────┼────────────────┘
                       │
             Projection Manager
                       │
              Projection Anchor
                       │
             Geodesic Local Map
                       │
      ┌────────────────┼────────────────┐
      │                │                │
    Globe         Curved Map        Local Flat
      │                │                │
      └────────────────┴────────────────┘
                       │
                    Renderer
```

云层则：

```text
                Cloud Simulation
                       │
       ┌───────────────┴───────────────┐
       │                               │
Volumetric Cloud                Projected Cloud
       │                               │
     Globe                         Flat Map
       └───────────────┬───────────────┘
                       │
                    Blend
```

---

# 37. 开发阶段

## Phase 1 — Planet Coordinate Core

完成：

```text
PlanetPosition
double3 Sphere
Geo ↔ Sphere
Great Circle Distance
Tangent Basis
```

---

## Phase 2 — Projection Manager

完成：

```text
Sphere → Local Flat
Local Flat → Sphere
Projection Center
Camera Centered Projection
Recenter
```

---

## Phase 3 — Globe / Flat Morph

完成：

```text
Sphere Position
Flat Position
Curvature
Flatten Shader
```

要求转换期间仍可以操作地图。

---

## Phase 4 — Terrain Patch

完成：

```text
Patch Sphere Bounds
Projection
LOD
Streaming
Floating Origin
```

---

## Phase 5 — Rivers / Roads / Cities

把：

```text
River
Road
City
Border
```

全部切换为 Planet Space 数据。

---

## Phase 6 — Cloud Dual Representation

完成：

```text
Global Cloud Field
Volumetric Renderer
Projected 2D Cloud Renderer
Cloud Crossfade
Cloud Shadow
```

重点测试：

```text
Space
→ 穿过云层
→ 地表
→ Curved Map
→ Flat Map
```

云的位置、运动和天气状态必须连续。

---

## Phase 7 — Weather / Climate

保证：

```text
Wind
Temperature
Humidity
Cloud
Solar Heating
Greenhouse Effect
```

完全独立于地图投影。

---

## Phase 8 — Markley Atlas

最后再把 Markley 接入：

```text
Global Strategic View
```

不要让 Markley 反过来限制主游戏坐标架构。

---

# 38. 最终设计原则

ORBIS 后续所有模块都必须遵守以下规则：

### Rule 1

```text
Sphere is Truth.
```

球面是真实世界。

### Rule 2

```text
Projection is Presentation.
```

投影只负责表现。

### Rule 3

```text
Never store gameplay state in flat projection coordinates.
```

核心 gameplay 数据禁止永久保存在局部平面坐标中。

### Rule 4

```text
The camera defines the local map.
```

局部地图跟随摄像机/玩家，而不是玩家在固定地图上移动到边界。

### Rule 5

```text
Simulation remains continuous during projection transitions.
```

投影转换不能暂停世界。

### Rule 6

```text
Cloud simulation is global; cloud rendering is view-dependent.
```

云的模拟统一，表现根据 Globe / Curved / Flat 模式变化。

---

# 39. 最终效果

玩家体验：

```text
太空
 ↓
完整星球
 ↓
靠近星球
 ↓
看到真实体积云
 ↓
穿过云层
 ↓
地表开始展开
 ↓
体积云逐渐弱化
 ↓
穿过云底后云体淡出
 ↓
仅保留云影与天气影响
 ↓
地图逐渐变平
 ↓
完全平面游戏
```

整个过程中：

```text
没有切场景
没有世界重载
没有固定地图边界
没有投影 seam
没有极区爆炸
云体在穿过云底后按高度自然淡出，同时保留云影与天气
```

ORBIS 最终不再是：

> 一个球体模式 + 一个独立 2D 地图。

而应该成为：

> **一个连续可变曲率、以真实球面为核心、根据玩家视角动态展开的行星世界。**
