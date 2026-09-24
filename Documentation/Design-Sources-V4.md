# V4 设计来源与取舍

## 成熟的排水和地形思路

Barnes、Lehman、Mulla 的 **Priority-Flood: An Optimal Depression-Filling and Watershed-Labeling Algorithm for Digital Elevation Models** 提供低处优先处理洼地、构造排水关系的经典思路，讨论过不同邻接结构。本工程 V3 已有优先队列排水；V4 保留这部分，并将「排水拓扑」和「可见河道路径」拆开。导入中心线的连通分量也采用低处优先的出流取向。

来源：https://arxiv.org/abs/1511.04463

Red Blob Games 的 **Making maps with noise functions** 展示了多频噪声、山脊和坐标扭曲等地形构造方法。V4 在已有球面板块高度上加入少量保守热松弛和三维域扭曲细节；没有把教程的二维纹理坐标直接套到球体上，也没有声称引入一个完整工业地形中间件。

来源：https://www.redblobgames.com/maps/terrain-from-noise/
相关设计实验：https://www.redblobgames.com/x/1728-elevation-control/

Unity 官方高度图说明用于区分高度数据与颜色纹理，以及高度图导入的边界。本工程没有使用 Unity Terrain 的平面组件去冒充球面地表，而是把栅格采样送入已有球面网格和近景补丁。

来源：https://docs.unity3d.com/6000.0/Documentation/Manual/terrain-Heightmaps.html

## EU4 借鉴的是数据组织与定位，不是完整兼容

使用高度图和河流图分别表达高程与河网定位，避免仅由渲染网格中心推测河道。参考作者提供的 EU4 地图工具 **MagellanEU4** 以及地图教程，对图层分离的工作流进行研究。

工具作者仓库：https://github.com/Graapefruit/MagellanEU4
地图教程作者：https://www.bilibili.com/opus/972941471336366081

官方 EU4 map-modding wiki 地址已尝试访问，但本次读取遭遇访问限制，不能据此声称已经逐条核验了官方河流调色板规范：
https://eu4.paradoxwikis.com/Map_modding

因此 V4 明确采用自己的约定：2:1 等经纬图、线性灰度高度、蓝色中心线及亮度提示。支持 8 位索引 BMP 的图像解码，不等于完整支持 EU4 的地图尺寸、调色板索引语义、源点/合流规则、省份拓扑或分汊算法。将 EU4 工程文件直接拖入，不保证兼容。

## 2D 图形和编辑器接口

2D 建筑／人物采用少量 Mesh 批量绘制摄像机朝向的透明裁切面片；地理和文明数据仍属三维世界的 Tile。附图是程序生成的占位素材，不是 EU4 或 WorldBox 素材。

导入窗口沿用项目 UI Toolkit 方向，编辑器文件选择使用官方 `EditorUtility.OpenFilePanelWithFilters`；验收菜单使用 `ShaderUtil.ShaderHasError` 读取着色器当前错误状态。仅读取该状态不代表所有平台／所有变体都通过编译。

来源：https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorUtility.OpenFilePanelWithFilters.html
来源：https://docs.unity3d.com/cn/6000.0/ScriptReference/ShaderUtil.ShaderHasError.html

原有云、水体和大气的第三方归属继续保留在 `ThirdPartyNotices.md`。本次没有下载第三方游戏资产，没有新增商业 Unity 插件依赖，也没有打包系统字体。
