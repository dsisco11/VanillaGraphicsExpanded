#ifndef VGE_LUMONSCENE_CHUNKSLOT_GLSL
#define VGE_LUMONSCENE_CHUNKSLOT_GLSL
// ============================================================================
// LumonScene chunkSlot helpers (Phase 22.X - ChunkSlots)
//
// Purpose:
// - Provide a deterministic mapping from world chunk coordinates to a bounded `chunkSlot`
//   index used by the LumonScene surface-cache page tables.
//
// Notes:
// - This file only defines the math + uniforms. It does not enforce any policy about
//   which window is active (Near/Far); that is provided by the runtime via uniforms.
// - Safe fallback: if dims are not configured (<=0), mapping is treated as disabled and
//   `chunkSlot` defaults to 0 for backwards compatibility.
// ============================================================================

// Runtime-provided slot window parameters.
// - `originMinChunk`: inclusive minimum chunk coord of the active slot window.
// - `dims`: window dimensions in chunks (must be > 0 per axis when enabled).
// - `ring`: ring offsets in chunk units (used to keep slot indices stable under window movement).
uniform ivec3 vge_lumonSceneChunkSlotOriginMinChunk;
uniform ivec3 vge_lumonSceneChunkSlotDims;
uniform ivec3 vge_lumonSceneChunkSlotRing;

// Convert world position in block units to chunk coord (chunk size is 32 blocks).
ivec3 VgeLumonSceneChunkCoordFromWorldPos(vec3 worldPosBlocks)
{
    // floor() handles negative coordinates correctly.
    vec3 c = floor(worldPosBlocks * (1.0 / 32.0));
    return ivec3(c);
}

int VgeLumonSceneModPositive(int v, int m)
{
    if (m <= 0) return 0;
    int r = v % m;
    return (r < 0) ? (r + m) : r;
}

// Maps a chunk coordinate to a slot index.
//
// Returns:
// - true if the chunk is inside the configured window OR mapping is disabled.
// - false if mapping is enabled and the chunk is outside the window.
bool VgeLumonSceneTryMapChunkCoordToSlot(ivec3 chunkCoord, out uint outChunkSlot)
{
    ivec3 dims = vge_lumonSceneChunkSlotDims;
    if (dims.x <= 0 || dims.y <= 0 || dims.z <= 0)
    {
        // Mapping disabled (backwards compatible): treat as "inside" and use slot 0.
        outChunkSlot = 0u;
        return true;
    }

    ivec3 local = chunkCoord - vge_lumonSceneChunkSlotOriginMinChunk;
    if (local.x < 0 || local.y < 0 || local.z < 0
        || local.x >= dims.x || local.y >= dims.y || local.z >= dims.z)
    {
        outChunkSlot = 0u;
        return false;
    }

    ivec3 ring = vge_lumonSceneChunkSlotRing;
    int sx = VgeLumonSceneModPositive(local.x + ring.x, dims.x);
    int sy = VgeLumonSceneModPositive(local.y + ring.y, dims.y);
    int sz = VgeLumonSceneModPositive(local.z + ring.z, dims.z);

    // Row-major (X fastest):
    // chunkSlot = (sy * dimZ + sz) * dimX + sx
    outChunkSlot = uint((sy * dims.z + sz) * dims.x + sx);
    return true;
}

#endif // VGE_LUMONSCENE_CHUNKSLOT_GLSL

