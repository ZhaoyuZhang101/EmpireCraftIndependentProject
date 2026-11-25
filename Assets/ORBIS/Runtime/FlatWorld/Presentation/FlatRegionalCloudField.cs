using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Orbis.Flat
{
    // The worker owns the statistical/moisture models and private worker buffers.
    // Unity uploads completed snapshots into a 3-slot pool (Published, Staged, UploadFree)
    // so published GPU/CPU buffers remain strictly immutable until the final HP column strip commits.
    internal sealed class FlatRegionalCloudField : IDisposable
    {
        const int Width = FlatRegionalSnapshotPool.Width, Height = FlatRegionalSnapshotPool.Height;
        readonly WorldData world;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly FlatRegionalSnapshotPool pool = new FlatRegionalSnapshotPool();
        readonly Texture2D[] cloudTextures = new Texture2D[FlatRegionalSnapshotPool.SlotCount];
        readonly Texture2D[] precipTextures = new Texture2D[FlatRegionalSnapshotPool.SlotCount];
        readonly byte[] workerCloudRgba = new byte[Width * Height * 4];
        readonly ushort[] workerPrecipHalf = new ushort[Width * Height * 4];
        float[] precipFallback;
        float workerSouthRain, workerSouthSfc, workerNorthRain, workerNorthSfc;
        readonly FlatMoistureCloudBudget moistureBudget = new FlatMoistureCloudBudget();
        FlatClimateStatistics model;
        Task worker;
        long workerTick = long.MinValue;
        int workerRevision = -1;
        FlatMoistureState workerMoisture;
        bool workerHadStorms, workerSpinUpAtTick;
        FlatCycloneTrack workerNorth, workerSouth, workerPrevNorth, workerPrevSouth;

        internal FlatRegionalSnapshotPool Pool => pool;
        internal ulong LatestUploadedGeneration => pool.LatestUploadedGeneration;
        internal long? LatestUploadedTick => pool.LatestUploadedIndex >= 0 ? (long?)pool.LatestUploadedTick : null;

        internal FlatRegionalCloudField(WorldData world) { this.world = world; }

        internal bool HasValidLatestFor(WorldData targetWorld, double day) =>
            pool.HasValidLatestFor(targetWorld, (long)Math.Floor(day * 4));

        internal bool HasPublishedFor(WorldData targetWorld)
        {
            if (!pool.HasPublishedFor(targetWorld)) return false;
            int p = pool.PublishedIndex;
            return cloudTextures[p] && precipTextures[p];
        }

        internal long? GetPublishedTick(WorldData targetWorld) =>
            HasPublishedFor(targetWorld) ? pool.GetPublishedTick(targetWorld) : null;

        internal ulong GetPublishedGeneration(WorldData targetWorld) =>
            HasPublishedFor(targetWorld) ? pool.GetPublishedGeneration(targetWorld) : 0UL;

        internal bool Prepare(double day, out bool changed)
        {
            long tick = (long)Math.Floor(day * 4);
            pool.InvalidateLatestIfIncompatible(world, tick);
            changed = false;

            if (worker != null)
            {
                if (!worker.IsCompleted) return false;
                bool exactMatch = workerTick == tick &&
                    workerRevision == world.TerrainRevision &&
                    ReferenceEquals(workerMoisture, world.Moisture) &&
                    FlatRegionalSnapshotPool.SameStorms(
                        world.Cyclones, tick, workerTick, workerHadStorms, workerSpinUpAtTick,
                        in workerNorth, in workerSouth, in workerPrevNorth, in workerPrevSouth);
                bool coalescedMatch = !exactMatch &&
                    workerTick < tick &&
                    workerRevision == world.TerrainRevision &&
                    ReferenceEquals(workerMoisture, world.Moisture) &&
                    workerTick >= pool.LatestUploadedTick;
                try
                {
                    worker.GetAwaiter().GetResult();
                    if (exactMatch || coalescedMatch)
                    {
                        UploadCompletedWorkerToFreeSlot();
                        changed = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("区域大气统计本期回退到月气候云场：" + ex.Message);
                }
                worker = null;
            }

            if (pool.HasExactLatestFor(world, tick))
                return true;

            bool rebuildModel = !ReferenceEquals(workerMoisture, world.Moisture);
            workerTick = tick;
            workerRevision = world.TerrainRevision;
            workerMoisture = world.Moisture;
            var storms = world.Cyclones?.Clone();
            workerHadStorms = storms != null;
            workerSpinUpAtTick = storms != null && storms.StepIndex != tick;
            if (storms != null && !workerSpinUpAtTick)
            {
                workerNorth = storms.North;
                workerSouth = storms.South;
                workerPrevNorth = storms.PreviousNorth;
                workerPrevSouth = storms.PreviousSouth;
            }
            else
            {
                workerNorth = workerSouth = workerPrevNorth = workerPrevSouth = default;
            }

            long capturedTick = workerTick;
            bool spinUpAtTick = workerSpinUpAtTick;
            var token = lifetime.Token;
            worker = Task.Run(() => Calculate(capturedTick * .25, storms, spinUpAtTick, rebuildModel, token), token);
            return false;
        }

        void UploadCompletedWorkerToFreeSlot()
        {
            int slotIdx = pool.AcquireUploadSlot();
            var slot = pool.GetSlot(slotIdx);
            Buffer.BlockCopy(workerCloudRgba, 0, slot.CloudRgba, 0, workerCloudRgba.Length);
            Buffer.BlockCopy(workerPrecipHalf, 0, slot.PrecipHalf, 0, workerPrecipHalf.Length * sizeof(ushort));

            var cloudTex = cloudTextures[slotIdx];
            if (!cloudTex)
            {
                cloudTex = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
                {
                    name = "Regional climate response slot " + slotIdx,
                    filterMode = FilterMode.Bilinear,
                    wrapModeU = TextureWrapMode.Repeat,
                    wrapModeV = TextureWrapMode.Clamp
                };
                cloudTextures[slotIdx] = cloudTex;
            }
            cloudTex.SetPixelData(slot.CloudRgba, 0);
            cloudTex.Apply(false, false);

            var precipTex = precipTextures[slotIdx];
            if (!precipTex)
            {
                var fmt = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf) ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat;
                precipTex = new Texture2D(Width, Height, fmt, false, true)
                {
                    name = "Regional 6h precip diagnostic slot " + slotIdx,
                    filterMode = FilterMode.Bilinear,
                    wrapModeU = TextureWrapMode.Repeat,
                    wrapModeV = TextureWrapMode.Clamp
                };
                precipTextures[slotIdx] = precipTex;
            }
            if (precipTex.format == TextureFormat.RGBAHalf)
            {
                precipTex.SetPixelData(slot.PrecipHalf, 0);
            }
            else
            {
                if (precipFallback == null || precipFallback.Length != slot.PrecipHalf.Length)
                    precipFallback = new float[slot.PrecipHalf.Length];
                for (int k = 0; k < slot.PrecipHalf.Length; k++)
                    precipFallback[k] = FlatMoistureCloudBudget.HalfBitsToFloat(slot.PrecipHalf[k]);
                precipTex.SetPixelData(precipFallback, 0);
            }
            precipTex.Apply(false, false);

            FlatRegionalSnapshotPool.ComputeCloudPoles(
                slot.CloudRgba, out float cSouthX, out float cSouthY, out float cNorthX, out float cNorthY);
            pool.CommitUpload(
                world,
                slotIdx, workerTick, workerRevision, workerMoisture,
                workerHadStorms, workerSpinUpAtTick,
                in workerNorth, in workerSouth, in workerPrevNorth, in workerPrevSouth,
                cSouthX, cSouthY, cNorthX, cNorthY,
                workerSouthRain, workerSouthSfc, workerNorthRain, workerNorthSfc);
        }

        void Calculate(double day, FlatCycloneWeather storms, bool spinUpAtTick, bool rebuildModel, CancellationToken cancel)
        {
            if (spinUpAtTick)
                storms = FlatCycloneWeather.SpinUpAt(world, day);
            if (model == null || rebuildModel)
                model = new FlatClimateStatistics(world, cancel, Width, atmosphereOnly: true);
            model.Refresh(day, storms, cancel);
            moistureBudget.Solve(world, day, storms);
            moistureBudget.PackPrecipitationHalf(
                workerPrecipHalf, out workerSouthRain, out workerSouthSfc, out workerNorthRain, out workerNorthSfc);
            for (int i = 0; i < FlatRegionalSnapshotPool.PixelCount; i++)
            {
                var response = RegionalCloudResponse.FromSynoptic(model.Cells[i], i, moistureBudget, storms, day);
                int p4 = i * 4;
                workerCloudRgba[p4] = Byte(Math.Max(0, response.Cover));
                workerCloudRgba[p4 + 1] = Byte(Math.Max(0, -response.Cover));
                workerCloudRgba[p4 + 2] = Byte(response.Convection);
                workerCloudRgba[p4 + 3] = Byte(response.Evaporation);
            }
        }

        static byte Byte(float unit) => (byte)(unit * 255 + .5f);

        internal void StageAndBind(WorldData targetWorld, double day, Material bake, Material hpColumn, out long? stagedTick, out ulong stagedGeneration)
        {
            long targetTick = (long)Math.Floor(day * 4);
            int slotIdx = pool.BeginStageForStrip(targetWorld, targetTick);
            BindSlotToMaterials(slotIdx, bake, hpColumn, out stagedTick, out stagedGeneration);
        }

        internal void AbortStagedStrip(WorldData targetWorld, Material bake, Material hpColumn)
        {
            pool.AbortStagedStrip();
            int pubIdx = HasPublishedFor(targetWorld) ? pool.PublishedIndex : -1;
            BindSlotToMaterials(pubIdx, bake, hpColumn, out _, out _);
        }

        internal void ResetPublishedOnResize(Material bake, Material hpColumn)
        {
            pool.ResetPublished();
            BindSlotToMaterials(-1, bake, hpColumn, out _, out _);
        }

        internal void CommitStagedStrip()
        {
            pool.CommitStagedStrip();
        }

        void BindSlotToMaterials(int slotIdx, Material bake, Material hpColumn, out long? tick, out ulong generation)
        {
            bool valid = slotIdx >= 0 && cloudTextures[slotIdx] && precipTextures[slotIdx];
            var slot = valid ? pool.GetSlot(slotIdx) : null;
            valid &= slot != null && slot.Valid;
            tick = valid ? (long?)slot.Tick : null;
            generation = valid ? slot.Generation : 0UL;

            var cloudTex = valid ? cloudTextures[slotIdx] : Texture2D.blackTexture;
            var precipTex = valid ? precipTextures[slotIdx] : Texture2D.blackTexture;
            var cloudPoles = valid
                ? new Vector4(slot.CloudSouthX, slot.CloudSouthY, slot.CloudNorthX, slot.CloudNorthY)
                : Vector4.zero;
            var precipPoles = valid
                ? new Vector4(slot.PrecipSouthRain, slot.PrecipSouthSfc, slot.PrecipNorthRain, slot.PrecipNorthSfc)
                : Vector4.zero;
            float readyFlag = valid ? 1f : 0f;

            if (bake)
            {
                bake.SetTexture("_RegionalCloudResponse", cloudTex);
                bake.SetFloat("_RegionalEvapReady", readyFlag);
                bake.SetVector("_RegionalCloudPoles", cloudPoles);
                bake.SetTexture("_RegionalPrecipDiagTex", precipTex);
                bake.SetVector("_RegionalPrecipPoles", precipPoles);
                bake.SetFloat("_RegionalPrecipReady", readyFlag);
            }
            if (hpColumn)
            {
                hpColumn.SetTexture("_RegionalPrecipDiagTex", precipTex);
                hpColumn.SetVector("_RegionalPrecipPoles", precipPoles);
                hpColumn.SetFloat("_RegionalPrecipReady", readyFlag);
            }
        }

        internal bool TryGetPublishedPrecipitation(WorldData targetWorld, out Texture2D precipTex, out Vector4 precipPoles)
        {
            if (HasPublishedFor(targetWorld))
            {
                int p = pool.PublishedIndex;
                var slot = pool.GetSlot(p);
                precipTex = precipTextures[p];
                precipPoles = new Vector4(slot.PrecipSouthRain, slot.PrecipSouthSfc, slot.PrecipNorthRain, slot.PrecipNorthSfc);
                return true;
            }
            precipTex = null;
            precipPoles = Vector4.zero;
            return false;
        }

        internal bool TrySamplePublishedPrecipitation(WorldData targetWorld, double u, double v, out RegionalPrecipitationDiagnostic diagnostic) =>
            pool.TrySamplePublishedPrecipitation(targetWorld, u, v, out diagnostic);

        internal bool TrySamplePrecipitationForTick(WorldData targetWorld, long expectedTick, double u, double v, out RegionalPrecipitationDiagnostic diagnostic) =>
            pool.TrySamplePrecipitationForTick(targetWorld, expectedTick, u, v, out diagnostic);

        public void Dispose()
        {
            lifetime.Cancel();
            if (worker == null) lifetime.Dispose();
            else worker.ContinueWith(t => { if (t.IsFaulted) _ = t.Exception; lifetime.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            for (int i = 0; i < FlatRegionalSnapshotPool.SlotCount; i++)
            {
                if (cloudTextures[i]) UnityEngine.Object.Destroy(cloudTextures[i]);
                if (precipTextures[i]) UnityEngine.Object.Destroy(precipTextures[i]);
                cloudTextures[i] = null;
                precipTextures[i] = null;
            }
            pool.ResetAll();
        }
    }
}
