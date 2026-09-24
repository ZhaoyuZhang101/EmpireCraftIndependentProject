# Perf3.3.3 實施紀錄

日期：2026-09-12。基線：Perf3.3.2 VisualRepair。需求：升級路線 v2、卷軸接縫刷新、D3D Shader 錯誤與返回星球尺度。

## 階段邊界

按路線第 90 節逐小模組實施，先交付 P0 座標基礎與現有渲染穩定性修復。不是整份路線或整個 P0 的完成宣告。原始方案存於 `UPGRADE_PLAN_v2.md`；本紀錄的狀態表用來區分已接入與後續工作。

| 路線項目 | 本輪狀態 |
|---|---|
| 單一地形資料來源 | 沿用 WorldData；本輪沒有建立第二套地形、天氣或存檔狀態。 |
| PlanetCoordinateSystem | 已實作 double 座標、球面正反轉換與正交局部基底，接入 navigator。 |
| Floating Origin | 已有 double 原點相減 API，卷軸每次移動後以 double 做週期重定位；完整場景物件、物理和導航原點遷移尚未實作。 |
| Local Tangent Plane | 球面落點共用 ENU 基底已接入；既有 2D 幾何仍是經緯展開，尚非具有真實公尺尺度的局部地形區塊。 |
| ProjectionBlend | 保留地表／地表水共用網格與同一 color/depth 投影函式；球面軸向集中至共用 HLSL。完整物理世界和雲體積的連續變形尚未完成。 |
| Cube Sphere | 未遷移儲存或生成六面獨立水文；跨面鄰接、流向運輸與串流需整體實施。 |
| AuthaGraph | 只確立全球 UI 用途，未提供假冒的投影或反投影。 |
| Climate／Hydrology／SW3／UniStorm／A*／LUT | 未在本階段重寫或匯入。工程目前仍為 Built-in，沒有完成 URP 遷移與商業插件整合驗收。 |

## 修改／新增檔案

完整清單與 SHA-256 在 `CHANGE_MANIFEST_3_3_3.json`，程式差異在 `3_3_3_changes.diff`。主要檔案如下：

| 類型 | 檔案（相對工程根目錄） | 用途 |
|---|---|---|
| 新增 | Assets/ORBIS/Runtime/Planet/Core/PlanetCoordinateSystem.cs | PlanetVector、PlanetLocation、PlanetTangentFrame、PeriodicMapPosition。 |
| 修改 | Assets/ORBIS/Runtime/FlatWorld/Presentation/FlatScrollNavigator.cs | 共享座標、雙精度平移、同步提交、落地尺度返回與反向操作。 |
| 修改 | Assets/ORBIS/Runtime/FlatWorld/Presentation/FlatLocalWater.cs | 舊區塊保留、雙緩衝、換區淡化與取消自動漣漪。 |
| 新增 | Assets/ORBIS/Resources/ORBIS/FlatWorld/PlanetCoordinates.cginc | 共用球面軸向。 |
| 修改 | Assets/ORBIS/Resources/ORBIS/FlatWorld/ScrollProjection.cginc | 接入共用球面函式，保留 Z 鏡射於顯示層。 |
| 修改 | Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatPixelDetail.cginc | HLSL 保留字修復。 |
| 修改 | Assets/ORBIS/Resources/ORBIS/FlatWorld/Water/FlatWater.cginc | 迴圈遮蔽、接縫導數、世界座標區塊混合。 |
| 修改 | Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatMap.shader、ScrollSurface.shader | 舊 SWE 紋理及混合預設屬性。 |
| 修改 | Assets/ORBIS/Runtime/OrbisAtmosphereRenderer.cs | MRT 結束時綁定圖像效果的 destination。 |
| 新增 | Assets/ORBIS/Editor/FlatSeamValidation.cs | Unity Play Mode 提交一致性及進退尺度回歸。 |
| 修改／新增 | Validation/Perf3_2/generate_fragment_probes.py、Validation/Perf3_3_3/ | 轉譯器支援改名索引、新版驗證腳本和結果。 |

## 演算法與關鍵程式

座標使用 +Y 北極、+Z 零經度、+X 東經 90 度，經緯度單位為弧度。`ToWorld`／`LocalFrame` 的 radius、altitude、center 使用同一呼叫者單位；不隱式採用地球半徑。現有顯示仍使用 radius=1 與舊高度顯示倍率，原本 SWE 的每格顯示尺度也沒有冒充行星距離。

```csharp
var location = PlanetCoordinateSystem.FromMapUV(u, v, altitude);
var frame = PlanetCoordinateSystem.LocalFrame(location, planetCenter, planetRadius);
PlanetVector local = frame.ToLocal(worldPosition); // 先 double 相減，再轉 float
PlanetVector world = frame.ToWorld(local);
PlanetVector localVelocity = frame.DirectionToLocal(worldVelocity); // 向量不减原點
```

