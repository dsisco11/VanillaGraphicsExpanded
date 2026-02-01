// Shared world/matrix space bridge helpers.
//
// Used by:
// - Patched vanilla terrain shaders (PatchId + chunkSlot mapping)
// - LumOn debug shaders (TraceScene sampling)

#ifndef VGE_WORLDSPACE_BRIDGE_GLSL
#define VGE_WORLDSPACE_BRIDGE_GLSL

@import "./lumon_terrain_bridge_ubo.glsl"

// Convert matrix-space position (block units) to absolute world cell (block coord).
ivec3 VgeMatrixSpacePosToWorldCell(vec3 posRelBlocks)
{
    ivec3 baseCell = ivec3(floor(posRelBlocks + vge_lumonSceneWorldBlockOffsetRem));
    return baseCell + vge_lumonSceneWorldChunkCoordOffset * 32;
}

// Convert matrix-space position (block units) to absolute world chunk coord (chunk size = 32 blocks).
ivec3 VgeMatrixSpacePosToWorldChunkCoord(vec3 posRelBlocks)
{
    vec3 p = posRelBlocks + vge_lumonSceneWorldBlockOffsetRem;
    ivec3 local = ivec3(floor(p * (1.0 / 32.0)));
    return local + vge_lumonSceneWorldChunkCoordOffset;
}

#endif // VGE_WORLDSPACE_BRIDGE_GLSL

