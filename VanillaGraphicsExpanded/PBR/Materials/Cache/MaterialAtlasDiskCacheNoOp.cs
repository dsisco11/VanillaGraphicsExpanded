namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

/// <summary>
/// No-op implementation used until persistence/eviction is implemented.
/// </summary>
internal sealed class MaterialAtlasDiskCacheNoOp : IMaterialAtlasDiskCache
{
    public static MaterialAtlasDiskCacheNoOp Instance { get; } = new();

    private MaterialAtlasDiskCacheNoOp() { }

    public void Clear()
    {
        // Intentionally no-op.
    }

    public MaterialAtlasDiskCacheStats GetStatsSnapshot()
        => new(
            MaterialParams: default,
            NormalDepth: default,
            TotalEntries: 0,
            TotalBytes: 0,
            EvictedEntries: 0,
            EvictedBytes: 0);

    public bool TryLoadMaterialParamsTile(AtlasCacheKey key, out float[] rgbTriplets)
    {
        rgbTriplets = null!;
        return false;
    }

    public void StoreMaterialParamsTile(AtlasCacheKey key, int width, int height, float[] rgbTriplets)
    {
        // Intentionally no-op.
    }

    public bool TryLoadNormalDepthTile(AtlasCacheKey key, out float[] rgbaQuads)
    {
        rgbaQuads = null!;
        return false;
    }

    public void StoreNormalDepthTile(AtlasCacheKey key, int width, int height, float[] rgbaQuads)
    {
        // Intentionally no-op.
    }
}
