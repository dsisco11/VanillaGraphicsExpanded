using System;
using System.Collections.Generic;
using System.Threading;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

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
        MaterialOverrideTextureLoader overrideLoader)
    {
        if (generationId <= 0) throw new ArgumentOutOfRangeException(nameof(generationId));
        GenerationId = generationId;
        AtlasPages = atlasPages ?? throw new ArgumentNullException(nameof(atlasPages));
        CpuTileJobs = cpuTileJobs ?? throw new ArgumentNullException(nameof(cpuTileJobs));
        OverrideJobs = overrideJobs ?? throw new ArgumentNullException(nameof(overrideJobs));
        OverrideLoader = overrideLoader ?? throw new ArgumentNullException(nameof(overrideLoader));

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

    public Dictionary<int, MaterialAtlasBuildPageState> PagesByAtlasTexId { get; }

    public MaterialOverrideTextureLoader OverrideLoader { get; }

    public int TotalTiles { get; }

    public int CompletedTiles { get; private set; }

    public int TotalOverrides { get; }

    public int OverridesApplied { get; private set; }

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

    public bool IsComplete => CompletedTiles >= TotalTiles && !IsCancelled;

    public CancellationToken Token => cts.Token;

    public void IncrementCompletedTile()
        => CompletedTiles++;

    public void IncrementOverridesApplied()
        => OverridesApplied++;

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
