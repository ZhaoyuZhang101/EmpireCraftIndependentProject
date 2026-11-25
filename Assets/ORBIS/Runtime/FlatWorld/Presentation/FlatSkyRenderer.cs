using System;
using UnityEngine;
namespace Orbis.Flat
{
    /// <summary>Low-frequency visual weather shared by globe and local map.</summary>
    public sealed class FlatSkyRenderer : MonoBehaviour
    {
        public RenderTexture Texture { get; private set; }
        public RenderTexture ColumnTexture { get; private set; }
        RenderTexture weatherBack, columnBack, columnStrip;
        int pendingStrip = -1;
        const int ColumnStrips = 4;
        public double Clock { get; private set; }
        public bool Ready { get { return Texture && ColumnTexture && bake && hpColumn && rendered && Texture.IsCreated() && ColumnTexture.IsCreated(); } }
        public string Status { get; private set; } = "云层缓存等待启用。";
        public int CacheWidth { get; private set; }
        const int ClimateWidth = FlatSkyForcingCache.DefaultClimateWidth, ClimateHeight = FlatSkyForcingCache.DefaultClimateHeight;
        WorldData world;
        Texture2D climate, hpHigh, wind, windBack, climateWind, glebaForcing, glebaAux, glebaUpperWind;
        Texture3D hpNoise, hpDetail;
        Material bake, hpColumn;
        readonly FlatSkyForcingScheduler forcingScheduler = new FlatSkyForcingScheduler(ClimateWidth, ClimateHeight);
        readonly float[] hiddenWindRgba = new float[ClimateWidth * ClimateHeight * 4];
        readonly Color[] windPixels = new Color[ClimateWidth * ClimateHeight], seasonalWindPixels = new Color[ClimateWidth * ClimateHeight];
        readonly Vector2[] longitudeBasis = new Vector2[ClimateWidth], latitudeBasis = new Vector2[ClimateHeight];
        float[] upperWindFallback;
        double stagingWindDay;
        int stagingWindGeneration;
        bool stagingWind, pendingForcingBake;
        float debt, windBlend;
        bool rendered, failed;
        double stagedClock;
        Vector4 pendingNorth, pendingSouth, pendingPhase, visibleNorth, visibleSouth, visiblePhase;
        FlatRegionalCloudField regionalClimate;
        long? stagingClimateStep;
        int stagingRevision;
        public long? RegionalClimateStep { get; private set; }
        bool weatherWasHidden;
        readonly Vector4[] flashes = new Vector4[4];
        FlatLightningVisuals lightning;
        public Texture2D DynamicWind { get { return wind; } }
        public bool DynamicReady { get { return Ready; } }
        public double RenderClock { get { return stagedClock; } }
        public Vector4[] LightningFlashes { get { return flashes; } }

