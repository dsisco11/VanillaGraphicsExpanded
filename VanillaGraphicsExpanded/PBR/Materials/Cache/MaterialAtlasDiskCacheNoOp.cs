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

    public bool TryLoadMaterialParamsTile(AtlasCacheKey key, out float[] rgbTriplets)
    {
        rgbTriplets = null!;
        return false;
    }

    public void StoreMaterialParamsTile(AtlasCacheKey key, float[] rgbTriplets)
    {
        // Intentionally no-op.
    }

    public bool TryLoadNormalDepthTile(AtlasCacheKey key, out float[] rgbaQuads)
    {
        rgbaQuads = null!;
        return false;
    }

    public void StoreNormalDepthTile(AtlasCacheKey key, float[] rgbaQuads)
    {
        // Intentionally no-op.
    }
}
