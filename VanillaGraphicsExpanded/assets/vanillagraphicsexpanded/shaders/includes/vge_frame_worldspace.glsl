#ifndef VGE_FRAME_WORLDSPACE_GLSL
#define VGE_FRAME_WORLDSPACE_GLSL

@import "./vge_frame_ubo.glsl"

/** Restores exact world-cell coordinates from render-relative positions and the split origin. */
ivec3 VgeFrameMatrixSpaceToWorldCell(vec3 posRelBlocks)
{
    ivec3 baseCell = ivec3(floor(posRelBlocks + vgeFrame.renderOriginBlockRemainder.xyz));
    return baseCell + vgeFrame.renderOriginChunkCoord.xyz * 32;
}

/** Restores a floating-point world position when a consumer needs a continuous coordinate. */
vec3 VgeFrameMatrixSpaceToWorldPosition(vec3 posRelBlocks)
{
    return posRelBlocks
        + vgeFrame.renderOriginBlockRemainder.xyz
        + vec3(vgeFrame.renderOriginChunkCoord.xyz) * 32.0;
}

#endif
