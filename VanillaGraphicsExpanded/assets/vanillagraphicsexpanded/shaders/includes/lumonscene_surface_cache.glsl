#ifndef VGE_LUMONSCENE_SURFACE_CACHE_GLSL
#define VGE_LUMONSCENE_SURFACE_CACHE_GLSL
// ============================================================================
// LumonScene Surface Cache Sampling (Phase 22.10) - v1
//
// Sampling contract:
// - PatchIdGBuffer stores: (chunkSlot, patchId, packedPatchUv, misc)
// - v1 virtual page mapping is deterministic: virtualPageIndex = patchId % (128*128)
// - PageTableMip0 stores packed entry: [0..23]=physicalPageId (1-based), [24..31]=flags
// - Physical page decode matches LumonScenePhysicalPagePool.DecodePhysicalId().
//
// Notes:
// - This is a voxel-only placeholder mapping until real PatchId->VirtualHandle/rect is wired.
// - Shading uses IrradianceAtlas (RGB irradiance, A weight) as diffuse GI.
// ============================================================================

const uint VGE_LUMONSCENE_VIRTUAL_W = 128u;
const uint VGE_LUMONSCENE_VIRTUAL_H = 128u;
const uint VGE_LUMONSCENE_VIRTUAL_COUNT = VGE_LUMONSCENE_VIRTUAL_W * VGE_LUMONSCENE_VIRTUAL_H;

const uint VGE_LUMONSCENE_PAGE_PHYS_ID_MASK = 0xFFFFFFu;
const uint VGE_LUMONSCENE_PAGE_FLAG_SHIFT = 24u;

// Flags mirror LumonScenePageTableEntryPacking.Flags (byte).
const uint VGE_LUMONSCENE_FLAG_RESIDENT     = 1u << 0u;
const uint VGE_LUMONSCENE_FLAG_NEEDS_CAPTURE= 1u << 1u;
const uint VGE_LUMONSCENE_FLAG_NEEDS_RELIGHT= 1u << 2u;

bool VgeLumonSceneTryDecodePatchId(
    uvec4 patchIdG,
    out uint outChunkSlot,
    out uint outPatchId,
    out vec2 outPatchUv01)
{
    outChunkSlot = patchIdG.x;
    outPatchId = patchIdG.y;
    if (outPatchId == 0u)
    {
        outPatchUv01 = vec2(0.0);
        return false;
    }

    uint packedUv = patchIdG.z;
    uint u16 = packedUv & 0xFFFFu;
    uint v16 = (packedUv >> 16u) & 0xFFFFu;
    outPatchUv01 = vec2(float(u16), float(v16)) / 65535.0;
    outPatchUv01 = clamp(outPatchUv01, vec2(0.0), vec2(1.0));
    return true;
}

bool VgeLumonSceneTrySampleIrradiance_NearFieldV1(
    uint chunkSlot,
    uint patchId,
    vec2 patchUv01,
    usampler2DArray pageTableMip0,
    sampler2DArray irradianceAtlas,
    int tileSizeTexels,
    int tilesPerAxis,
    int tilesPerAtlas,
    out vec3 outIrradiance,
    out uint outFlags,
    out uint outPhysicalPageId)
{
    outIrradiance = vec3(0.0);
    outFlags = 0u;
    outPhysicalPageId = 0u;

    if (tileSizeTexels <= 0 || tilesPerAxis <= 0 || tilesPerAtlas <= 0)
    {
        return false;
    }

    // v1: virtual page is derived from patchId (placeholder until PatchMetadata is wired).
    uint virtualPageIndex = patchId % VGE_LUMONSCENE_VIRTUAL_COUNT;
    uint vx = virtualPageIndex & (VGE_LUMONSCENE_VIRTUAL_W - 1u);
    uint vy = virtualPageIndex / VGE_LUMONSCENE_VIRTUAL_W;

    uint packedEntry = texelFetch(pageTableMip0, ivec3(int(vx), int(vy), int(chunkSlot)), 0).x;
    outPhysicalPageId = packedEntry & VGE_LUMONSCENE_PAGE_PHYS_ID_MASK;
    outFlags = packedEntry >> VGE_LUMONSCENE_PAGE_FLAG_SHIFT;

    if (outPhysicalPageId == 0u)
    {
        return false;
    }

    // Only use fully ready pages for shading.
    if ((outFlags & VGE_LUMONSCENE_FLAG_RESIDENT) == 0u)
    {
        return false;
    }
    if ((outFlags & (VGE_LUMONSCENE_FLAG_NEEDS_CAPTURE | VGE_LUMONSCENE_FLAG_NEEDS_RELIGHT)) != 0u)
    {
        return false;
    }

    uint pageIndex = outPhysicalPageId - 1u;
    uint atlasIndex = pageIndex / uint(tilesPerAtlas);
    uint local = pageIndex - atlasIndex * uint(tilesPerAtlas);
    uint tileY = local / uint(tilesPerAxis);
    uint tileX = local - tileY * uint(tilesPerAxis);

    ivec2 base = ivec2(int(tileX) * tileSizeTexels, int(tileY) * tileSizeTexels);
    ivec2 inTile = ivec2(
        int(clamp(floor(patchUv01.x * float(tileSizeTexels)), 0.0, float(tileSizeTexels - 1))),
        int(clamp(floor(patchUv01.y * float(tileSizeTexels)), 0.0, float(tileSizeTexels - 1))));

    ivec3 atlasTexel = ivec3(base + inTile, int(atlasIndex));
    outIrradiance = texelFetch(irradianceAtlas, atlasTexel, 0).rgb;
    return true;
}

#endif // VGE_LUMONSCENE_SURFACE_CACHE_GLSL
