# 夏威夷地形雨核验

观测对照来自夏威夷大学马诺阿分校的《夏威夷降水图集》。Giambelluca, T.W., Q. Chen, A.G. Frazier, J.P. Price, Y.-L. Chen, P.-S. Chu, J.K. Eischeid, and D.M. Delparte, 2013: *Online Rainfall Atlas of Hawai‘i*. Bull. Amer. Meteor. Soc., 94, 313–316. [论文](https://doi.org/10.1175/BAMS-D-11-00228.1) · [图集和资料下载](https://rainfall.geography.hawaii.edu/downloads)

格点与表格资料的使用条款允许非商业复制和分发，展示资料及衍生成果须注明来源。验证脚本读取单独下载的观测文件，游戏运行和发布包只使用 ORBIS 自身的地形与大气数据。[署名与使用条款（页面中的 Conditions of Use）](https://rainfall.geography.hawaii.edu/how-to-cite)

## 对照尺度

图集提供 1978–2007 年的常年月、年估计，原始格距约 234 × 250 米。在北纬 20°，ORBIS 的 2048 × 1024 世界每格约 18.4 × 19.5 公里。脚本按两套格网的球面重叠面积汇总观测，海上缺值不参与平均。误差和相关系数按每格有效陆地面积加权。覆盖率表示图集陆地有多大比例落在模拟陆格或河道格内。

区域降水原有的 160 公里取样跨过小岛，希洛的来风和背风侧容易读到相同的远海高程。新增的 32 公里坡向取样沿八个方位计算湿风迎坡抬升，也保留低山山顶收到的迎坡降水。热带海洋上空的浅云贡献在 1700–2700 米之间逐渐衰减。390 米抬升响应和 58% 浅云权重是模型参数。各大气格原已凝结的月降水总量仍按球面面积守恒。[夏威夷大学对迎风坡与信风逆温的说明](https://rainfall.geography.hawaii.edu/rainfall)

## 地球预设结果

2048 × 1024 地图使用默认种子 `ORBIS-2D-2026`。两次生成使用相同的大气与地形版本。基线只将 `FlatOrographicRainfall.cs` 换成 `d805bb7` 的版本。

| 地点 | 原模拟 mm/年 | 更新后 mm/年 | 图集同格陆地均值 mm/年 |
| --- | ---: | ---: | ---: |
| 希洛，迎风沿岸 | 768 | 4208 | 5283 |
| Waikoloa，背风低地 | 739 | 553 | 336 |
| 茂纳凯亚，格平均高程 2562 米 | 841 | 446 | 634 |

全州 57 个可配对陆格覆盖图集陆地的 90.5%。年雨量平均绝对误差从 1134 降到 971 毫米，均方根误差从 1692 降到 1317 毫米，空间相关系数从 −0.02 升到 0.59。全州年雨量平均仍偏低 588 毫米，科纳的局地海风降水和考艾岛陡坡的极端雨量仍有误差。

1024 宽及以上的地形能在不同地格分辨这三个位置。512 宽时，希洛位于海格，Waikoloa 与山顶共用陆格。256 宽时三处共用海格。海岸精细选择和气候模拟仍有不同的空间分辨率。旧存档保留原来的月常年值，主动重建地形后会应用新的坡向降水。

## 复现

在仓库根目录运行下列命令。将 [StateASCIIGrids_mm.zip](https://atlas.uhtapis.org/rainfall/assets/files/GISLayers/StateASCIIGrids_mm.zip) 放在 `ValidationOutput/Hawaii/`。此次对照的原始 ZIP 的 SHA-256 为 `3ffd75aab1cda8f571161b5c7c8c5882066bb60b2b5b34572f3d4729706742f0`。

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --orographic-rainfall
dotnet run --project Validation/FlatWorld/FlatWorld.Core.Tests.csproj -c Release -- --hawaii-climate 2048
dotnet run --project Validation/EarthClimate/Export.csproj -c Release -- ValidationOutput/Hawaii/earth2048.bin 2048
python Validation/EarthClimate/check_hawaiian_rainfall.py ValidationOutput/Hawaii/StateASCIIGrids_mm.zip ValidationOutput/Hawaii/earth2048.bin
```

传入多份 ECL1 导出即可逐份对照同一图集。程序海岛回归另行检验风向翻转、低峰与高峰的降水差异，以及逐月水量守恒。