同一點在不同本地 frame 的位置應先 `oldFrame.ToWorld` 再 `newFrame.ToLocal`，風速／流速則使用 Direction 方法。這是座標換基，不是遠距離沿球面的平行運輸；Cube Sphere 邊界向量運輸不能直接用此替代。

舊版 `PanBy` 可由 UI 事件在 `FlatWorldApp.Update` 之後執行，當時 `Tick` 已改副本可見性，但 `SyncMaterials` 要等下一個 Update。跨越顯示切口時，`RenderPan` 會重定位一個世界寬度，這個不同步會被放大成一幀刷新。現在提交順序固定為：

```csharp
if(surface) ApplyProjection(surface);
UpdateSheets();
UpdateRoute();
```

每次 Tick 與複製地圖材質後都走同一提交點；不靠 UI Update 的先後次序。`ReanchorForExit` 也立即更新鏡頭和材質，避免大氣拾取使用新基底而鏡頭仍停在舊基底。

週期重定位保留 double 的 canonical X，只向 GPU 送出 anchor 附近的有界座標。Tile ID 仍經唯一的 WorldData 尋址。此重定位不建立新地圖、不重跑生成、不更換時間。

局部水面異步產生新區塊時，上一張已發布紋理繼續以自己的世界矩形採樣。換區時交換兩張已分配紋理，使用 smoothstep 在 0.22 秒內混合貢獻；淡化完成前不再次換區。矩形原點在經度上按世界寬度取模。兩張固定 64×64 RGBA32 紋理共 32 KiB 原始像素，不含 Unity／驅動開銷。當前方法保留視覺連續性，並未把 SWE 求解狀態搬運到新網格；全球水量仍不由這個視覺求解器控制。

水面波紋的螢幕導數先減去最近的整圈寬度，再估算採樣跨度，避免 canonical UV 跨零時造成濾波強度突然變化。

進入 2D 時記錄 pixels/tile 與 viewport height，返回门檻依當前 viewport height 換算。滾輪跨過門檻時等待平滑縮放到門檻後自動開始收回；M 不設定 FitPixels，直接按當前 orthographicSize 推導收回起始距離。反向操作取消退出並使用當前鏡頭位置；短暫反轉不重新定義落地門檻。

大氣 MRT 的 finally 原本在 Present 後重新綁定舊 render target。已改為保留 destination。這是針對之前畫面中 OnRenderImage 警告的源碼修正；尚未在 Unity 重現和確認警告消失。Unity 對 image effect 的來源／目的地契約見 [OnRenderImage 官方文件](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html)。

## Inspector 設定

本階段沒有新增必填 Inspector 欄位。入口仍為 Orbis2DWorld.unity，FlatWorldApp 在執行時建立 navigator／材質。保留 Built-in 管線；新座標核心不依賴 Unity.Mathematics 或商業插件。新增 SWE 材質欄位是隱藏的執行時資料，不增加玩家控制項。

## Debug 與驗收

- 新版離線入口：`python3 Validation/Perf3_3_3/run_offline.py`。
- Unity Shader 匯入：原有 Tools/ORBIS/Perf3.3/2。
- 新增 Play Mode 菜單：Tools/ORBIS/Perf3.3/5 Verify seam commit and return scale in Play Mode。
- 菜單 5 會在 PanBy 返回後立即讀材質，檢查經度零點、相對落點的顯示切口與多圈移動，然後檢查滾輪退出和 M 直接退出的中心 Tile。它改變視角，暫停／恢復模擬，不寫世界存檔。
- 純數值一致不等於已消除所有畫面閃爍：仍須在原存檔錄製快速／慢速往返接縫並用 Profiler 驗收，尤其是開雲、低畫質、視窗縮放和水面換區。

## 已知限制與下一階段介面

沒有執行 C# 語義編譯、Unity 場景載入、D3D11 或 FPS 測試。GLES 對關鍵字的規則與 D3D 不同，因此本版補了關鍵字掃描，但掃描仍不是 D3D 編譯器。

下一個 P0 子階段應讓權威 PlanetProfile 明確提供半徑及公尺尺度，使用 `PlanetLocation → WorldData 高度適配器 → PlanetTangentFrame` 產生局部地形和水面區塊，讓碰撞及導航查詢讀同一份高度。保持既有存檔可透過適配器取樣，再實施 Cube Sphere 分塊及跨面鄰接；不要直接把六張獨立網格接起來後各自運行水文。

在局部區塊和共用 frame 驗收後，再接水文 Priority-Flood、主下游／分流、河網、真實河床刻蝕和 WaterState；之後才讓 SW3／UniStorm／A* 作為表現或局部執行器接入。AuthaGraph 投影及反投影保留為獨立全球 UI，不改權威資料的距離、流向或鄰接。
