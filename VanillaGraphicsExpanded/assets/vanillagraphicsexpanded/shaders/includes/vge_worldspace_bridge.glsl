// Shared world/matrix space bridge helpers.
//
// Used by:
// - Patched vanilla terrain shaders (PatchId + chunkSlot mapping)
// - LumOn debug shaders (TraceScene sampling)

#ifndef VGE_WORLDSPACE_BRIDGE_GLSL
#define VGE_WORLDSPACE_BRIDGE_GLSL

@import "./vge_frame_ubo.glsl"

// Convert matrix-space position (block units) to absolute world cell (block coord).
ivec3 VgeMatrixSpacePosToWorldCell(vec3 posRelBlocks)
{
    ivec3 baseCell = ivec3(floor(posRelBlocks + vgeFrame.renderOriginBlockRemainder.xyz));
    return baseCell + vgeFrame.renderOriginChunkCoord.xyz * 32;
}

// Convert matrix-space position (block units) to absolute world position (block units, float).
// Use when you need a continuous world position (e.g., ray origins for occupancy sampling).
vec3 VgeMatrixSpacePosToWorldPosAbs(vec3 posRelBlocks)
{
    return posRelBlocks
        + vgeFrame.renderOriginBlockRemainder.xyz
        + vec3(vgeFrame.renderOriginChunkCoord.xyz) * 32.0;
}

// Convert absolute world position (block units, float) back to matrix-space position (block units, float).
vec3 VgeWorldPosAbsToMatrixSpacePos(vec3 posAbsBlocks)
{
    return posAbsBlocks
        - vgeFrame.renderOriginBlockRemainder.xyz
        - vec3(vgeFrame.renderOriginChunkCoord.xyz) * 32.0;
}

// Convert matrix-space position (block units) to absolute world chunk coord (chunk size = 32 blocks).
ivec3 VgeMatrixSpacePosToWorldChunkCoord(vec3 posRelBlocks)
{
    vec3 p = posRelBlocks + vgeFrame.renderOriginBlockRemainder.xyz;
    ivec3 local = ivec3(floor(p * (1.0 / 32.0)));
    return local + vgeFrame.renderOriginChunkCoord.xyz;
}

#endif // VGE_WORLDSPACE_BRIDGE_GLSL
