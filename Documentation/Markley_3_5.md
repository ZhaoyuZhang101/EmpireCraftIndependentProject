# Markley 實施紀錄

最新使用者選擇優先於先前升級 MD 的 AuthaGraph / 多投影方案：三層遊玩顯示統一 Markley，UI 不增加投影切換選項。保留旧技术路线作为历史记录，不再将 AuthaGraph 作为当前目标。

## 資料與顯示

WorldData → 地理位置 → Markley 正投影 → 2D 畫面；反向由同一查表得到球面法向，據此還原地理索引並取樣原世界高程、水文、植被與氣候。球體高度直接取自同一份 VisualTex，不再獨立生成球體地形。全球資料的球面索引與顯示的四向投影分開處理，避免跨界重建地圖或改變存檔 Tile ID。

正投影為 Lee 複數多項式與四面體展開仿射變換；逆投影離線烘焙 1025 × 445 的 RGBAFloat 單位方向紋理。CPU 點選和 GPU 地表／雲柱共用該表；GPU 每次逆映射是一個雙線性取樣再正規化。表的原始大小約 6.96 MiB，CPU 保留浮點陣列，GPU 上傳後釋放 Unity CPU 紋理副本。

離線求解使用立體投影複平面上的阻尼 Newton；在矩形邊界因浮點判定不落入任一面時採微小內縮重試，頂點單獨使用球面座標。`table-info.json` 記錄重試數；未反算成功的像素會直接使烘焙失敗。不能將微小內縮重試說成全解析逆函數。

## 四向覆蓋

矩形宽高比 `4 / sqrt(3)`，归一化坐标为 `(u,v)`。

- `(u+1,v)` 与 `(u,v)` 相同。
- `(u+0.5,v+2)` 与 `(u,v)` 相同。
- 下边界 `(u,0)` 对应 `(0.25-u,0)`；上边界 `(u,1)` 对应 `(0.75-u,1)`。
- 任意行 `row=floor(v)`，`k=floor(row/2)`。偶数行映射到 `(frac(u+k/2), frac(v))`；奇数行映射到 `(frac(0.75-u-k/2),1-frac(v))`。

这不是把原始经纬地图的南北边直接黏成环面。跨界后的旋转与位移是投影的一部分。摄像机坐标用双精度并限制在等价的两行周期内，避免长时间卷动后浮点精度恶化。固定网格副本只改变可见性，二维端点使用四顶点网格。

## 手動驗收

1. Unity 内运行 `ORBIS / Validation / Markley Projection and Shaders`；在 Windows D3D11 下检查 Console，特别是 ScrollSurface、FlatMap 和 Clouds。
2. 选一个明显河口，从球体进入，拖动过渡，再返回；核对实际落点。缩回入场尺度后应开始收回，不先展示整张地图。
3. 持续向四个方向平移，再沿相反方向返回，观察地形、河口、云和选中 Tile 是否连续。上下连接后的地图朝向变化应连续，不加载另一份世界。
4. 在四边附近快速拖动、反转滚轮、调整窗口尺寸，检查副本闪烁及水面双缓冲过渡。
5. 比较云开关：2D 中只应有很轻的变化，球体云仍应清晰；不同天气与昼夜都要检查。当前已通过的是数学／源码测试，视觉质量仍须实际运行验收。
6. Profiler 分别记录球体、展开、总览与详细图。复杂云积分和重复地图的实际成本尚未测量，不提供虚构 FPS。

## 来源与许可

- [Mapshaper Markley / Lee 实作](https://github.com/mbloch/mapshaper/blob/master/src/crs/mapshaper-lee-tetrahedral.mjs)
- [Mapshaper 多面体展开](https://github.com/mbloch/mapshaper/blob/master/src/crs/mapshaper-polyhedral-projection.mjs)
- [d3-geo-polygon Lee 实作](https://github.com/d3/d3-geo-polygon/blob/main/src/tetrahedralLee.js)

修改过的 Mapshaper 源文件和许可证随 `Tools/Markley/source` 提供（MPL-2.0；其 d3 部分 ISC）。`MarkleyProjection.cs` 的对应移植也按 MPL-2.0 提供。供应商素材来自用户上传包，保持原许可，仅为该项目私有使用；不构成公开再分发授权。
