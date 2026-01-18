namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

/// <summary>
/// Disk cache seam for atlas build outputs.
/// Phase 7 intentionally provides interfaces and wiring points only (no persistence).
/// </summary>
internal interface IMaterialAtlasDiskCache
{
    void Clear();

    bool TryLoadMaterialParamsTile(AtlasCacheKey key, out float[] rgbTriplets);

    void StoreMaterialParamsTile(AtlasCacheKey key, int width, int height, float[] rgbTriplets);

    bool TryLoadNormalDepthTile(AtlasCacheKey key, out float[] rgbaQuads);

    void StoreNormalDepthTile(AtlasCacheKey key, int width, int height, float[] rgbaQuads);
}
