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
        readonly FlatMoistureCloudBudget moistureBudget = new FlatMoistureCloudBudget();
        FlatClimateStatistics model;
        Task worker;
        long requested = long.MinValue;
        int revision;
        FlatMoistureState requestedMoisture;
        bool requestedHadStorms;
        FlatCycloneTrack requestedNorth, requestedSouth, requestedPrevNorth, requestedPrevSouth;
        bool available, hasUploadedTexture;
        long uploadedTick = long.MinValue;
        int uploadedRevision = -1;
        FlatMoistureState uploadedMoisture;
        Texture2D texture;
        Vector4 poles;
        internal long? Step => available ? requested : (long?)null;
        internal bool HasValidField => available || hasUploadedTexture;

        internal FlatRegionalCloudField(WorldData world) { this.world = world; }

        bool SameRequestedStorms(long tick)
        {
            var c = world.Cyclones;
            bool hasStorms = c != null && c.StepIndex == tick;
            if (hasStorms != requestedHadStorms) return false;
            if (!hasStorms) return true;
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
            requestedHadStorms = storms != null && storms.StepIndex == tick;
            if (requestedHadStorms)
            {
                requestedNorth = storms.North; requestedSouth = storms.South;
                requestedPrevNorth = storms.PreviousNorth; requestedPrevSouth = storms.PreviousSouth;
            }
            else
            {
                requestedNorth = requestedSouth = requestedPrevNorth = requestedPrevSouth = default;
            }
            var token = lifetime.Token;
            worker = Task.Run(() => Calculate(tick * .25, storms, rebuildModel, token), token);
            return false;
        }

        void Calculate(double day, FlatCycloneWeather storms, bool rebuildModel, CancellationToken cancel)
        {
            if (model == null || rebuildModel) model = new FlatClimateStatistics(world, cancel, Width, atmosphereOnly: true);
            model.Refresh(day, storms, cancel);
            moistureBudget.Solve(world, day, storms);
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
            bake.SetVector("_RegionalCloudPoles", valid ? poles : Vector4.zero);
        }

        public void Dispose()
        {
            lifetime.Cancel();
            if (worker == null) lifetime.Dispose();
            else worker.ContinueWith(t => { if (t.IsFaulted) _ = t.Exception; lifetime.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (texture) UnityEngine.Object.Destroy(texture);
        }
    }
}
