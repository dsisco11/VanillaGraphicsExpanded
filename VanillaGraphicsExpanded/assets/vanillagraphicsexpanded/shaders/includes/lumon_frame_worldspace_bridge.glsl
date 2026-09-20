#ifndef LUMON_FRAME_WORLDSPACE_BRIDGE_GLSL
#define LUMON_FRAME_WORLDSPACE_BRIDGE_GLSL

@import "./lumon_ubos.glsl"

ivec3 LumonFrameMatrixSpacePosToWorldCell(vec3 posRelBlocks)
{
    ivec3 baseCell = ivec3(floor(posRelBlocks + matrixSpaceWorldBlockOffsetRem));
    return baseCell + matrixSpaceWorldChunkCoordOffset * 32;
}

vec3 LumonFrameMatrixSpacePosToWorldPosAbs(vec3 posRelBlocks)
{
    return posRelBlocks
        + matrixSpaceWorldBlockOffsetRem
        + vec3(matrixSpaceWorldChunkCoordOffset) * 32.0;
}

#endif
