using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.ModSystems;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal sealed class PbrMaterialAtlasBuildScheduler : IDisposable
{
    private readonly object sessionLock = new();

    private readonly ConcurrentQueue<PbrMaterialAtlasTileJob> pendingCpuJobs = new();
    private readonly ConcurrentQueue<PbrMaterialAtlasTileUpload> completedUploads = new();
    private readonly ConcurrentQueue<PbrMaterialAtlasTileUpload> pendingGpuUploads = new();

    private readonly Stopwatch stopwatch = new();

    private PbrMaterialAtlasBuildSession? session;
    private Func<int, VanillaGraphicsExpanded.PBR.Materials.PbrMaterialAtlasPageTextures?>? tryGetPageTextures;

    public void Initialize(Func<int, VanillaGraphicsExpanded.PBR.Materials.PbrMaterialAtlasPageTextures?> tryGetPageTextures)
    {
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

            while (pendingCpuJobs.TryDequeue(out _)) { }
            while (completedUploads.TryDequeue(out _)) { }
            while (pendingGpuUploads.TryDequeue(out _)) { }

            foreach (PbrMaterialAtlasTileJob job in newSession.TileJobs)
            {
                pendingCpuJobs.Enqueue(job);

                if (newSession.PagesByAtlasTexId.TryGetValue(job.AtlasTextureId, out PbrMaterialAtlasPageBuildState? page))
                {
                    page.PendingTiles++;
                    page.PageClearDone = true; // material params texture is pre-filled with defaults.
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
        if (tryGetPageTextures is null)
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
        while (completedUploads.TryDequeue(out PbrMaterialAtlasTileUpload completed))
        {
            if (completed.GenerationId != active.GenerationId)
            {
                // Stale.
                continue;
            }

            pendingGpuUploads.Enqueue(completed);
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
            if (!pendingCpuJobs.TryDequeue(out PbrMaterialAtlasTileJob job))
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

                    float[] rgb = VanillaGraphicsExpanded.PBR.Materials.PbrMaterialParamsPixelBuilder.BuildRgb16fTile(
                        job.Texture,
                        job.Definition,
                        job.RectWidth,
                        job.RectHeight,
                        active.Token);

                    completedUploads.Enqueue(new PbrMaterialAtlasTileUpload(
                        job.GenerationId,
                        job.AtlasTextureId,
                        job.RectX,
                        job.RectY,
                        job.RectWidth,
                        job.RectHeight,
                        rgb));
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
            if (!pendingGpuUploads.TryDequeue(out PbrMaterialAtlasTileUpload upload))
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
                pageTextures.MaterialParamsTexture.UploadData(
                    upload.RgbTriplets,
                    upload.RectX,
                    upload.RectY,
                    upload.RectWidth,
                    upload.RectHeight);

                if (active.PagesByAtlasTexId.TryGetValue(upload.AtlasTextureId, out PbrMaterialAtlasPageBuildState? page))
                {
                    page.InFlightTiles = Math.Max(0, page.InFlightTiles - 1);
                    page.CompletedTiles++;
                }

                active.IncrementCompletedTile();
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

        while (pendingCpuJobs.TryDequeue(out _)) { }
        while (completedUploads.TryDequeue(out _)) { }
        while (pendingGpuUploads.TryDequeue(out _)) { }
    }
}
