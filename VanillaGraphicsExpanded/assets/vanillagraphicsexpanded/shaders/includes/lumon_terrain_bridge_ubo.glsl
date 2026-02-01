// LumOn Terrain Bridge UBO
//
// Provides a stable mapping from the engine's "matrix space" (camera-relative/origin-shifted)
// positions back to absolute world coordinates for voxel-grid sampling.
//
// This is intentionally a tiny standalone UBO to avoid importing the full LumOnFrameUBO into
// vanilla terrain shaders (macro collision risk).

#ifndef LUMON_TERRAIN_BRIDGE_UBO_GLSL
#define LUMON_TERRAIN_BRIDGE_UBO_GLSL

// Binding point contract.
// GLSL 330 requires binding from C# via glUniformBlockBinding / glBindBufferBase.
#define LUMON_UBO_TERRAIN_BRIDGE_BINDING 14

layout(std140) uniform LumOnTerrainBridgeUBO
{
    // xyz = chunkCoord offset between matrix space and world space (in chunk units).
    // w reserved (std140 alignment).
    ivec4 vge_worldChunkCoordOffset;

    // xyz = remainder offset in block units, in [0,32) per axis.
    // w reserved.
    vec4  vge_worldBlockOffsetRem;
} vgeTerrainBridge;

// Compatibility aliases (keep existing uniform names in shader code).
#define vge_lumonSceneWorldChunkCoordOffset (vgeTerrainBridge.vge_worldChunkCoordOffset.xyz)
#define vge_lumonSceneWorldBlockOffsetRem   (vgeTerrainBridge.vge_worldBlockOffsetRem.xyz)

#endif // LUMON_TERRAIN_BRIDGE_UBO_GLSL

