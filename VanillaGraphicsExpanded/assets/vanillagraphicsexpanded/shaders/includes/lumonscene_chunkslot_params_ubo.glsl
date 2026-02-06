// LumonScene chunkSlot params UBO
//
// Non-opaque uniforms for lumonscene_chunkslot.glsl.

#ifndef LUMONSCENE_CHUNKSLOT_PARAMS_UBO_GLSL
#define LUMONSCENE_CHUNKSLOT_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeLumonSceneChunkSlotParamsUBO
{
    // originMinChunk.xyz
    ivec4 originMinChunk0;

    // dims.xyz
    ivec4 dims0;

    // ring.xyz
    ivec4 ring0;
} vgeLumonSceneChunkSlotParams;

#define vge_lumonSceneChunkSlotOriginMinChunk (vgeLumonSceneChunkSlotParams.originMinChunk0.xyz)
#define vge_lumonSceneChunkSlotDims (vgeLumonSceneChunkSlotParams.dims0.xyz)
#define vge_lumonSceneChunkSlotRing (vgeLumonSceneChunkSlotParams.ring0.xyz)

#endif // LUMONSCENE_CHUNKSLOT_PARAMS_UBO_GLSL
