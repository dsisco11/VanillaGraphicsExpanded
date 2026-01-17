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

    private readonly ConcurrentQueue<PbrMaterialAtlasTileJob> pendingCpuJobs = new();
    private readonly ConcurrentQueue<PbrMaterialAtlasTileUpload> completedUploads = new();
    private readonly ConcurrentQueue<PbrMaterialAtlasTileUpload> pendingGpuUploads = new();
    private readonly ConcurrentQueue<PbrMaterialAtlasMaterialOverrideUpload> pendingOverrideUploads = new();

    private readonly Stopwatch stopwatch = new();

    private readonly HashSet<string> overrideFailureLogOnce = new();

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

            while (pendingCpuJobs.TryDequeue(out _)) { }
            while (completedUploads.TryDequeue(out _)) { }
            while (pendingGpuUploads.TryDequeue(out _)) { }
            while (pendingOverrideUploads.TryDequeue(out _)) { }

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
                        rgb,
                        job.Texture,
                        job.MaterialParamsOverride,
                        job.OverrideRuleId,
                        job.OverrideRuleSource,
                        job.OverrideScale));
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

                // Enqueue the optional override upload after the procedural upload.
                if (upload.MaterialParamsOverride is not null)
                {
                    pendingOverrideUploads.Enqueue(new PbrMaterialAtlasMaterialOverrideUpload(
                        GenerationId: upload.GenerationId,
                        AtlasTextureId: upload.AtlasTextureId,
                        RectX: upload.RectX,
                        RectY: upload.RectY,
                        RectWidth: upload.RectWidth,
                        RectHeight: upload.RectHeight,
                        TargetTexture: upload.TargetTexture,
                        OverrideAsset: upload.MaterialParamsOverride,
                        RuleId: upload.OverrideRuleId,
                        RuleSource: upload.OverrideRuleSource,
                        Scale: upload.OverrideScale));
                }

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

        // Apply a limited number of override uploads per frame (after base tiles).
        while (uploads < maxUploads && stopwatch.Elapsed.TotalMilliseconds < budgetMs)
        {
            if (!pendingOverrideUploads.TryDequeue(out PbrMaterialAtlasMaterialOverrideUpload ov))
            {
                break;
            }

            if (ov.GenerationId != active.GenerationId)
            {
                continue;
            }

            var pageTextures = tryGetPageTextures(ov.AtlasTextureId);
            if (pageTextures is null)
            {
                continue;
            }

            try
            {
                if (!VanillaGraphicsExpanded.PBR.Materials.PbrOverrideTextureLoader.TryLoadRgbaFloats01(
                        capi,
                        ov.OverrideAsset,
                        out int _,
                        out int _,
                        out float[] rgba01,
                        out string? reason,
                        expectedWidth: ov.RectWidth,
                        expectedHeight: ov.RectHeight))
                {
                    string key = $"{ov.RuleId ?? ov.OverrideAsset.ToString()}|{ov.TargetTexture}";
                    if (overrideFailureLogOnce.Add(key))
                    {
                        capi.Logger.Warning(
                            "[VGE] PBR override ignored: rule='{0}' target='{1}' override='{2}' reason='{3}'. Falling back to generated maps.",
                            ov.RuleId ?? "(no id)",
                            ov.TargetTexture,
                            ov.OverrideAsset,
                            reason ?? "unknown error");
                    }

                    continue;
                }

                float[] rgb = new float[checked(ov.RectWidth * ov.RectHeight * 3)];
                for (int y = 0; y < ov.RectHeight; y++)
                {
                    int srcRow = (y * ov.RectWidth) * 4;
                    int dstRow = (y * ov.RectWidth) * 3;

                    ReadOnlySpan<float> src = rgba01.AsSpan(srcRow, ov.RectWidth * 4);
                    Span<float> dst = rgb.AsSpan(dstRow, ov.RectWidth * 3);

                    // Channel packing must match vge_material.glsl (RGB = roughness, metallic, emissive). Ignore alpha.
                    SimdSpanMath.CopyInterleaved4ToInterleaved3(src, dst);
                }

                if (ov.Scale.Roughness != 1f || ov.Scale.Metallic != 1f || ov.Scale.Emissive != 1f)
                {
                    SimdSpanMath.MultiplyClamp01Interleaved3InPlace2D(
                        destination3: rgb,
                        rectWidthPixels: ov.RectWidth,
                        rectHeightPixels: ov.RectHeight,
                        rowStridePixels: ov.RectWidth,
                        mul0: ov.Scale.Roughness,
                        mul1: ov.Scale.Metallic,
                        mul2: ov.Scale.Emissive);
                }

                pageTextures.MaterialParamsTexture.UploadData(
                    rgb,
                    ov.RectX,
                    ov.RectY,
                    ov.RectWidth,
                    ov.RectHeight);

                active.IncrementOverridesApplied();
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
        while (pendingOverrideUploads.TryDequeue(out _)) { }
    }
}
