# Shader 設計與參考來源

查閱日期：2026-09-10。下列為作者／官方的一手資料。本專案沒有直接嵌入這些專案的程式、素材或 LUT；依概念重新編寫 Built-in 管線 Shader。

| 來源 | 參考內容與本版選擇 |
|---|---|
| [Eric Bruneton：Precomputed Atmospheric Scattering](https://ebruneton.github.io/precomputed_atmospheric_scattering/) | 參考大氣密度分布、波長相關消光、Rayleigh／Mie 散射與數值驗證思路。本版是即時單次散射積分，沒有照搬多次散射預計算 LUT、臭氧光譜及完整輻射測量流程。 |
| [Guerrilla：The Real-Time Volumetric Cloudscapes of Horizon Zero Dawn](https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn) | 參考雲形、動畫和光照共同設計的方式。本版用覆蓋場、垂直密度剖面、週期性形狀／侵蝕體積、自陰影與提前終止；沒有聲稱達到該項目的效能或畫質。 |
| [Sebastian Lague：Solar System](https://github.com/SebLague/Solar-System) | 查閱 Unity 行星實驗的結構與適用範圍，作為原生行星渲染專案的參考；本版不依賴其程式或素材。 |
| [Unity 6：Camera Depth Texture](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-CameraDepthTexture.html) | 地表附 ShadowCaster pass，體積以相機深度截止，另加解析海平面交點作保底，避免背面雲穿透行星。 |
| [Unity 6：Texture3D.SetPixels32](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture3D.SetPixels32.html) | 用於載入預先生成的 64³ 四通道雲體積；GPU 重複取樣，不在每個步進點重新計算完整 Worley 鄰域。 |
| [Unity 6：ShaderUtil.ShaderHasError](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderUtil.ShaderHasError.html) | 用於專案內的 Editor 圖形驗證選單；目前尚未在 Editor 執行該檢查。 |

## 同一套天氣資料如何進入畫面

1. `OrbisAtmosphereModel` 產生壓力場、低壓系統與風場擾動。
2. `OrbisWorld.Climate` 結合風帶、季風、局部溫差與地形，更新風、雲、降水、土壤與溫度。
3. 每個 Tile 將風積分為 `cloudDrift`，並隨世界存檔保存；平滑顯示使用當前風向外插剩餘幀時間。
4. `OrbisWeatherField` 對球面附近四個 Tile 插值，生成雲量／濕度／溫度／降雨、三維風向及積分位移貼圖。
5. `WeatherDensity.cginc` 同時供體積雲、地表雲影及平面地圖使用。不存在與風場無關的固定全局滾動向量。
6. `Clouds.shader` 在同一條射線上積分雲和大氣，使用 RGB 透射乘上背景，再加入散射輻射；平均氣溫改變空氣尺度高度，太陽輻射係數改變亮度。

雲與風是遊戲尺度的近似耦合，並非完整流體力學求解器。局部積分位移不是守恆的拉格朗日流體求解；長時間的雲形變化仍受程序密度細節影響。土壤與水汽目前也保留簡化更新，不能當作氣象預報。

## 渲染成本與限制

- 桌面 Built-in 管線。地形約 44 萬三角形，獨立 Chunk 剔除；GPU 效能尚待測量。
- 單一具名 GrabPass 用於 RGB 透射合成。這會增加一次螢幕拷貝，沒有宣稱是 URP Render Graph 或零拷貝實作。
- 體積畫質 32／56／80 步，日光路徑六點取樣，雲陰影三點取樣；低透射時提前終止。
- 高度尺度與雲層厚度為了星球視角經過誇張。沒有多次散射 LUT、時間重投影、近地飛行或自適應地形 LOD。
- 「地表細節」控制材質與微表面擾動；不改變已建立的網格細分級別。地形細分固定為 8。
