# ORBIS Perf3.5 — Markley 四向卷軸

本工程的体积云散射包含对 **HanPi Volume Cloud © AshenOneArt**
（https://github.com/AshenOneArt/HPVolumeCloud）`phi_fwd` 漫射场的
Built-in 管线改编。来源阅读版与完整许可保存在
`Reference/HPVolumeCloud/`；本工程并未使用来源的 HDRP Renderer。
太阳、月亮和星空素材选自用户提供的 SolarSystem 包，并由 ORBIS 时钟控制。

本包是從目前可恢復的工程快照重建的新版，取代先前失效的 3.4 下載檔；不是原 3.4 壓縮檔的逐位元副本。包含 Unity 工程、之前的地形／河口／樹木／接縫修復，以及本次 Markley 修改。無須再疊加舊 Scroll Patch。

## 開啟

1. 解壓後，在 Unity Hub 開啟 `ORBIS_TerrainPerf3_5_Markley`。
2. 請使用 Unity **6000.0.55f1 / Built-in Render Pipeline**（見 `ProjectSettings/ProjectVersion.txt`；工程最初建立於 6000.0.47f1）。
3. 開啟 `Assets/ORBIS/Scenes/Orbis2DWorld.unity`，等待匯入後進入 Play Mode。
4. 建議開啟新目錄，避免同時存在兩份 ORBIS 腳本。不要把 VendorSources 整個搬進 Assets。
5. 執行 `ORBIS > Validation > Markley Projection and Shaders`。此入口使用真正的 C# 和 Unity 已匯入 Shader 檢查；本次環境未能執行它。

## 本次改動

- 球體的展開網格、2D 總覽與詳細圖統一使用 **Markley / Lee 四面體保角投影**；地形、自然／政治／水體圖、點選與路徑沿用同一份世界資料。
- 左右週期銜接；上下跨界採用旋轉副本及水平位移，支援連續四向拖動。地理 Tile ID 不會因跨界複製，座標重置不重新生成世界。
- 平移座標、地圖副本可見性和路徑在同次提交中更新，針對接縫出現一幀刷新閃爍的原因修正。
- 球體與 2D 展開／收回期間可拖動，也可用 WASD／方向鍵平移；拖動更新落點。
- 滾輪回到進入 2D 的尺度後即從**當前地點**返回球體；M 從當前尺度收回，保留先前版本的退出門檻修復。
- 2D 平面雲層不透明度降到 0.02；地圖雲影降到原權重的 0.05；體積雲柱在總覽／詳細圖分別使用 0.05／0.025 權重。球體體積雲不套用這個淡化權重。
- SW3 的 StreamWaves／OceanWaves 波紋素材接入現有水面；UniStorm 的形狀噪聲、細節噪聲與 CloudProfile 接入現有體積雲密度。

## 包的差異與資產整合邊界

`Full` 包保留 `VendorSources` 中的 SW3、UniStorm、A* 原始資源及 `Tools/WorldAtlas/source`。`Core` 包包含同一套可開啟的 Unity 工程和正在使用的水紋／雲噪聲素材，省略大型 VendorSources 供應商來源，方便下載。

本次實際啟用的是上述水紋、噪聲與雲參數橋接；**沒有宣稱已接入完整 SW3 URP Renderer、UniStorm 天氣控制器、A* 尋路替換或 WorldAtlas 生成流程**。A* 和 WorldAtlas 原始碼保留於 Full 包，後續階段需要繼續接入。此版本未遷移至 URP。

## 投影與驗證限制

Markley 可以形成四向連續的地圖覆蓋，但不是等距或無變形投影，四個四面體頂點仍有不可避免的奇點。經緯資料僅保留作世界資料／存檔的統一索引，不是另一套遊玩投影。尚未完成米制局部物理、完整分塊導航與新版氣候水文模擬。

本次有效測試在 `Validation/Perf3_5`：10,000 個公開實作參考位置的正投影比對、反查表回算、四邊對應、座標重置；C# 語法檢查；生產地表、雲密度／光照、Markley 取樣程式機械轉譯後的 Mesa GLES 編譯。報告明確記錄 `unity_executed: false`。這些**不等於 Unity C# 語義編譯、D3D11 編譯、FPS 或實機畫面驗收**。舊 Validation 目錄僅為歷史記錄。

詳見 `Documentation/Markley_3_5.md`。
