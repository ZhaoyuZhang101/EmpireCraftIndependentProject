# Render preparation workers

The active rectangular map keeps `WorldData` authoritative on the main thread. The worker boundary is a copied native snapshot. Jobs must never inspect or mutate `WorldData`, `DirtyChunks`, Unity textures, materials, or GameObjects.

## Acceptance checks

- Compare every tile of all thirteen map layers with the existing `FlatPalette` serial implementation, including the longitude seam, poles, coastal dilation and partial 32-tile chunks. Semantic bytes and float records remain exact; display gradients allow one byte quantization unit for Mono/Burst rounding.
- Compare all four float data channels with the authoritative facts (depth/flow/width, shore/sediment/freshness/connections, elevation/material/wetness/HAND, and elevation/shore/depth/canopy).
- Change source identity, terrain revision, layer and month while pending; reject and requeue obsolete results. Ordinary simulation revisions must still allow coherent publication while preserving later dirties, preventing starvation.
- Dirty chunks created after scheduling must remain dirty after completion. Never clear the current dirty set when publishing an older result.
- Dispose while pending and dispose twice without native lifetime errors. Restore unpublished dirty work to its source world.
- Measure snapshot CPU time separately from the observed schedule-to-completion delay and main-thread upload cost. A timing for a synthetic preparation test is not a frame-rate measurement.

## Boundaries

Initial texture warmup may block once. Recurring updates only call `Complete` after `IsCompleted`. Destruction must join pending jobs before disposing their native storage. Generation workers use a shared processor-aware cap, leaving room for the editor/render thread; drainage accumulation and other dependent passes remain ordered.

## Implemented ownership

`FlatMapRenderer.Flush` now schedules/polls `FlatMapRenderJobs`. One batch can be in flight, with persistent `NativeArray` storage sized to the fixed world. Full invalidation copies each tile once; a partial update captures only each dirty 32×32 chunk and its two-tile coastal halo. `IJobParallelFor` performs coastal albedo dilation, palette interpolation, semantic encoding and the eight texture buffer records. Unity's worker pool owns job execution; the application does not alter its global worker count.

Scheduling records source identity, terrain revision, month, requested layer and a monotonically increasing batch version. It clears the captured dirty set after successful scheduling. Completion checks identity/revisions/layer/month, rejects stale results and restores their captured chunk IDs, while preserving newly dirtied chunks. A completed accepted batch is uploaded only on the main thread. Destruction joins the pending job and restores unpublished work before disposing native buffers.

`FlatWorkerPolicy.Resolve` and `Options` bound independent managed generation passes to at most eight workers and reserve two logical processors where possible. Explicit worker requests are bounded too. Climate and independent terrain-analysis rows use this shared policy. The authoritative simulation remains on the main thread.

Diagnostics exposed on `FlatMapRenderer`: `PreparationPending`, `RejectedPreparationBatches`, `LastPreparedTiles`, `LastSnapshotMilliseconds`, `LastPreparationLatencyMilliseconds`, `LastUploadMilliseconds`. Latency includes time until the next poll; it is not pure worker CPU time. Texture upload and dirty snapshot copying still cost main-thread time. The first visible frame performs one explicit warmup join; recurring updates can appear one or more frames later. Rapid invalidation discards and reschedules a batch, rather than publishing obsolete results.

Run `Orbis.Editor.FlatWorkerRegression.Run()` in the connected Editor. It writes `Validation/Perf3_5/render-workers.json` with equivalence/lifetime assertions and separate serial, full snapshot, observed job, and dirty snapshot timings. This does not establish a frame-rate improvement.
