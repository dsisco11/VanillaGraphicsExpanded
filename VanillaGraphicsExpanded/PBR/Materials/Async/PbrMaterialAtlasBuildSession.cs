using System;
using System.Collections.Generic;
using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal sealed class PbrMaterialAtlasBuildSession : IDisposable
{
    private readonly CancellationTokenSource cts;

    public PbrMaterialAtlasBuildSession(
        int generationId,
        IReadOnlyList<(int atlasTextureId, int width, int height)> atlasPages,
        IReadOnlyList<PbrMaterialAtlasTileJob> tileJobs)
    {
        if (generationId <= 0) throw new ArgumentOutOfRangeException(nameof(generationId));
        GenerationId = generationId;
        AtlasPages = atlasPages ?? throw new ArgumentNullException(nameof(atlasPages));
        TileJobs = tileJobs ?? throw new ArgumentNullException(nameof(tileJobs));

        cts = new CancellationTokenSource();
        CreatedUtc = DateTime.UtcNow;

        PagesByAtlasTexId = new Dictionary<int, PbrMaterialAtlasPageBuildState>(capacity: atlasPages.Count);
        foreach ((int atlasTextureId, int width, int height) in atlasPages)
        {
            PagesByAtlasTexId[atlasTextureId] = new PbrMaterialAtlasPageBuildState(atlasTextureId, width, height);
        }

        TotalTiles = tileJobs.Count;
        CompletedTiles = 0;
    }

    public int GenerationId { get; }

    public DateTime CreatedUtc { get; }

    public IReadOnlyList<(int atlasTextureId, int width, int height)> AtlasPages { get; }

    public IReadOnlyList<PbrMaterialAtlasTileJob> TileJobs { get; }

    public Dictionary<int, PbrMaterialAtlasPageBuildState> PagesByAtlasTexId { get; }

    public int TotalTiles { get; }

    public int CompletedTiles { get; private set; }

    public int OverridesApplied { get; private set; }

    public bool IsCancelled => cts.IsCancellationRequested;

    public bool IsComplete => CompletedTiles >= TotalTiles && !IsCancelled;

    public CancellationToken Token => cts.Token;

    public void IncrementCompletedTile()
        => CompletedTiles++;

    public void IncrementOverridesApplied()
        => OverridesApplied++;

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
