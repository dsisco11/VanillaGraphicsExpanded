using System;
using System.Collections.Generic;
using System.Threading;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal sealed class MaterialAtlasBuildSession : IDisposable
{
    private readonly CancellationTokenSource cts;
    private readonly object overrideLock = new();
    private readonly HashSet<string> overrideFailureLogOnce = new();
    private readonly PriorityFifoQueue<MaterialAtlasParamsGpuOverrideUpload> pendingOverrideUploads = new();
    private readonly Dictionary<(int atlasTexId, AtlasRect rect), MaterialAtlasParamsGpuOverrideUpload> overridesByRect = new();

    public MaterialAtlasBuildSession(
        int generationId,
        IReadOnlyList<(int atlasTextureId, int width, int height)> atlasPages,
        IReadOnlyList<IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>> cpuTileJobs,
        IReadOnlyList<MaterialAtlasParamsGpuOverrideUpload> overrideJobs,
        IReadOnlyList<IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob>> normalDepthCpuJobs,
        MaterialOverrideTextureLoader overrideLoader,
        MaterialAtlasAsyncCacheCounters? cacheCounters)
    {
        if (generationId <= 0) throw new ArgumentOutOfRangeException(nameof(generationId));
        GenerationId = generationId;
        AtlasPages = atlasPages ?? throw new ArgumentNullException(nameof(atlasPages));
        CpuTileJobs = cpuTileJobs ?? throw new ArgumentNullException(nameof(cpuTileJobs));
        OverrideJobs = overrideJobs ?? throw new ArgumentNullException(nameof(overrideJobs));
        NormalDepthCpuJobs = normalDepthCpuJobs ?? throw new ArgumentNullException(nameof(normalDepthCpuJobs));
        OverrideLoader = overrideLoader ?? throw new ArgumentNullException(nameof(overrideLoader));
        CacheCounters = cacheCounters;

        cts = new CancellationTokenSource();
        CreatedUtc = DateTime.UtcNow;

        PagesByAtlasTexId = new Dictionary<int, MaterialAtlasBuildPageState>(capacity: atlasPages.Count);
        foreach ((int atlasTextureId, int width, int height) in atlasPages)
        {
            PagesByAtlasTexId[atlasTextureId] = new MaterialAtlasBuildPageState(atlasTextureId, width, height);
        }

        TotalTiles = cpuTileJobs.Count;
        CompletedTiles = 0;

        TotalOverrides = overrideJobs.Count;
        OverridesApplied = 0;

        TotalNormalDepthJobs = normalDepthCpuJobs.Count;
        CompletedNormalDepthJobs = 0;

        // Prime the per-rect override dictionary for O(1) lookup when a tile upload completes.
        foreach (var ov in overrideJobs)
        {
            overridesByRect[(ov.AtlasTextureId, ov.Rect)] = ov;
        }
    }

    public int GenerationId { get; }

    public DateTime CreatedUtc { get; }

    public IReadOnlyList<(int atlasTextureId, int width, int height)> AtlasPages { get; }

    public IReadOnlyList<IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>> CpuTileJobs { get; }

    public IReadOnlyList<MaterialAtlasParamsGpuOverrideUpload> OverrideJobs { get; }

    public IReadOnlyList<IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob>> NormalDepthCpuJobs { get; }

    public Dictionary<int, MaterialAtlasBuildPageState> PagesByAtlasTexId { get; }

    public MaterialOverrideTextureLoader OverrideLoader { get; }

    public MaterialAtlasAsyncCacheCounters? CacheCounters { get; }

    public int TotalTiles { get; }

    public int CompletedTiles { get; private set; }

    public int TotalOverrides { get; }

    public int OverridesApplied { get; private set; }

    public int TotalNormalDepthJobs { get; }

    public int CompletedNormalDepthJobs { get; private set; }

    public int PendingOverrideUploadsCount
    {
        get
        {
            lock (overrideLock)
            {
                return pendingOverrideUploads.Count;
            }
        }
    }

    public bool IsCancelled => cts.IsCancellationRequested;

    public bool IsComplete => CompletedTiles >= TotalTiles && CompletedNormalDepthJobs >= TotalNormalDepthJobs && !IsCancelled;

    public CancellationToken Token => cts.Token;

    public void IncrementCompletedTile()
        => CompletedTiles++;

    public void IncrementOverridesApplied()
        => OverridesApplied++;

    public void MarkNormalDepthJobCompleted(int atlasTextureId)
    {
        CompletedNormalDepthJobs++;

        if (PagesByAtlasTexId.TryGetValue(atlasTextureId, out MaterialAtlasBuildPageState? page))
        {
            page.NormalDepthPendingJobs = Math.Max(0, page.NormalDepthPendingJobs - 1);
            page.NormalDepthCompletedJobs++;
        }
    }

    public void EnsureNormalDepthPageCleared(ICoreClientAPI capi, int atlasTextureId, MaterialAtlasPageTextures pageTextures)
    {
        if (!PagesByAtlasTexId.TryGetValue(atlasTextureId, out MaterialAtlasBuildPageState? page))
        {
            return;
        }

        if (page.NormalDepthPageClearDone)
        {
            return;
        }

        if (pageTextures.NormalDepthTexture is null || !pageTextures.NormalDepthTexture.IsValid)
        {
            return;
        }

        MaterialAtlasNormalDepthGpuBuilder.ClearAtlasPage(
            capi,
            destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
            atlasWidth: page.Width,
            atlasHeight: page.Height);

        page.NormalDepthPageClearDone = true;
    }

    public void EnqueueOverrideUpload(MaterialAtlasParamsGpuOverrideUpload upload)
    {
        lock (overrideLock)
        {
            pendingOverrideUploads.Enqueue(upload.Priority, upload);
        }
    }

    public bool TryDequeuePendingOverrideUpload(out MaterialAtlasParamsGpuOverrideUpload upload)
    {
        lock (overrideLock)
        {
            return pendingOverrideUploads.TryDequeue(out upload);
        }
    }

    public bool TryDequeueOverrideForRect(int atlasTextureId, AtlasRect rect, out MaterialAtlasParamsGpuOverrideUpload upload)
    {
        lock (overrideLock)
        {
            if (!overridesByRect.Remove((atlasTextureId, rect), out upload))
            {
                upload = default;
                return false;
            }

            return true;
        }
    }

    public bool TryMarkOverrideSatisfiedByCache(int atlasTextureId, AtlasRect rect)
    {
        lock (overrideLock)
        {
            if (!overridesByRect.Remove((atlasTextureId, rect), out _))
            {
                return false;
            }
        }

        // Treat as applied (tile already contains post-override output).
        IncrementOverridesApplied();

        if (PagesByAtlasTexId.TryGetValue(atlasTextureId, out MaterialAtlasBuildPageState? page))
        {
            page.PendingOverrides = Math.Max(0, page.PendingOverrides - 1);
            page.CompletedOverrides++;
        }

        return true;
    }

    public void LogOverrideFailureOnce(
        ICoreClientAPI capi,
        string? ruleId,
        AssetLocation targetTexture,
        AssetLocation overrideAsset,
        string? reason)
    {
        string key = $"{ruleId ?? overrideAsset.ToString()}|{targetTexture}";
        lock (overrideLock)
        {
            if (!overrideFailureLogOnce.Add(key))
            {
                return;
            }
        }

        capi.Logger.Warning(
            "[VGE] PBR override ignored: rule='{0}' target='{1}' override='{2}' reason='{3}'. Falling back to generated maps.",
            ruleId ?? "(no id)",
            targetTexture,
            overrideAsset,
            reason ?? "unknown error");
    }

    public void Cancel()
    {
        if (cts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
            // Best-effort.
        }
    }

    public void Dispose()
    {
        cts.Dispose();
    }
}
