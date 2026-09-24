# ORBIS 天体、体积云与水资源接入

## 在星球视角看到太阳、月亮和星空

素材位于 `Assets/ORBIS/Resources/ORBIS/Celestial`，来自用户提供的
`D:/World Orbis/My project/Assets/SolarSystem`。仅选用了太阳、月亮贴图和
六面星空；来源轨道线预制体和脚本保存在
`Assets/ThirdParty/SolarSystemOrbit`，作为轨道外观参考。天空盒材质的
顶部贴图引用已修正为原包的 `Space_Top.tif`。

ORBIS 的 `VisualSun` 和 `OrbisNightLighting.EvaluateMoon` 继续决定
太阳/月亮的位置与月相，原包的 `PlanetManager`、`FollowOrbit` 和相机
控制器不参与运行。轨道由 ORBIS 日历驱动，星球视角不绘制轨道线。
太阳/月亮外观在原有大气背景合成阶段绘制，因而不会产生第二个太阳；
云的透射率仍覆盖天体圆盘。二维地图保持原来的纯色背景。

## HPVolumeCloud

用户提供的 `HPVolumeCloud-main.zip` 中，`VolumetricClouds.hlsl` 明确
声明为 **HDRP 阅读版、非可编译单元**。参考源码、README 和许可保存在
`Reference/HPVolumeCloud/`。ORBIS 使用
`OrbisHPVolumeCloud.cginc` 改编其 `phi_fwd` 各向同性漫射源积分：
散射沉积、低吸收存活率、扩散核、建立所需的光学厚度、云边界门控。
仍由 ORBIS 的天气层、球形云壳、阴影图集、步数和昼夜系统提供输入；
这不是来源 HDRP 渲染器的逐行移植。源包建议的特殊云型贴图、驱动器、
高空云和 HDRP 回调并未提供为可编译的 Unity 资源。

HanPi Volume Cloud © AshenOneArt
https://github.com/AshenOneArt/HPVolumeCloud

## 三个 Unity 包的使用判断

| 包 | 在当前 Built-in ORBIS 中的做法 |
| --- | --- |
| UniStorm 5.3.4 | 已有形状噪声、细节噪声和 `CloudProfile` 桥接。完整天气控制器会形成第二套时间与天气状态，不接入。 |
| Stylized Water 3 v3.0.1 | 包内 `package.json` 明确依赖 URP 17.0.3；当前仅复用 `StreamWaves`、`OceanWaves` 波纹贴图。完整 Renderer 需先迁移水面管线，不在此次改动范围。 |
| Crest 5.6.3 | 包内有 Built-in、URP、HDRP 分支，技术上可以运行 Built-in；其海洋、河流、深度与反射系统需替换 ORBIS 现有球面水体，建议另设独立水体集成阶段验证。 |

保持 `ProjectSettings/GraphicsSettings.asset` 中 Built-in 管线，不导入
三个包的整套示例场景或项目设置。
