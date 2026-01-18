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

internal sealed class PbrMaterialAtlasBuildScheduler : IDisposable
{
    private readonly object sessionLock = new();

    private readonly PriorityFifoQueue<MaterialAtlasParamsCpuTileJob> pendingCpuJobs = new();
    private readonly ConcurrentQueue<MaterialAtlasParamsGpuTileUpload> completedUploads = new();
    private readonly PriorityFifoQueue<MaterialAtlasParamsGpuTileUpload> pendingGpuUploads = new();

    private readonly Stopwatch stopwatch = new();

    private PbrMaterialAtlasBuildSession? session;
    private Func<int, VanillaGraphicsExpanded.PBR.Materials.PbrMaterialAtlasPageTextures?>? tryGetPageTextures;
    private ICoreClientAPI? capi;

    public void Initialize(
        ICoreClientAPI capi,
        Func<int, VanillaGraphicsExpanded.PBR.Materials.PbrMaterialAtlasPageTextures?> tryGetPageTextures)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.tryGetPageTextures = tryGetPageTextures ?? throw new ArgumentNullException(nameof(tryGetPageTextures));
    }

    public PbrMaterialAtlasBuildSession? ActiveSession
    {
        get
        {
            lock (sessionLock)
            {
                return session;
            }
        }
    }

    public void StartSession(PbrMaterialAtlasBuildSession newSession)
    {
        ArgumentNullException.ThrowIfNull(newSession);

        lock (sessionLock)
        {
            CancelSession_NoLock();
            session = newSession;

            pendingCpuJobs.Clear();
            while (completedUploads.TryDequeue(out _)) { }
            pendingGpuUploads.Clear();

            foreach (MaterialAtlasParamsCpuTileJob job in newSession.CpuTileJobs)
            {
                pendingCpuJobs.Enqueue(job.Priority, job);

                if (newSession.PagesByAtlasTexId.TryGetValue(job.AtlasTextureId, out PbrMaterialAtlasPageBuildState? page))
                {
                    page.PendingTiles++;
                    page.PageClearDone = true; // material params texture is pre-filled with defaults.
                }
            }

            // Enqueue override-only uploads (rects that have no corresponding tile job).
            foreach (MaterialAtlasParamsGpuOverrideUpload ov in newSession.OverrideJobs)
            {
                if (newSession.PagesByAtlasTexId.TryGetValue(ov.AtlasTextureId, out PbrMaterialAtlasPageBuildState? page))
                {
                    page.PendingOverrides++;
                }

                newSession.EnqueueOverrideUpload(ov);
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

        PbrMaterialAtlasBuildSession? active;
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
            if (!pendingCpuJobs.TryDequeue(out MaterialAtlasParamsCpuTileJob job))
            {
                break;
            }

            if (job.GenerationId != active.GenerationId)
            {
                continue;
            }

            if (active.PagesByAtlasTexId.TryGetValue(job.AtlasTextureId, out PbrMaterialAtlasPageBuildState? page))
            {
                page.PendingTiles = Math.Max(0, page.PendingTiles - 1);
                page.InFlightTiles++;
            }

            dispatched++;

            _ = Task.Run(() =>
            {
                try
                {
                    active.Token.ThrowIfCancellationRequested();

                    MaterialAtlasParamsGpuTileUpload upload = job.Execute(active.Token);
                    completedUploads.Enqueue(upload);
                }
                catch (OperationCanceledException)
                {
                    // Ignore.
                }
                catch
                {
                    // Best-effort: skip this tile.
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
            }
            catch
            {
                // Ignore GL errors during shutdown.
            }
        }
    }

    public void Dispose()
    {
        CancelActiveSession();

        pendingCpuJobs.Clear();
        while (completedUploads.TryDequeue(out _)) { }
        pendingGpuUploads.Clear();
    }
}
