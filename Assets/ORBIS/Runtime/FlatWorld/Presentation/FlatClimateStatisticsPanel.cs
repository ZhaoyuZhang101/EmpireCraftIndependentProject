using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.Flat
{
    internal sealed class FlatClimateStatisticsPanel : IDisposable
    {
        enum Layer { Feature, Correlation, Biome, BiomeConfidence, WeatherDistance, WeatherFlags, WeatherReliability, WaterDistance, WaterFlags, WaterReliability }
        static readonly string[] LayerNames = { "物理变量", "区域温度 / 降水相关", "辅助生物群系", "群系分类支持度", "天气马氏距离 D", "天气异常类型", "天气参考可靠度", "水文马氏距离 D", "水文异常类型", "水文参考可靠度" };
        static readonly string[] FeatureNames = { "气温", "相对湿度", "海平面气压", "降水", "水平风速", "土壤饱和度", "地表高程", "蒸发", "本地径流", "河道流量", "地下水储量" };
        static readonly string[] Units = { "°C", "1", "hPa", "mm/d", "m/s", "1", "m", "mm/d", "mm/d", "相对量", "mm" };
        static readonly string[] BiomeNames = { "热带雨林", "热带草原", "温带森林", "温带草原", "沙漠", "苔原", "北方针叶林", "高山带" };
        static readonly int[] BiomeColors = { 0x247759, 0xb2a051, 0x579261, 0xafbb72, 0xe1b577, 0xa9c5bc, 0x477776, 0xb6acba };
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        const int Missing = 0x555967, UndefinedCorrelation = 0x765c69, Protected = 0x243e53, Outside = 0x855e74, Normal = 0x315957;
        static readonly int[] Sequential = { 0x24374f, 0x266a80, 0x4d9b91, 0xa7bd84, 0xf4dca1 };
        static readonly int[] Diverging = { 0x345e9e, 0x86a7c7, 0xd6d6c7, 0xd39c75, 0xa24d40 };
        static readonly int[] DistanceColors = { 0x254b59, 0x5b9291, 0xe0cb89, 0xd17c53, 0x963d50 };

        readonly VisualElement window, body, host, marker, legend, matrix;
        readonly Image image;
        readonly Label status, caption, selection, details, matrixLabel;
        readonly ScrollView matrixScroll;
        readonly DropdownField layer, feature, matrixKind;
        readonly Button close, opener;
        readonly List<VisualElement> disabledBehindModal = new List<VisualElement>();
        WorldData world;
        int terrainRevision, desired, scheduled, published = -1, uploadedRaster = -1;
        long quarter = long.MinValue;
        double sampleDay;
        FlatCycloneWeather storms;
        FlatBiomeStatistics supplied;
        Worker worker;
        CancellationTokenSource cancellation;
        Task<Frame> job;
        Task draining;
        bool failed;
        internal bool IsOpen { get; private set; }
        internal bool IsReady => IsOpen && published == desired && !failed;
        internal int SelectedTile { get; private set; } = -1;
        internal Texture2D Texture { get; private set; }

        internal FlatClimateStatisticsPanel(VisualElement root)
        {
            window = root.Q("climateStatsWindow");
            opener = root.Q<Button>("climateStatistics");
            body = window.Q("climateStatsBody"); host = window.Q("climateStatsMapHost");
            image = window.Q<Image>("climateStatsMap"); marker = window.Q("climateStatsMarker");
            legend = window.Q("climateStatsLegend"); matrix = window.Q("climateStatsMatrix");
            matrixScroll = window.Q<ScrollView>("climateStatsMatrixScroll");
            status = window.Q<Label>("climateStatsStatus"); caption = window.Q<Label>("climateStatsCaption");
            selection = window.Q<Label>("climateStatsSelection"); details = window.Q<Label>("climateStatsDetails");
            matrixLabel = window.Q<Label>("climateStatsMatrixLabel");
            layer = window.Q<DropdownField>("climateStatsLayer"); feature = window.Q<DropdownField>("climateStatsFeature");
            matrixKind = window.Q<DropdownField>("climateStatsMatrixKind");
            layer.choices = new List<string>(LayerNames); layer.index = 0;
            feature.choices = new List<string>();
            for (int i = 0; i < FeatureNames.Length; ++i) feature.choices.Add(FeatureNames[i] + "  " + DisplayUnit(i));
            feature.index = 0;
            matrixKind.choices = new List<string> { "天气", "水文" }; matrixKind.index = 0;
            layer.RegisterValueChangedCallback(e => { feature.EnableInClassList("is-hidden", layer.index != 0); Request(); });
            feature.RegisterValueChangedCallback(e => Request());
            matrixKind.RegisterValueChangedCallback(e => Request());
            close = window.Q<Button>("climateStatsClose"); close.clicked += Close;
            window.Q<Button>("climateStatsRefresh").clicked += () => { quarter = long.MinValue; Request(); };
            window.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); } });
            window.Q("climateStatsCard").RegisterCallback<GeometryChangedEvent>(e =>
                body.EnableInClassList("climate-narrow", e.newRect.width < 860));
            host.RegisterCallback<GeometryChangedEvent>(e => {
                if (e.newRect.width > 0 && Math.Abs(e.newRect.height - .5f * e.newRect.width) > 1)
                    host.style.height = .5f * e.newRect.width;
            });
            image.scaleMode = ScaleMode.StretchToFill;
            host.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0 || world == null || !IsOpen) return;
                var p = host.WorldToLocal(e.position);
                var rect = host.contentRect;
                if (rect.width <= 0 || rect.height <= 0 || !rect.Contains(p)) return;
                int x = Math.Min(world.Width - 1, (int)((p.x - rect.x) * world.Width / rect.width));
                int y = Math.Min(world.Height - 1, (int)((1 - (p.y - rect.y) / rect.height) * world.Height));
                SelectTile(world.Index(x, y));
                e.StopPropagation();
            });
        }

        internal void Open(WorldData source, double day, int tile, FlatBiomeStatistics biomeReferences = null)
        {
            if (IsOpen) Close();
            IsOpen = true;
            supplied = biomeReferences;
            foreach (var sibling in window.parent.Children())
                if (sibling != window && sibling.enabledSelf) { sibling.SetEnabled(false); disabledBehindModal.Add(sibling); }
            window.RemoveFromClassList("is-hidden");
            BindWorld(source, tile);
            close.Focus();
            Tick(source, day);
        }

        void BindWorld(WorldData source, int tile)
        {
            RetireJob();
            world = source;
            terrainRevision = source.TerrainRevision;
            SelectedTile = (uint)tile < (uint)source.Count ? tile : source.Index(source.Width / 2, source.Height / 2);
            worker = new Worker(source, supplied);
            quarter = long.MinValue;
            published = uploadedRaster = -1;
            image.image = null;
            marker.AddToClassList("is-hidden");
            details.text = selection.text = caption.text = matrixLabel.text = "";
            legend.Clear(); matrix.Clear();
            matrixScroll.AddToClassList("is-hidden");
            Request();
        }

        internal void SelectTile(int tile)
        {
            if (!IsOpen || world == null || (uint)tile >= (uint)world.Count || SelectedTile == tile) return;
            SelectedTile = tile;
            Request();
        }

        void Request()
        {
            if (!IsOpen) return;
            ++desired;
            failed = false;
            status.text = "正在读取气候统计快照…";
        }

        // Called only for the visible panel. Tile picks and display choices reuse
        // the atlas; camera changes and civilization revisions are independent.
        internal void Tick(WorldData source, double day)
        {
            if (!IsOpen) return;
            if (world != source || terrainRevision != source.TerrainRevision) BindWorld(source, -1);
            long epoch = (long)Math.Floor(day * 4);
            if (quarter != epoch)
            {
                quarter = epoch; sampleDay = day;
                storms = source.Cyclones?.Clone(); // Unity thread captures the mutable storm snapshot.
                Request();
            }
            if (job != null && job.IsCompleted)
            {
                try
                {
                    var result = job.GetAwaiter().GetResult();
                    if (result.Request == desired) { Apply(result); published = result.Request; }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    failed = scheduled == desired;
                    worker = new Worker(source, supplied);
                    uploadedRaster = -1;
                    if (failed) status.text = "统计快照失败，可点击刷新重试。" + ex.Message;
                    Debug.LogException(ex);
                }
                finally { job = null; cancellation.Dispose(); cancellation = null; }
            }
            if (job != null || published == desired || failed || (draining != null && !draining.IsCompleted)) return;
            draining = null;
            var input = new Input(desired, sampleDay, storms, SelectedTile, (Layer)layer.index, (ClimateFeature)feature.index, matrixKind.index == 1);
            scheduled = desired;
            var owner = worker;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            job = Task.Run(() => owner.Calculate(input, token), token);
        }

        void RetireJob()
        {
            if (job == null) return;
            var old = cancellation;
            old.Cancel();
            draining = job.ContinueWith(t => { if (t.IsFaulted) { var observed = t.Exception; } old.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            job = null; cancellation = null;
        }

        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            RetireJob();
            world = null; worker = null; supplied = null; storms = null;
            window.AddToClassList("is-hidden");
            foreach (var sibling in disabledBehindModal) sibling.SetEnabled(true);
            disabledBehindModal.Clear();
            if (opener.panel != null) opener.Focus();
            image.image = null;
            if (Texture) UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }

        public void Dispose() { Close(); }

        void Apply(Frame frame)
        {
            if (uploadedRaster != frame.Raster)
            {
                if (!Texture || Texture.width != frame.Width || Texture.height != frame.Height)
                {
                    if (Texture) UnityEngine.Object.Destroy(Texture);
                    Texture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false) {
                        name = "ORBIS climate statistics", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                    };
                }
                Texture.LoadRawTextureData(frame.Pixels); Texture.Apply(false, false); image.image = Texture;
                legend.Clear();
                foreach (var key in frame.Keys)
                {
                    var group = new VisualElement(); group.AddToClassList("climate-key");
                    var swatch = new VisualElement(); swatch.AddToClassList("climate-key-mark");
                    swatch.style.backgroundColor = (Color)ToColor(key.Color); group.Add(swatch);
                    var label = new Label(key.Text); label.AddToClassList("climate-key-text"); group.Add(label); legend.Add(group);
                }
                caption.text = frame.Caption;
                uploadedRaster = frame.Raster;
            }
            marker.RemoveFromClassList("is-hidden");
            marker.style.left = Length.Percent(100f * (SelectedTile % world.Width + .5f) / world.Width);
            marker.style.top = Length.Percent(100f * (1 - (SelectedTile / world.Width + .5f) / world.Height));
            status.text = frame.Status; selection.text = frame.Selection;
            details.text = "现有地表  " + FlatPalette.BiomeNames[(int)frame.Ground] + "\n" + frame.Details;
            ShowMatrix(frame.Matrix);
        }

        void ShowMatrix(MatrixData data)
        {
            matrix.Clear(); matrixLabel.text = data.Caption;
            int n = data.Axes.Length;
            matrixScroll.EnableInClassList("is-hidden", n == 0);
            if (n == 0) return;
            matrixScroll.style.height = (n + 1) * 34 + 20;
            matrix.style.width = (n + 1) * 79;
            for (int i = -1; i < n; ++i)
            {
                var row = new VisualElement(); row.AddToClassList("climate-table-row"); matrix.Add(row);
                for (int j = -1; j < n; ++j)
                {
                    var label = new Label(); label.AddToClassList("climate-table-cell"); row.Add(label);
                    if (i < 0 || j < 0)
                    {
                        int k = Math.Max(i, j);
                        label.text = k < 0 ? "协方差" : FeatureNames[(int)data.Axes[k]] + "\n" + Units[(int)data.Axes[k]];
                        if (k >= 0) label.tooltip = "地块观测 " + Measurement(data.Axes[k], data.Observed[k]) + "  ·  参考均值 " +
                            Measurement(data.Axes[k], data.Means[k]) + " " + Units[(int)data.Axes[k]];
                        label.AddToClassList("climate-table-axis");
                        continue;
                    }
                    int slot = i * n + j;
                    bool defined = !double.IsNaN(data.Correlations[slot]);
                    int color = defined ? Ramp(Diverging, .5 + .5 * data.Correlations[slot]) : UndefinedCorrelation;
                    label.text = Number(data.Values[slot]);
                    label.style.backgroundColor = (Color)ToColor(color);
                    var rgb = ToColor(color);
                    label.style.color = rgb.r * .299 + rgb.g * .587 + rgb.b * .114 > 145 ? Color.black : Color.white;
                    label.tooltip = FeatureNames[(int)data.Axes[i]] + " × " + FeatureNames[(int)data.Axes[j]] +
                        "  " + data.Values[slot].ToString("G7", Invariant) + " " + Units[(int)data.Axes[i]] + "·" + Units[(int)data.Axes[j]] +
                        (defined ? "  Pearson r = " + Number(data.Correlations[slot]) : "  Pearson r 未定义（零方差）");
                }
            }
        }

        static Color32 ToColor(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static string Number(double value)
        {
            if (!Finite(value)) return "—";
            double size = Math.Abs(value);
            if (size == 0) return "0";
            if (size < .0001 || size >= 1e7) return value.ToString("0.##E+0", Invariant);
            if (size >= 1000) return value.ToString("0.#", Invariant);
            if (size >= 10) return value.ToString("0.##", Invariant);
            return value.ToString(size >= 1 ? "0.###" : "0.#####", Invariant);
        }
        static string Measurement(ClimateFeature feature, double value)
        {
            if (!Finite(value)) return "—";
            if (feature == ClimateFeature.Pressure || feature == ClimateFeature.Temperature) return value.ToString("0.0", Invariant);
            if (feature == ClimateFeature.Elevation) return value.ToString("0", Invariant);
            return Number(value);
        }
        static string DisplayUnit(int axis) => Units[axis] == "1" ? "比例 0–1" : Units[axis];
        static int Ramp(int[] colors, double t)
        {
            double p = Math.Max(0, Math.Min(1, t)) * (colors.Length - 1);
            int i = Math.Min(colors.Length - 2, (int)p), a = colors[i], b = colors[i + 1];
            double f = p - i;
            int r = (int)((a >> 16) + ((b >> 16) - (a >> 16)) * f + .5);
            int g = (int)(((a >> 8) & 255) + (((b >> 8) & 255) - ((a >> 8) & 255)) * f + .5);
            int blue = (int)((a & 255) + ((b & 255) - (a & 255)) * f + .5);
            return r << 16 | g << 8 | blue;
        }

        readonly struct Input
        {
            internal readonly int Request, Tile;
            internal readonly double Day;
            internal readonly FlatCycloneWeather Storms;
            internal readonly Layer Layer;
            internal readonly ClimateFeature Feature;
            internal readonly bool WaterMatrix;
            internal Input(int request, double day, FlatCycloneWeather storms, int tile, Layer layer, ClimateFeature feature, bool water)
            { Request = request; Day = day; Storms = storms; Tile = tile; Layer = layer; Feature = feature; WaterMatrix = water; }
        }

        readonly struct Key
        {
            internal readonly string Text;
            internal readonly int Color;
            internal readonly ClimateSignals Signal;
            internal Key(string text, int color, ClimateSignals signal = ClimateSignals.None) { Text = text; Color = color; Signal = signal; }
        }
        static readonly Key[] WeatherKeys = {
            new Key("复合", 0xaf78ab, ClimateSignals.Compound), new Key("高温", 0xd26749, ClimateSignals.Hot),
            new Key("低温", 0x709ccf, ClimateSignals.Cold), new Key("低压", 0x9782b9, ClimateSignals.LowPressure),
            new Key("多雨", 0x52949e, ClimateSignals.Wet), new Key("少雨", 0xd6ae62, ClimateSignals.Dry),
            new Key("强风", 0xa5d0c4, ClimateSignals.HighWind)
        };
        static readonly Key[] WaterKeys = {
            new Key("洪水潜势", 0x80649d, ClimateSignals.FloodPotential), new Key("干旱风险", 0xbe6f43, ClimateSignals.DroughtRisk),
            new Key("高流量", 0x5085b3, ClimateSignals.HighFlow), new Key("高径流", 0x5aabac, ClimateSignals.RunoffHigh),
            new Key("低径流", 0xc2ab69, ClimateSignals.RunoffLow), new Key("地下水低", 0xb28782, ClimateSignals.GroundwaterLow),
            new Key("土壤干", 0xcd9b61, ClimateSignals.SoilDry), new Key("多雨", 0x5894a1, ClimateSignals.Wet),
            new Key("少雨", 0xe0c78d, ClimateSignals.Dry), new Key("复合", 0xa678ab, ClimateSignals.Compound)
        };

        sealed class MatrixData
        {
            internal string Caption;
            internal ClimateFeature[] Axes = Array.Empty<ClimateFeature>();
            internal double[] Values, Correlations, Observed, Means;
        }
        sealed class Frame
        {
            internal int Request, Width, Height, Raster;
            internal byte[] Pixels;
            internal Key[] Keys;
            internal string Caption, Status, Selection, Details;
            internal MatrixData Matrix;
            internal Biome Ground;
        }

        // One background job at a time owns this atlas, its score workspaces and
        // raster. The main thread copies the finished bytes before scheduling again.
        sealed class Worker
        {
            readonly WorldData world;
            readonly FlatBiomeStatistics biomeReferences;
            FlatClimateStatistics atlas;
            double previousDay = double.NaN;
            Layer previousLayer;
            ClimateFeature previousFeature;
            int raster;
            byte[] pixels;
            double[] samples;
            Key[] keys;
            string caption;
            double low, high;

            internal Worker(WorldData world, FlatBiomeStatistics biomes) { this.world = world; biomeReferences = biomes; }

            internal Frame Calculate(Input input, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (atlas == null) atlas = new FlatClimateStatistics(world, token, 64, biomeReferences);
                bool updated = previousDay != input.Day;
                if (updated) atlas.Refresh(input.Day, input.Storms, token);
                if (updated || raster == 0 || input.Layer != previousLayer || input.Feature != previousFeature)
                {
                    MakeRaster(input, token);
                    ++raster;
                    previousLayer = input.Layer; previousFeature = input.Feature;
                }
                previousDay = input.Day;
                var tile = atlas.Inspect(input.Tile, input.Day, input.Storms);
                token.ThrowIfCancellationRequested();
                int x = input.Tile % world.Width, y = input.Tile / world.Width;
                double lon = ((x + .5) / world.Width - .5) * 360, lat = ((y + .5) / world.Height - .5) * 180;
                return new Frame {
                    Request = input.Request, Width = atlas.Width, Height = atlas.Height, Raster = raster,
                    Pixels = pixels, Keys = keys, Caption = caption, Ground = world.Biomes[input.Tile],
                    Status = world.Seed + "  ·  第 " + ((int)(input.Day / 360) + 1) + " 年 / " + ((int)(input.Day / 30) % 12 + 1) +
                        " 月  ·  全球快照日 " + (Math.Floor(input.Day * 4) / 4).ToString("F2", Invariant) + "  ·  全球采样 " + atlas.Width + " × " + atlas.Height +
                        "  ·  " + atlas.RegionCount + " 个区域  ·  每 6 游戏小时刷新",
                    Selection = "地块 " + input.Tile + "  [" + x + ", " + y + "]  ·  区域 " + (tile.Region + 1) + "\n" +
                        Math.Abs(lat).ToString("F1", Invariant) + (lat < 0 ? "°S  " : "°N  ") +
                        Math.Abs(lon).ToString("F1", Invariant) + (lon < 0 ? "°W" : "°E") +
                        "  ·  观测日 " + input.Day.ToString("F2", Invariant),
                    Details = Describe(tile), Matrix = MatrixSnapshot(tile, input.WaterMatrix)
                };
            }

            void MakeRaster(Input input, CancellationToken token)
            {
                var cells = atlas.Cells;
                if (pixels == null) { pixels = new byte[cells.Length * 4]; samples = new double[cells.Length]; }
                var legendEntries = new List<Key>();
                low = 0; high = 1;
                caption = LayerNames[(int)input.Layer];
                if (input.Layer == Layer.Feature)
                {
                    int n = 0;
                    for (int i = 0; i < cells.Length; ++i)
                    {
                        double v = ClimateFeatureSet.Value(cells[i].Observation, input.Feature);
                        if (Finite(v)) samples[n++] = v;
                    }
                    Array.Sort(samples, 0, n);
                    if (n > 0) { low = samples[(n - 1) / 20]; high = samples[(n - 1) * 19 / 20]; }
                    bool fullRange = n > 0 && low == high;
                    if (fullRange) { low = samples[0]; high = samples[n - 1]; }
                    int axis = (int)input.Feature;
                    caption = FeatureNames[axis] + "  ·  " + DisplayUnit(axis) + "  ·  " + (fullRange
                        ? (low == high ? "全部有效样点同值" : "色阶覆盖有效样点的最小至最大值")
                        : "色阶取有效样本第 5 至 95 百分位，超界使用端点色");
                    if (n == 0) caption = FeatureNames[axis] + "  ·  全球样点均无此项观测";
                    if (n > 0) ScaleKeys(legendEntries, Sequential, low, high, "", input.Feature);
                }
                else if (input.Layer == Layer.Correlation)
                {
                    caption = "同季节区域参考的气温与降水 Pearson r";
                    ScaleKeys(legendEntries, Diverging, -1, 1, "");
                    legendEntries.Add(new Key("零方差 / r 未定义", UndefinedCorrelation));
                }
                else if (input.Layer == Layer.Biome)
                {
                    for (int i = 0; i < BiomeNames.Length; ++i) legendEntries.Add(new Key(BiomeNames[i], BiomeColors[i]));
                    legendEntries.Add(new Key("保护生境", Protected)); legendEntries.Add(new Key("参考范围外", Outside));
                }
                else if (input.Layer == Layer.WeatherFlags || input.Layer == Layer.WaterFlags)
                {
                    caption += "  ·  重叠时按图例顺序着色，地块详情列出全部信号";
                    legendEntries.AddRange(input.Layer == Layer.WeatherFlags ? WeatherKeys : WaterKeys);
                    legendEntries.Add(new Key("无标记", Normal));
                }
                else if (input.Layer == Layer.WeatherDistance || input.Layer == Layer.WaterDistance)
                {
                    caption += "  ·  相对当地区域参考，D 无量纲";
                    ScaleKeys(legendEntries, DistanceColors, 0, 8, "≥");
                }
                else
                {
                    caption += input.Layer == Layer.BiomeConfidence ? "  ·  候选群系的条件概率" : "  ·  区域参考样本支持指数";
                    ScaleKeys(legendEntries, Sequential, 0, 1, "");
                }
                legendEntries.Add(new Key("无可用数据", Missing));
                keys = legendEntries.ToArray();
                for (int i = 0; i < cells.Length; ++i)
                {
                    if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                    int color = Pixel(cells[i], input), k = i * 4;
                    pixels[k] = (byte)(color >> 16); pixels[k + 1] = (byte)(color >> 8); pixels[k + 2] = (byte)color; pixels[k + 3] = 255;
                }
            }

            int Pixel(in ClimateTileDiagnostics tile, Input input)
            {
                switch (input.Layer)
                {
                    case Layer.Feature:
                        double v = ClimateFeatureSet.Value(tile.Observation, input.Feature);
                        return !Finite(v) ? Missing : Ramp(Sequential, low == high ? .5 : (v - low) / (high - low));
                    case Layer.Correlation:
                        var reference = tile.WeatherReference;
                        if (reference == null || reference.Count < 2) return Missing;
                        int temp = reference.Features.IndexOf(ClimateFeature.Temperature), rain = reference.Features.IndexOf(ClimateFeature.Precipitation);
                        if (temp < 0 || rain < 0) return Missing;
                        if (reference.Covariance(temp, temp) <= 0 || reference.Covariance(rain, rain) <= 0) return UndefinedCorrelation;
                        return Ramp(Diverging, .5 + .5 * reference.Correlation(temp, rain));
                    case Layer.Biome:
                        if (tile.Biome.Status == FlatBiomeStatus.ProtectedHabitat) return Protected;
                        if (tile.Biome.Status == FlatBiomeStatus.OutsideReference) return Outside;
                        return UsableBiome(tile.Biome) ? BiomeColors[(int)tile.Biome.Best] : Missing;
                    case Layer.BiomeConfidence:
                        return UsableBiome(tile.Biome) ? Ramp(Sequential, tile.Biome.Confidence) : Missing;
                    case Layer.WeatherDistance: return tile.Weather.Valid ? Ramp(DistanceColors, tile.Weather.Distance / 8) : Missing;
                    case Layer.WaterDistance: return tile.Water.Valid ? Ramp(DistanceColors, tile.Water.Distance / 8) : Missing;
                    case Layer.WeatherReliability: return tile.Weather.Valid ? Ramp(Sequential, tile.Weather.Reliability) : Missing;
                    case Layer.WaterReliability: return tile.Water.Valid ? Ramp(Sequential, tile.Water.Reliability) : Missing;
                    case Layer.WeatherFlags: return FlagColor(tile.Weather, WeatherKeys);
                    case Layer.WaterFlags: return FlagColor(tile.Water, WaterKeys);
                    default: return Missing;
                }
            }
        }

        static void ScaleKeys(List<Key> keys, int[] palette, double min, double max, string upperPrefix, ClimateFeature? feature = null)
        {
            if (min == max) { keys.Add(new Key(feature.HasValue ? Measurement(feature.Value, min) : Number(min), Ramp(palette, .5))); return; }
            for (int i = 0; i < palette.Length; ++i)
            {
                double value = min + (max - min) * i / (palette.Length - 1);
                string label = feature.HasValue ? Measurement(feature.Value, value) : Number(value);
                keys.Add(new Key((i == palette.Length - 1 ? upperPrefix : "") + label, palette[i]));
            }
        }

        static int FlagColor(ClimateScore score, Key[] priority)
        {
            if (!score.Valid) return Missing;
            foreach (var key in priority) if ((score.Signals & key.Signal) != 0) return key.Color;
            return Normal;
        }

        static bool UsableBiome(FlatBiomeEstimate b) => b.Status == FlatBiomeStatus.Estimated || b.Status == FlatBiomeStatus.Ambiguous;

        static string SignalNames(ClimateSignals signals, Key[] keys)
        {
            var text = new StringBuilder();
            foreach (var key in keys)
                if ((signals & key.Signal) != 0) { if (text.Length > 0) text.Append("、"); text.Append(key.Text); }
            return text.Length == 0 ? "无标记" : text.ToString();
        }

        static string Score(ClimateScore score, Key[] keys)
        {
            return score.Valid ? "D " + Number(score.Distance) + "  ·  D² " + Number(score.SquaredDistance) +
                "  ·  可靠度 " + score.Reliability.ToString("P0", Invariant) + "\n" + SignalNames(score.Signals, keys) : "参考或观测不足";
        }

        static string Describe(in ClimateTileDiagnostics tile)
        {
            var o = tile.Observation;
            var b = tile.Biome;
            var text = new StringBuilder(580);
            text.Append("辅助群系  ");
            if (UsableBiome(b))
            {
                text.Append(BiomeNames[(int)b.Best]).Append("  ").Append(b.Confidence.ToString("P0", Invariant));
                if (b.Status == FlatBiomeStatus.Ambiguous) text.Append("（过渡）");
                if (b.RunnerUp != FlatBiomeClass.None) text.Append("  ·  次选 ").Append(BiomeNames[(int)b.RunnerUp]);
            }
            else text.Append(b.Status == FlatBiomeStatus.ProtectedHabitat ? "保护生境" :
                b.Status == FlatBiomeStatus.OutsideReference ? "参考范围外" : "参考不足");
            text.Append("\n\n天气  ").Append(Score(tile.Weather, WeatherKeys));
            text.Append("\n水文  ").Append(Score(tile.Water, WaterKeys));
            text.Append("\n\n当期观测");
            for (int i = 0; i < FeatureNames.Length; ++i)
            {
                var axis = (ClimateFeature)i;
                double value = ClimateFeatureSet.Value(o, axis);
                text.Append('\n').Append(FeatureNames[i]).Append("  ");
                if (!Finite(value)) text.Append('—');
                else if (axis == ClimateFeature.Humidity || axis == ClimateFeature.SoilMoisture)
                    text.Append((100 * value).ToString("0.#", Invariant)).Append('%');
                else text.Append(Measurement(axis, value)).Append(' ').Append(Units[i]);
            }
            text.Append("\n\n水文预算  ").Append(Number(o.WaterBudgetDays)).Append(" 天\n预算降水  ")
                .Append(Number(o.WaterBudgetPrecipitation)).Append(" mm/d  ·  预算流量  ").Append(Number(o.WaterBudgetDischarge)).Append(" 相对量");
            string source = o.Source == FlatClimateObservationSource.LayeredStep ? "分层大气已求解步" :
                o.Source == FlatClimateObservationSource.RegionalForcing ? "区域物理强迫" :
                o.Source == FlatClimateObservationSource.MonthlyNormal ? "常年月值" : "缺失";
            text.Append("\n\n来源  ").Append(source).Append("  ·  气旋轨迹").Append(o.StormTrackSampled ? "已采样" : "无可用历史");
            return text.ToString();
        }

        static MatrixData MatrixSnapshot(in ClimateTileDiagnostics tile, bool water)
        {
            var reference = water ? tile.WaterReference : tile.WeatherReference;
            var result = new MatrixData();
            if (reference == null || reference.Count < 2) { result.Caption = "该地块的区域参考样本不足。"; return result; }
            int n = reference.Features.Dimension;
            result.Axes = new ClimateFeature[n]; result.Values = new double[n * n]; result.Correlations = new double[n * n];
            result.Observed = new double[n]; result.Means = new double[n];
            var description = new StringBuilder();
            description.Append("区域 ").Append(tile.Region + 1).Append(water ? " 的 30 天预算月" : " 的最近常年月").Append("  ·  有效样本 ")
                .Append(reference.EffectiveCount.ToString("F1", Invariant))
                .Append("，本区支持不足时合并邻区同类地表。\n数值单位为行、列单位之积。蓝、浅灰、褐红分别为 r = −1、0、+1。紫灰为零方差。悬停轴名查看观测和均值。");
            result.Caption = description.ToString();
            for (int i = 0; i < n; ++i)
            {
                result.Axes[i] = reference.Features[i].Feature;
                result.Observed[i] = reference.Value(tile.Observation, result.Axes[i]); result.Means[i] = reference.Mean(i);
                for (int j = 0; j < n; ++j)
                {
                    result.Values[i * n + j] = reference.Covariance(i, j);
                    result.Correlations[i * n + j] = reference.Covariance(i, i) > 0 && reference.Covariance(j, j) > 0
                        ? reference.Correlation(i, j) : double.NaN;
                }
            }
            return result;
        }
    }
}
