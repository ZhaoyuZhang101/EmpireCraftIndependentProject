using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Orbis.Flat
{
    // The worker owns the model and staging pixels. Unity only uploads a completed
    // field that still belongs to the requested terrain and committed weather step.
    internal sealed class FlatRegionalCloudField : IDisposable
    {
        const int Width = 64, Height = Width / 2;
        readonly WorldData world;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly Color32[] pixels = new Color32[Width * Height];
        readonly ushort[] precipHalf = new ushort[Width * Height * 4];
        readonly ushort[] uploadedPrecipHalf = new ushort[Width * Height * 4];
        float[] precipFallback;
        float stagedSouthRain, stagedSouthSfc, stagedNorthRain, stagedNorthSfc;
        readonly FlatMoistureCloudBudget moistureBudget = new FlatMoistureCloudBudget();
        FlatClimateStatistics model;
        Task worker;
        long requested = long.MinValue;
        int revision;
        FlatMoistureState requestedMoisture;
        bool requestedHadStorms, requestedSpinUpAtTick;
        FlatCycloneTrack requestedNorth, requestedSouth, requestedPrevNorth, requestedPrevSouth;
        bool available, hasUploadedTexture;
        long uploadedTick = long.MinValue;
        int uploadedRevision = -1;
        FlatMoistureState uploadedMoisture;
        Texture2D texture, precipTexture;
        Vector4 poles, precipPoles;
        internal long? Step => available ? requested : (long?)null;
        internal bool HasValidField => available || hasUploadedTexture;
        internal Texture2D PrecipitationTexture => HasValidField ? precipTexture : null;
        internal Vector4 PrecipitationPoles => HasValidField ? precipPoles : Vector4.zero;

        internal FlatRegionalCloudField(WorldData world) { this.world = world; }

        bool SameRequestedStorms(long tick)
        {
            var c = world.Cyclones;
            if (c == null) return !requestedHadStorms;
            if (!requestedHadStorms) return false;
            if (c.StepIndex != tick)
                return requestedSpinUpAtTick && requested == tick;
            if (requestedSpinUpAtTick) return false;
            return FlatSkyForcingCache.SameCycloneTrack(c.North, requestedNorth) &&
                   FlatSkyForcingCache.SameCycloneTrack(c.South, requestedSouth) &&
                   FlatSkyForcingCache.SameCycloneTrack(c.PreviousNorth, requestedPrevNorth) &&
                   FlatSkyForcingCache.SameCycloneTrack(c.PreviousSouth, requestedPrevSouth);
        }

        internal bool Prepare(double day, out bool changed)
        {
            long tick = (long)Math.Floor(day * 4);
            if (hasUploadedTexture && (uploadedRevision != world.TerrainRevision ||
                !ReferenceEquals(uploadedMoisture, world.Moisture) || tick < uploadedTick))
            {
                hasUploadedTexture = false;
                uploadedTick = long.MinValue;
            }
            bool current = requested == tick && revision == world.TerrainRevision &&
                ReferenceEquals(requestedMoisture, world.Moisture) && SameRequestedStorms(tick);
            changed = false;
            if (worker != null)
            {
                if (!worker.IsCompleted) return false;
                bool workerMatchesProvenance = revision == world.TerrainRevision &&
                    ReferenceEquals(requestedMoisture, world.Moisture) &&
                    requested <= tick && requested >= uploadedTick;
                try
                {
                    worker.GetAwaiter().GetResult();
                    if (current || workerMatchesProvenance)
                    {
                        if (!texture) texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true) {
                            name = "Regional climate response for HP clouds", filterMode = FilterMode.Bilinear,
                            wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp
                        };
                        texture.SetPixels32(pixels);
                        texture.Apply(false, false);
                        if (!precipTexture)
                        {
                            var fmt = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf) ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat;
                            precipTexture = new Texture2D(Width, Height, fmt, false, true)
                            {
                                name = "Regional 6h cloud rain-out and surface fallout (kg/m2/6h)",
                                filterMode = FilterMode.Bilinear,
                                wrapModeU = TextureWrapMode.Repeat,
                                wrapModeV = TextureWrapMode.Clamp
                            };
                        }
                        if (precipTexture.format == TextureFormat.RGBAHalf)
                        {
                            precipTexture.SetPixelData(precipHalf, 0);
                        }
                        else
                        {
                            if (precipFallback == null || precipFallback.Length != precipHalf.Length)
                                precipFallback = new float[precipHalf.Length];
                            for (int k = 0; k < precipHalf.Length; k++)
                                precipFallback[k] = FlatMoistureCloudBudget.HalfBitsToFloat(precipHalf[k]);
                            precipTexture.SetPixelData(precipFallback, 0);
                        }
                        precipTexture.Apply(false, false);
                        Buffer.BlockCopy(precipHalf, 0, uploadedPrecipHalf, 0, precipHalf.Length * sizeof(ushort));
                        precipPoles = new Vector4(stagedSouthRain, stagedSouthSfc, stagedNorthRain, stagedNorthSfc);
                        var south = Pole(0);
                        var north = Pole(Height - 1);
                        poles = new Vector4(south.x, south.y, north.x, north.y);
                        hasUploadedTexture = true;
                        uploadedTick = requested;
                        uploadedRevision = revision;
                        uploadedMoisture = requestedMoisture;
                        available = current;
                    }
                }
                catch (Exception ex)
                {
                    available = false;
                    Debug.LogWarning("区域大气统计本期回退到月气候云场：" + ex.Message);
                }
                worker = null;
                changed = current || workerMatchesProvenance;
            }
            if (current) return true;
            bool rebuildModel = !ReferenceEquals(requestedMoisture, world.Moisture);
            available = false;
            requested = tick;
            revision = world.TerrainRevision;
            requestedMoisture = world.Moisture;
            var storms = world.Cyclones?.Clone();
            requestedHadStorms = storms != null;
            requestedSpinUpAtTick = storms != null && storms.StepIndex != tick;
            if (storms != null && !requestedSpinUpAtTick)
            {
                requestedNorth = storms.North; requestedSouth = storms.South;
                requestedPrevNorth = storms.PreviousNorth; requestedPrevSouth = storms.PreviousSouth;
            }
            else
            {
                requestedNorth = requestedSouth = requestedPrevNorth = requestedPrevSouth = default;
            }
            bool spinUpAtTick = requestedSpinUpAtTick;
            var token = lifetime.Token;
            worker = Task.Run(() => Calculate(tick * .25, storms, spinUpAtTick, rebuildModel, token), token);
            return false;
        }

        void Calculate(double day, FlatCycloneWeather storms, bool spinUpAtTick, bool rebuildModel, CancellationToken cancel)
        {
            if (spinUpAtTick)
                storms = FlatCycloneWeather.SpinUpAt(world, day);
            if (model == null || rebuildModel) model = new FlatClimateStatistics(world, cancel, Width, atmosphereOnly: true);
            model.Refresh(day, storms, cancel);
            moistureBudget.Solve(world, day, storms);
            moistureBudget.PackPrecipitationHalf(precipHalf, out stagedSouthRain, out stagedSouthSfc, out stagedNorthRain, out stagedNorthSfc);
            for (int i = 0; i < pixels.Length; i++)
            {
                var response = RegionalCloudResponse.FromSynoptic(model.Cells[i], i, moistureBudget, storms, day);
                pixels[i] = new Color32(Byte(Math.Max(0, response.Cover)), Byte(Math.Max(0, -response.Cover)),
                    Byte(response.Convection), Byte(response.Evaporation));
            }
        }

        static byte Byte(float unit) => (byte)(unit * 255 + .5f);

        Vector2 Pole(int row)
        {
            int moisture = 0, convection = 0;
            for (int x = 0; x < Width; x++)
            {
                var p = pixels[row * Width + x];
                moisture += p.r - p.g;
                convection += p.b;
            }
            return new Vector2(moisture, convection) / (255 * Width);
        }

        internal void Bind(Material bake)
        {
            bool valid = (available || hasUploadedTexture) && texture;
            bake.SetTexture("_RegionalCloudResponse", valid ? texture : Texture2D.blackTexture);
            bake.SetFloat("_RegionalEvapReady", valid ? 1f : 0f);
            bake.SetVector("_RegionalCloudPoles", valid ? poles : Vector4.zero);
            BindPrecipitation(bake);
        }

        internal void BindPrecipitation(Material target)
        {
            if (!target) return;
            bool valid = (available || hasUploadedTexture) && precipTexture;
            target.SetTexture("_RegionalPrecipDiagTex", valid ? precipTexture : Texture2D.blackTexture);
            target.SetVector("_RegionalPrecipPoles", valid ? precipPoles : Vector4.zero);
        }

        internal bool TrySampleUploadedPrecipitation(double u, double v, out RegionalPrecipitationDiagnostic diagnostic)
        {
            if (!HasValidField)
            {
                diagnostic = default;
                return false;
            }
            diagnostic = FlatMoistureCloudBudget.SamplePackedPrecipitationHalf(uploadedPrecipHalf, u, v);
            return true;
        }

        public void Dispose()
        {
            lifetime.Cancel();
            if (worker == null) lifetime.Dispose();
            else worker.ContinueWith(t => { if (t.IsFaulted) _ = t.Exception; lifetime.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (texture) UnityEngine.Object.Destroy(texture);
            if (precipTexture) UnityEngine.Object.Destroy(precipTexture);
        }
    }
}
