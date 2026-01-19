using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Numerics;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal sealed class MaterialAtlasBuildScheduler : IDisposable
{
    private readonly object sessionLock = new();

    private readonly PriorityFifoQueue<IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>> pendingCpuJobs = new();
    private readonly ConcurrentQueue<MaterialAtlasParamsGpuTileUpload> completedUploads = new();
    private readonly PriorityFifoQueue<MaterialAtlasParamsGpuTileUpload> pendingGpuUploads = new();

    private readonly PriorityFifoQueue<IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob>> pendingNormalDepthCpuJobs = new();
    private readonly ConcurrentQueue<MaterialAtlasNormalDepthGpuJob> completedNormalDepthUploads = new();
    private readonly PriorityFifoQueue<MaterialAtlasNormalDepthGpuJob> pendingNormalDepthGpuJobs = new();

    private readonly Stopwatch stopwatch = new();

    private double lastTickMs;
    private int lastDispatchedCpuJobs;
    private int lastGpuUploads;
    private int lastOverrideUploads;
    private int lastNormalDepthUploads;

    private int lastLoggedCompleteGenerationId;

    private MaterialAtlasBuildSession? session;
    private Func<int, VanillaGraphicsExpanded.PBR.Materials.MaterialAtlasPageTextures?>? tryGetPageTextures;
    private ICoreClientAPI? capi;

    public void Initialize(
        ICoreClientAPI capi,
        Func<int, VanillaGraphicsExpanded.PBR.Materials.MaterialAtlasPageTextures?> tryGetPageTextures)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.tryGetPageTextures = tryGetPageTextures ?? throw new ArgumentNullException(nameof(tryGetPageTextures));
    }

    public MaterialAtlasBuildSession? ActiveSession
    {
        get
        {
            lock (sessionLock)
            {
                return session;
            }
        }
    }

    public void StartSession(MaterialAtlasBuildSession newSession)
    {
        ArgumentNullException.ThrowIfNull(newSession);

        lock (sessionLock)
        {
            CancelSession_NoLock();
            session = newSession;

            pendingCpuJobs.Clear();
            while (completedUploads.TryDequeue(out _)) { }
            pendingGpuUploads.Clear();
            pendingNormalDepthCpuJobs.Clear();
            while (completedNormalDepthUploads.TryDequeue(out _)) { }
            pendingNormalDepthGpuJobs.Clear();

            lastLoggedCompleteGenerationId = 0;

            var tileRects = new HashSet<(int atlasTexId, AtlasRect rect)>();

            foreach (IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload> job in newSession.CpuTileJobs)
            {
                pendingCpuJobs.Enqueue(job.Priority, job);

                // IMaterialAtlasCpuJob does not expose the rect, so extract from known implementations.
                if (job is MaterialAtlasParamsCpuTileJob p)
                {
                    tileRects.Add((p.AtlasTextureId, p.Rect));
                }
                else if (job is MaterialAtlasParamsCpuCachedTileJob c)
                {
                    tileRects.Add((c.AtlasTextureId, c.Rect));
                }

                if (newSession.PagesByAtlasTexId.TryGetValue(job.AtlasTextureId, out MaterialAtlasBuildPageState? page))
                {
                    page.PendingTiles++;
                    page.PageClearDone = true; // material params texture is pre-filled with defaults.
                }
            }

            // Enqueue override-only uploads (rects that have no corresponding tile job).
            foreach (MaterialAtlasParamsGpuOverrideUpload ov in newSession.OverrideJobs)
            {
                if (newSession.PagesByAtlasTexId.TryGetValue(ov.AtlasTextureId, out MaterialAtlasBuildPageState? page))
                {
                    page.PendingOverrides++;
                }

                if (!tileRects.Contains((ov.AtlasTextureId, ov.Rect)))
                {
                    newSession.EnqueueOverrideUpload(ov);
                }
            }

            foreach (IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob> job in newSession.NormalDepthCpuJobs)
            {
                pendingNormalDepthCpuJobs.Enqueue(job.Priority, job);

                if (newSession.PagesByAtlasTexId.TryGetValue(job.AtlasTextureId, out MaterialAtlasBuildPageState? page))
                {
                    page.NormalDepthPendingJobs++;
                    page.NormalDepthPageClearDone = false;
                }
            }
        }
    }

    public void CancelActiveSession()
    {
        lock (sessionLock)
        {
            CancelSession_NoLock();
        }
    }

    private void CancelSession_NoLock()
    {
        if (session is null)
        {
            return;
        }

        try
        {
            session.Cancel();
        }
        finally
        {
            session.Dispose();
            session = null;
        }
    }

    public void TickOnRenderThread()
    {
        if (tryGetPageTextures is null || capi is null)
        {
            return;
        }

        MaterialAtlasBuildSession? active;
        lock (sessionLock)
        {
            active = session;
        }

        if (active is null || active.IsCancelled)
        {
            return;
        }

        // Pump completed CPU results into the GPU upload queue.
        while (completedUploads.TryDequeue(out MaterialAtlasParamsGpuTileUpload completed))
        {
            if (completed.GenerationId != active.GenerationId)
            {
                // Stale.
                continue;
            }

            pendingGpuUploads.Enqueue(completed.Priority, completed);
        }

        while (completedNormalDepthUploads.TryDequeue(out MaterialAtlasNormalDepthGpuJob completedNd))
        {
            if (completedNd.GenerationId != active.GenerationId)
            {
                continue;
            }

            pendingNormalDepthGpuJobs.Enqueue(completedNd.Priority, completedNd);
        }

        float budgetMs = ConfigModSystem.Config.MaterialAtlasAsyncBudgetMs;
        if (budgetMs <= 0)
        {
            budgetMs = 0.5f;
        }

        int maxUploads = ConfigModSystem.Config.MaterialAtlasAsyncMaxUploadsPerFrame;
        if (maxUploads <= 0)
        {
            maxUploads = 1;
        }

        int maxCpuJobsPerFrame = ConfigModSystem.Config.MaterialAtlasAsyncMaxCpuJobsPerFrame;
        if (maxCpuJobsPerFrame <= 0)
        {
            maxCpuJobsPerFrame = 1;
        }

        stopwatch.Restart();

        // Dispatch a limited number of CPU jobs per frame.
        int dispatched = 0;
        while (dispatched < maxCpuJobsPerFrame && stopwatch.Elapsed.TotalMilliseconds < budgetMs)
        {
            bool isMaterialParams = pendingCpuJobs.TryDequeue(out IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload> job);
            bool isNormalDepth = false;
            IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob> ndJob = default!;

            if (!isMaterialParams)
            {
                isNormalDepth = pendingNormalDepthCpuJobs.TryDequeue(out ndJob);
                if (!isNormalDepth)
                {
                    break;
                }
            }

            if (isMaterialParams)
            {
                if (job.GenerationId != active.GenerationId)
                {
                    continue;
                }

                if (active.PagesByAtlasTexId.TryGetValue(job.AtlasTextureId, out MaterialAtlasBuildPageState? page))
                {
                    page.PendingTiles = Math.Max(0, page.PendingTiles - 1);
                    page.InFlightTiles++;
                }
            }
            else
            {
                if (ndJob.GenerationId != active.GenerationId)
                {
                    continue;
                }
            }

            dispatched++;

            bool isMp = isMaterialParams;
            IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload> mpJob = job;
            IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob> ndJobLocal = ndJob;
            CancellationToken token = active.Token;

            _ = Task.Run(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    if (isMp)
                    {
                        MaterialAtlasParamsGpuTileUpload upload = mpJob.Execute(token);
                        completedUploads.Enqueue(upload);
                    }
                    else
                    {
                        MaterialAtlasNormalDepthGpuJob gpuJob = ndJobLocal.Execute(token);
                        completedNormalDepthUploads.Enqueue(gpuJob);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore.
                }
                catch
                {
                    // Best-effort: skip.
                }
            }, CancellationToken.None);
        }

        // Do a limited number of GPU uploads per frame.
        int uploads = 0;
        while (uploads < maxUploads && stopwatch.Elapsed.TotalMilliseconds < budgetMs)
        {
            if (!pendingGpuUploads.TryDequeue(out MaterialAtlasParamsGpuTileUpload upload))
            {
                break;
            }

            if (upload.GenerationId != active.GenerationId)
            {
                continue;
            }

            var pageTextures = tryGetPageTextures(upload.AtlasTextureId);
            if (pageTextures is null)
            {
                continue;
            }

            try
            {
                upload.Execute(capi, tryGetPageTextures, active);
                uploads++;
            }
            catch
            {
                // Ignore GL errors during shutdown.
            }
        }

        // Apply a limited number of override uploads per frame.
        int overrideUploads = 0;
        while (uploads < maxUploads && stopwatch.Elapsed.TotalMilliseconds < budgetMs)
        {
            if (!active.TryDequeuePendingOverrideUpload(out MaterialAtlasParamsGpuOverrideUpload ov))
            {
                break;
            }

            if (ov.GenerationId != active.GenerationId)
            {
                continue;
            }

            try
            {
                ov.Execute(capi, tryGetPageTextures, active);
                uploads++;
                overrideUploads++;
            }
            catch
            {
                // Ignore GL errors during shutdown.
            }
        }

        // Do a limited number of normal+depth GPU jobs per frame.
        int normalDepthUploads = 0;
        while (uploads < maxUploads && stopwatch.Elapsed.TotalMilliseconds < budgetMs)
        {
            if (!pendingNormalDepthGpuJobs.TryDequeue(out MaterialAtlasNormalDepthGpuJob job))
            {
                break;
            }

            if (job.GenerationId != active.GenerationId)
            {
                continue;
            }

            try
            {
                job.Execute(capi, tryGetPageTextures, active);
                uploads++;
                normalDepthUploads++;
            }
            catch
            {
                // Ignore GL errors during shutdown.
            }
        }

        lastTickMs = stopwatch.Elapsed.TotalMilliseconds;
        lastDispatchedCpuJobs = dispatched;
        lastOverrideUploads = overrideUploads;
        lastNormalDepthUploads = normalDepthUploads;

        // totalUploads == materialUploads + overrideUploads + normalDepthUploads
        lastGpuUploads = Math.Max(0, uploads - overrideUploads - normalDepthUploads);

        if (active.IsComplete
            && lastLoggedCompleteGenerationId != active.GenerationId
            && ConfigModSystem.Config.EnableMaterialAtlasDiskCache
            && ConfigModSystem.Config.DebugLogMaterialAtlasDiskCache
            && capi is not null
            && active.CacheCounters is not null)
        {
            lastLoggedCompleteGenerationId = active.GenerationId;
            MaterialAtlasAsyncCacheCounters.Snapshot s = active.CacheCounters.GetSnapshot();
            capi.Logger.Debug(
                "[VGE] Material atlas disk cache (material params): base hits={0} misses={1}; override hits={2} misses={3}",
                s.BaseHits,
                s.BaseMisses,
                s.OverrideHits,
                s.OverrideMisses);
        }
    }

    public MaterialAtlasAsyncBuildDiagnostics GetDiagnosticsSnapshot()
    {
        MaterialAtlasBuildSession? active;
        lock (sessionLock)
        {
            active = session;
        }

        if (active is null)
        {
            return new MaterialAtlasAsyncBuildDiagnostics(
                GenerationId: 0,
                IsCancelled: false,
                IsComplete: true,
                TotalTiles: 0,
                CompletedTiles: 0,
                TotalOverrides: 0,
                OverridesApplied: 0,
                TotalNormalDepthJobs: 0,
                CompletedNormalDepthJobs: 0,
                PendingCpuJobs: pendingCpuJobs.Count,
                CompletedCpuResults: completedUploads.Count,
                PendingGpuUploads: pendingGpuUploads.Count,
                PendingOverrideUploads: 0,
                PendingNormalDepthJobs: pendingNormalDepthGpuJobs.Count,
                LastTickMs: lastTickMs,
                LastDispatchedCpuJobs: lastDispatchedCpuJobs,
                LastGpuUploads: lastGpuUploads,
                LastOverrideUploads: lastOverrideUploads,
                LastNormalDepthUploads: lastNormalDepthUploads,
                Pages: Array.Empty<MaterialAtlasAsyncBuildDiagnostics.Page>());
        }

        var pages = new List<MaterialAtlasAsyncBuildDiagnostics.Page>(capacity: active.PagesByAtlasTexId.Count);
        foreach (var kvp in active.PagesByAtlasTexId)
        {
            MaterialAtlasBuildPageState p = kvp.Value;
            pages.Add(new MaterialAtlasAsyncBuildDiagnostics.Page(
                AtlasTextureId: p.AtlasTextureId,
                Width: p.Width,
                Height: p.Height,
                PendingTiles: p.PendingTiles,
                InFlightTiles: p.InFlightTiles,
                CompletedTiles: p.CompletedTiles,
                PendingOverrides: p.PendingOverrides,
                CompletedOverrides: p.CompletedOverrides,
                PageClearDone: p.PageClearDone,
                NormalDepthPendingJobs: p.NormalDepthPendingJobs,
                NormalDepthCompletedJobs: p.NormalDepthCompletedJobs,
                NormalDepthPageClearDone: p.NormalDepthPageClearDone));
        }

        return new MaterialAtlasAsyncBuildDiagnostics(
            GenerationId: active.GenerationId,
            IsCancelled: active.IsCancelled,
            IsComplete: active.IsComplete,
            TotalTiles: active.TotalTiles,
            CompletedTiles: active.CompletedTiles,
            TotalOverrides: active.TotalOverrides,
            OverridesApplied: active.OverridesApplied,
            TotalNormalDepthJobs: active.TotalNormalDepthJobs,
            CompletedNormalDepthJobs: active.CompletedNormalDepthJobs,
            PendingCpuJobs: pendingCpuJobs.Count,
            CompletedCpuResults: completedUploads.Count,
            PendingGpuUploads: pendingGpuUploads.Count,
            PendingOverrideUploads: active.PendingOverrideUploadsCount,
            PendingNormalDepthJobs: pendingNormalDepthGpuJobs.Count,
            LastTickMs: lastTickMs,
            LastDispatchedCpuJobs: lastDispatchedCpuJobs,
            LastGpuUploads: lastGpuUploads,
            LastOverrideUploads: lastOverrideUploads,
            LastNormalDepthUploads: lastNormalDepthUploads,
            Pages: pages);
    }

    public void Dispose()
    {
        CancelActiveSession();

        pendingCpuJobs.Clear();
        while (completedUploads.TryDequeue(out _)) { }
        pendingGpuUploads.Clear();
    }
}
