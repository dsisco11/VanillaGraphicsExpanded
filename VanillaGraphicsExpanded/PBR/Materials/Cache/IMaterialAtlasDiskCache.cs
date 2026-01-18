namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

/// <summary>
/// Disk cache seam for atlas build outputs.
/// Phase 7 intentionally provides interfaces and wiring points only (no persistence).
/// </summary>
internal interface IMaterialAtlasDiskCache
{
    bool TryLoadMaterialParamsTile(AtlasCacheKey key, out float[] rgbTriplets);

    void StoreMaterialParamsTile(AtlasCacheKey key, float[] rgbTriplets);

    bool TryLoadNormalDepthTile(AtlasCacheKey key, out float[] rgbaQuads);

    void StoreNormalDepthTile(AtlasCacheKey key, float[] rgbaQuads);
}