        public void Initialize(WorldData data)
        {
            world = data;
            forcingScheduler.Bind(world);
            pendingForcingBake = false;
            regionalClimate?.Dispose();
            regionalClimate = null;
            lightning = new FlatLightningVisuals(data);
            wind = WeatherTexture("Visual cloud drift");
            windBack = WeatherTexture("Cloud drift staging");
            climateWind = WeatherTexture("Seasonal prevailing winds");
            for (int x = 0; x < ClimateWidth; x++)
            {
                float lon = ((x + .5f) / ClimateWidth - .5f) * 2 * Mathf.PI;
                longitudeBasis[x] = new Vector2(Mathf.Sin(lon), Mathf.Cos(lon));
            }
            for (int y = 0; y < ClimateHeight; y++)
            {
                float lat = ((y + .5f) / ClimateHeight - .5f) * Mathf.PI;
                latitudeBasis[y] = new Vector2(Mathf.Sin(lat), Mathf.Cos(lat));
            }
            var season = new FlatMonthlyForcing(world.Day);
            windBlend = season.SegmentBlend;
            UploadWindSync(season);
            Shader shader = Resources.Load<Shader>("ORBIS/FlatWorld/FlatSkyBake");
            Shader columnShader = Resources.Load<Shader>("ORBIS/FlatWorld/HPCloudColumn");
            if (!shader || !shader.isSupported || !columnShader || !columnShader.isSupported)
            {
                failed = true;
                Status = "云层缓存 Shader 不支持：云层已降级关闭。";
                Debug.LogWarning(Status);
                return;
            }
            bake = new Material(shader) { name = "Perf3.2 shared climate cloud cache" };
            hpColumn = new Material(columnShader) { name = "HP radial cloud cache" };
            hpNoise = OrbisCloudNoise.Create();
            hpDetail = OrbisCloudNoise.CreateDetail();
            hpHigh = OrbisCloudNoise.CreateHighDetail();
            hpColumn.SetTexture("_CloudNoise", hpNoise);
            hpColumn.SetTexture("_CloudDetailNoise", hpDetail);
            hpColumn.SetTexture("_HPHighDetail", hpHigh);
            hpColumn.SetTexture("_ScrollHeightTex", Texture2D.blackTexture);
            hpColumn.SetFloat("_ScrollHeightScale", 0);
            hpColumn.SetVector("_ScrollSourceSize", new Vector4(512, 256, 0, 0));
            hpColumn.SetFloat("_CloudHeightScale", OrbisWeatherField.HeightScale);
            hpColumn.SetFloat("_Density", 1);
            hpColumn.SetFloat("_Cover", 1);
            climate = new Texture2D(ClimateWidth, ClimateHeight, TextureFormat.RGBA32, false, true)
                { name = "Monthly visual weather", filterMode = FilterMode.Bilinear, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            glebaForcing = new Texture2D(ClimateWidth, ClimateHeight, TextureFormat.RGBA32, false, true)
                { name = "Gleba synoptic cloud forcing", filterMode = FilterMode.Bilinear, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            glebaAux = new Texture2D(ClimateWidth, ClimateHeight, TextureFormat.RGBA32, false, true)
                { name = "Gleba stratocumulus and orographic cloud aux", filterMode = FilterMode.Bilinear, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            glebaUpperWind = WeatherTexture("Gleba upper-level Rossby and anvil wind");
            bake.SetTexture("_ClimateTex", climate);
            bake.SetTexture("_ClimateWindTex", climateWind);
            bake.SetTexture("_CloudSynopticTex", glebaForcing);
            bake.SetTexture("_CloudSynopticAuxTex", glebaAux);
            bake.SetTexture("_UpperWindTex", glebaUpperWind);
            bake.SetFloat("_GlebaCloudEnable", 1f);
            hpColumn.SetTexture("_ScrollCloudWindTex", wind);
            hpColumn.SetTexture("_ClimateTex", climate);
            try
            {
                Resize(512);
                forcingScheduler.Prefetch(world, world.Day);
            }
            catch (Exception ex) { failed = true; Status = "云缓存不可用，地表继续运行：" + ex.Message; Debug.LogException(ex); }
        }

        static Texture2D WeatherTexture(string label)
        {
            return new Texture2D(ClimateWidth, ClimateHeight, TextureFormat.RGBAHalf, false, true)
                { name = label, filterMode = FilterMode.Bilinear, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
        }

        static void SealPole(Color[] values, int w, int h)
        {
            for (int side = 0; side < 2; side++)
            {
                int start = side == 0 ? 0 : (h - 1) * w;
                Color mean = Color.clear;
                for (int x = 0; x < w; x++) mean += values[start + x];
                mean /= w;
                for (int x = 0; x < w; x++) values[start + x] = mean;
            }
        }

        void Resize(int width)
        {
            width = width >= 1024 ? 1024 : 512;
            if (Texture && CacheWidth == width) return;
            ReleaseCache(Texture);
            ReleaseCache(ColumnTexture);
            ReleaseCache(weatherBack);
            ReleaseCache(columnBack);
            ReleaseCache(columnStrip);
            pendingStrip = -1;
            CacheWidth = width;
            int height = width / 2;
            // The cheap weather pass resolves thin feeder bands at twice the
            // linear resolution of the ray-integrated shadow/precipitation map.
            Texture = new RenderTexture(width * 2, height * 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = "Low and high cloud cover", filterMode = FilterMode.Bilinear, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false };
            if (!Texture.Create()) throw new InvalidOperationException("Cannot allocate 2D cloud render target.");
            var columnDescriptor = Texture.descriptor;
            columnDescriptor.width = width;
            columnDescriptor.height = height;
            // Keep nearby cloud coverage for local light shafts alongside the exact radial column.
            columnDescriptor.useMipMap = true;
            columnDescriptor.autoGenerateMips = false;
            columnDescriptor.mipCount = 5;
            ColumnTexture = new RenderTexture(columnDescriptor)
                { name = "HP integrated low/high map clouds", filterMode = FilterMode.Bilinear, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            if (!ColumnTexture.Create()) throw new InvalidOperationException("Cannot allocate HP cloud column cache.");
            weatherBack = NewCache(Texture.descriptor, "Weather staging");
            columnBack = NewCache(ColumnTexture.descriptor, "Cloud column staging");
            var strip = ColumnTexture.descriptor;
            strip.height = height / ColumnStrips;
            strip.useMipMap = false;
            strip.mipCount = 1;
            columnStrip = NewCache(strip, "Cloud column update strip");
            bake.SetTexture("_ClimateTex", climate);
            rendered = false;
            pendingForcingBake = forcingScheduler.HasPublished;
            debt = 1;
            RegionalClimateStep = null;
        }

        static RenderTexture NewCache(RenderTextureDescriptor descriptor, string label)
        {
            var result = new RenderTexture(descriptor)
                { name = label, filterMode = FilterMode.Bilinear, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            if (!result.Create())
            {
                Destroy(result);
                throw new InvalidOperationException("Cannot allocate " + label);
            }
            return result;
        }

        void ReleaseCache(RenderTexture target)
        {
            if (!target) return;
            if (RenderTexture.active == target) RenderTexture.active = null;
            target.Release();
            Destroy(target);
        }

        static void RenderCache(RenderTexture target, Material material)
        {
            RenderTexture previous = RenderTexture.active;
            bool previousSRGB = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(Texture2D.blackTexture, target, material, 0);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSRGB; }
        }

        void RenderColumnStrip()
        {
            hpColumn.SetVector("_ColumnUVTransform", new Vector4(0, pendingStrip / (float)ColumnStrips, 1, 1f / ColumnStrips));
            RenderCache(columnStrip, hpColumn);
            Graphics.CopyTexture(columnStrip, 0, 0, 0, 0, columnStrip.width, columnStrip.height, columnBack, 0, 0, 0, pendingStrip * columnStrip.height);
            if (++pendingStrip < ColumnStrips) return;
            columnBack.GenerateMips();
            // Publish only a complete weather/optical-column pair. No visible stripes.
            var old = Texture;
            Texture = weatherBack;
            weatherBack = old;
            old = ColumnTexture;
            ColumnTexture = columnBack;
            columnBack = old;
            if (stagingWind)
            {
                var oldWind = wind;
                wind = windBack;
                windBack = oldWind;
                forcingScheduler.MarkCloudWindPublished(stagingWindDay, stagingRevision, stagingWindGeneration);
            }
            stagedClock = hpColumn.GetFloat("_ScrollWeatherTime");
            visibleNorth = pendingNorth;
            visibleSouth = pendingSouth;
            visiblePhase = pendingPhase;
            Shader.SetGlobalVector("_OrbisCycloneNorth", visibleNorth);
            Shader.SetGlobalVector("_OrbisCycloneSouth", visibleSouth);
            Shader.SetGlobalVector("_OrbisCyclonePhase", visiblePhase);
            RegionalClimateStep = stagingClimateStep;
            pendingStrip = -1;
            rendered = true;
            Status = "云层 " + Texture.width + "×" + Texture.height + " · 云影和雨雪 " + CacheWidth + "×" + (CacheWidth / 2) +
                (RegionalClimateStep.HasValue ? " · 区域气候统计" : " · 月气候");
        }

        void ApplySeasonalWindPixels(float[] windRgba)
        {
            for (int i = 0, p4 = 0; i < seasonalWindPixels.Length; i++, p4 += 4)
                seasonalWindPixels[i] = new Color(windRgba[p4], windRgba[p4 + 1], windRgba[p4 + 2], windRgba[p4 + 3]);
            climateWind.SetPixels(seasonalWindPixels);
            climateWind.Apply(false, false);
        }

        void UploadWindSync(FlatMonthlyForcing season)
        {
            FlatSkyForcingCache.PackSeasonalWindRgba(world, season, ClimateWidth, ClimateHeight, hiddenWindRgba);
            ApplySeasonalWindPixels(hiddenWindRgba);
            forcingScheduler.MarkInitialWindUploaded(world, season);
        }

        /// <summary>
        /// Uploads already-computed background forcing buffers on the Unity main thread with zero
        /// heavy climate computation and zero per-refresh array allocations.
        /// </summary>
        void UploadForcingPayload(FlatSkyForcingPayload payload)
        {
            if (payload.HasSeasonalWind)
                ApplySeasonalWindPixels(payload.SeasonalWindRgba);

            if (payload.HasClimateSegment)
            {
                var cs = payload.ClimatePoleSouth;
                var cn = payload.ClimatePoleNorth;
                bake.SetVector("_ClimatePoleSouth", new Vector4(cs[0], cs[1], cs[2], cs[3]));
                bake.SetVector("_ClimatePoleNorth", new Vector4(cn[0], cn[1], cn[2], cn[3]));
                bake.SetFloat("_ClimateRows", payload.Height);
                climate.LoadRawTextureData(payload.MonthlyClimateRgba);
                climate.Apply(false, false);

                if (glebaUpperWind.format == TextureFormat.RGBAHalf)
                    glebaUpperWind.SetPixelData(payload.UpperWindHalf, 0);
                else
                {
                    if (upperWindFallback == null || upperWindFallback.Length != payload.UpperWindHalf.Length)
                        upperWindFallback = new float[payload.UpperWindHalf.Length];
                    for (int k = 0; k < upperWindFallback.Length; k++)
                        upperWindFallback[k] = Mathf.HalfToFloat(payload.UpperWindHalf[k]);
                    glebaUpperWind.SetPixelData(upperWindFallback, 0);
                }
                glebaUpperWind.Apply(false, false);
            }

            if (payload.HasSynopticForcing)
            {
                glebaForcing.LoadRawTextureData(payload.ForcingRgba);
                glebaForcing.Apply(false, false);
                glebaAux.LoadRawTextureData(payload.AuxRgba);
                glebaAux.Apply(false, false);
                var fs = payload.ForcingPoleSouth;
                var fn = payload.ForcingPoleNorth;
                bake.SetVector("_GlebaForcingPoleSouth", new Vector4(fs[0], fs[1], fs[2], fs[3]));
                bake.SetVector("_GlebaForcingPoleNorth", new Vector4(fn[0], fn[1], fn[2], fn[3]));
                bake.SetVector("_GlebaCirculationParams", new Vector4(payload.HadleyRad, payload.PolarRad, payload.RossbyWaveK, payload.RegimeCode));
                bake.SetFloat("_GlebaCloudEnable", 1f);
            }
        }

        void StageCloudWind(bool forceStage)
        {
            var cache = forcingScheduler.Cache;
            stagingWindDay = world.Day;
            stagingWindGeneration = cache.WindUploadGeneration;
            stagingWind = forceStage ||
                stagingWindDay != cache.VisibleWindDay ||
                stagingRevision != cache.VisibleWindRevision ||
                stagingWindGeneration != cache.VisibleWindGeneration;
            if (stagingWind)
            {
                for (int y = 0; y < ClimateHeight; y++)
                for (int x = 0; x < ClimateWidth; x++)
                {
                    int i = y * ClimateWidth + x;
                    Color ends = seasonalWindPixels[i];
                    float east = Mathf.Lerp(ends.r, ends.b, windBlend);
                    float north = Mathf.Lerp(ends.g, ends.a, windBlend);
                    var lon = longitudeBasis[x];
                    var lat = latitudeBasis[y];
                    var velocity = Vector3.ClampMagnitude(new Vector3(
                        east * lon.y - north * lat.x * lon.x,
                        north * lat.y,
                        -east * lon.x - north * lat.x * lon.y) * .014f, .025f);
                    windPixels[i] = new Color(velocity.x, velocity.y, velocity.z, 0);
                }
                SealPole(windPixels, ClimateWidth, ClimateHeight);
                windBack.SetPixels(windPixels);
                windBack.Apply(false, false);
            }
            hpColumn.SetTexture("_ScrollCloudWindTex", stagingWind ? windBack : wind);
        }

        static Vector4 StormVector(FlatCycloneTrack track)
        {
            if (!track.Tracked || track.Strength <= 0) return Vector4.zero;
            var p = FlatWeatherSimulation.Direction(track.U, track.V);
            return new Vector4((float)p.X, (float)p.Y, (float)p.Z, (float)track.Strength);
        }

        void SampleCyclones(double calendarDay, out Vector4 northVec, out Vector4 southVec, out Vector4 phaseVec)
        {
            var state = world.Cyclones;
            var north = state == null ? default : state.Sample(true, calendarDay);
            var south = state == null ? default : state.Sample(false, calendarDay);
            northVec = StormVector(north);
            southVec = StormVector(south);
            phaseVec = new Vector4((float)(north.Phase % (2 * Math.PI)), (float)(south.Phase % (2 * Math.PI)), 0, 0);
        }

        void StageCyclones(Vector4 northVec, Vector4 southVec, Vector4 phaseVec)
        {
            pendingNorth = northVec;
            pendingSouth = southVec;
            pendingPhase = phaseVec;
            bake.SetVector("_CycloneNorth", pendingNorth);
            bake.SetVector("_CycloneSouth", pendingSouth);
            bake.SetVector("_CyclonePhase", pendingPhase);
            hpColumn.SetVector("_OrbisCycloneNorth", pendingNorth);
            hpColumn.SetVector("_OrbisCycloneSouth", pendingSouth);
            hpColumn.SetVector("_OrbisCyclonePhase", pendingPhase);
        }

        public void Tick(float delta, bool needed, bool animate, bool autoSun, int width, double calendarDay, Vector2 focus = default, bool local = false)
        {
            lightning.Tick(delta, needed && animate && !failed, focus.x, focus.y, local);
            for (int i = 0; i < flashes.Length; i++)
            {
                var flash = lightning.Flashes[i];
                flashes[i] = new Vector4(flash.X, flash.Y, flash.Z, flash.Strength);
            }
            if (animate) Clock += Math.Max(0, delta);
            // Vegetation shares prevailing winds even while cloud rendering sleeps.
            var season = new FlatMonthlyForcing(world.Day);
            windBlend = season.SegmentBlend;
            if (!needed)
            {
                forcingScheduler.Poll(world, world.Day, cloudsNeeded: false, allowCommit: false, out _, out bool hiddenWindNeedsSync);
                if (hiddenWindNeedsSync) UploadWindSync(season);
                weatherWasHidden = true;
                Status = "云缓存休眠；晨昏/大气开关独立。";
                return;
            }
            if (failed) return;
            try
            {
                Resize(width);
                // A sleeping cache may contain half of an old storm. Resume at today's track.
                bool resumed = weatherWasHidden;
                if (resumed)
                {
                    pendingStrip = -1;
                    weatherWasHidden = false;
                    forcingScheduler.NotifyCloudsResumed();
                }
                if (pendingStrip >= 0 && (stagingRevision != world.TerrainRevision ||
                    forcingScheduler.ShouldAbortPendingStrip(world, world.Day) ||
                    (stagingClimateStep.HasValue && stagingClimateStep.Value != (long)Math.Floor(world.Day * 4))))
                    pendingStrip = -1;
                if (pendingStrip >= 0)
                {
                    // Keep advancing/coalescing background forcing without overwriting _ClimateTex mid-strip.
                    forcingScheduler.Poll(world, world.Day, cloudsNeeded: true, allowCommit: false, out _, out _);
                    RenderColumnStrip();
                    return;
                }

                if (forcingScheduler.Poll(world, world.Day, cloudsNeeded: true, allowCommit: true, out var readyPayload, out _))
                {
                    UploadForcingPayload(readyPayload);
                    pendingForcingBake = true;
                }

                if (regionalClimate == null) regionalClimate = new FlatRegionalCloudField(world);
                bool fieldReady = regionalClimate.Prepare(world.Day, out bool changed);

                // Never publish an incomplete or segment-mismatched cloud bake while the matching
                // background climate/forcing bundle is still building; keep the last complete pair on screen.
                if (!forcingScheduler.IsSegmentCurrent(world, world.Day))
                {
                    if (!rendered) Status = "云层气候强迫后台构建中…";
                    return;
                }

                // Keep the last complete pair on screen while the next six-hour field is computed.
                // A newly ready field also wakes a paused sky after the first neutral bake.
                if (!fieldReady && rendered) return;

                SampleCyclones(calendarDay, out var nextNorth, out var nextSouth, out var nextPhase);
                bool cycloneChanged = nextNorth != visibleNorth || nextSouth != visibleSouth || nextPhase != visiblePhase;
                var cache = forcingScheduler.Cache;
                bool windNeedsStage = world.Day != cache.VisibleWindDay ||
                    world.TerrainRevision != cache.VisibleWindRevision ||
                    cache.WindUploadGeneration != cache.VisibleWindGeneration;
                bool forcingChanged = pendingForcingBake || windNeedsStage;
                bool regionalChanged = changed || RegionalClimateStep != regionalClimate.Step;
                if (!animate && rendered && !forcingChanged && !resumed && !regionalChanged && !cycloneChanged)
                { Status = RegionalClimateStep.HasValue ? "云缓存暂停 · 区域气候统计" : "云缓存暂停 · 月气候"; return; }
                debt += Mathf.Max(0, delta);
                if (debt < .5f && rendered && !forcingChanged && !resumed && !regionalChanged && !cycloneChanged) return;
                debt = 0;
                pendingForcingBake = false;
                regionalClimate.Bind(bake);
                stagingClimateStep = regionalClimate.Step;
                stagingRevision = world.TerrainRevision;
                StageCloudWind(windNeedsStage);
                StageCyclones(nextNorth, nextSouth, nextPhase);
                bake.SetFloat("_VisualTime", (float)Clock);
                bake.SetFloat("_MonthBlend", season.SegmentBlend);
                hpColumn.SetFloat("_MonthBlend", season.SegmentBlend);
                RenderCache(weatherBack, bake);
                hpColumn.SetTexture("_ScrollWeatherTex", weatherBack);
                hpColumn.SetFloat("_ScrollWeatherTime", (float)Clock);
                pendingStrip = 0;
                RenderColumnStrip();
            }
            catch (Exception ex) { failed = true; rendered = false; Status = "云缓存已安全关闭：" + ex.Message; Debug.LogException(ex); }
        }

        public Vector3 Sun(float manualAngle, double calendarDay = double.NaN, bool automatic = false)
        {
            double day = double.IsNaN(calendarDay) ? world.Day : calendarDay;
            float season = (float)((day % 360 - 80) * Math.PI * 2 / 360);
            float declination = Mathf.Asin(Mathf.Sin(23.44f * Mathf.Deg2Rad) * Mathf.Sin(season));
            // Gameplay advances days in large increments; never use that directly as
            // solar rotation. Otherwise six days/second would cause a flashing map.
            float angle = automatic ? (float)FlatSolarClock.SolarLongitude(day, manualAngle) : manualAngle * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(angle) * Mathf.Cos(declination), Mathf.Sin(declination), Mathf.Cos(angle) * Mathf.Cos(declination));
        }

        public void Bind(Material target, Vector3 sun, bool clouds, bool shadows, bool atmosphere, bool dayNight, bool rain, bool lightning, bool nightLights, float opacity)
        {
            if (!target) return;
            target.SetTexture("_SkyTex", Ready ? (UnityEngine.Texture)ColumnTexture : Texture2D.blackTexture);
            target.SetFloat("_SkyReady", Ready ? 1 : 0);
            target.SetVector("_FlatSun", sun);
            target.SetFloat("_SkyClock", (float)Clock);
            target.SetFloat("_FlatClouds", clouds ? 1 : 0);
            target.SetTexture("_VegetationWindTex", climateWind ? climateWind : Texture2D.blackTexture);
            target.SetFloat("_VegetationWindReady", climateWind ? 1 : 0);
            target.SetFloat("_VegetationWindBlend", windBlend);
            target.SetVector("_CycloneNorth", visibleNorth);
            target.SetVector("_CycloneSouth", visibleSouth);
            target.SetFloat("_FlatCloudShadows", shadows ? 1 : 0);
            target.SetFloat("_FlatAtmosphere", atmosphere ? 1 : 0);
            target.SetFloat("_FlatDayNight", dayNight ? 1 : 0);
            target.SetFloat("_FlatRain", rain ? 1 : 0);
            target.SetFloat("_FlatLightning", lightning ? 1 : 0);
            target.SetFloat("_FlatNightLights", nightLights ? 1 : 0);
            target.SetFloat("_CloudOpacity", Mathf.Clamp01(opacity));
            target.SetVectorArray("_OrbisLightning", flashes);
        }

        void OnDestroy()
        {
            forcingScheduler.Dispose();
            Shader.SetGlobalVector("_OrbisCycloneNorth", Vector4.zero);
            Shader.SetGlobalVector("_OrbisCycloneSouth", Vector4.zero);
            Shader.SetGlobalVector("_OrbisCyclonePhase", Vector4.zero);
            regionalClimate?.Dispose();
            ReleaseCache(weatherBack);
            ReleaseCache(columnBack);
            ReleaseCache(columnStrip);
            ReleaseCache(Texture);
            ReleaseCache(ColumnTexture);
            if (climate) Destroy(climate);
            if (glebaForcing) Destroy(glebaForcing);
            if (glebaAux) Destroy(glebaAux);
            if (glebaUpperWind) Destroy(glebaUpperWind);
            if (wind) Destroy(wind);
            if (windBack) Destroy(windBack);
            if (climateWind) Destroy(climateWind);
            if (bake) Destroy(bake);
            if (hpColumn) Destroy(hpColumn);
            if (hpNoise) Destroy(hpNoise);
            if (hpDetail) Destroy(hpDetail);
            if (hpHigh) Destroy(hpHigh);
        }
    }
}
