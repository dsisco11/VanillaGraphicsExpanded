// LumOnScene RelightVoxelDda params UBO
//
// Non-opaque uniforms for lumonscene_relight_voxel_dda.csh.

#ifndef LUMONSCENE_RELIGHT_PARAMS_UBO_GLSL
#define LUMONSCENE_RELIGHT_PARAMS_UBO_GLSL

@import "./vge_ubo_bindings.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeLumOnSceneRelightParamsUBO
{
    // tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels
    uvec4 atlasLayout;

    // texelsPerPagePerFrame, raysPerTexel, maxDdaSteps, debugCountersEnabled
    uvec4 relightUints0;

    // frameIndex (x), occResolution (y), reserved (z,w)
    ivec4 relightInts0;

    // occOriginMinCell0.xyz
    ivec4 occOriginMinCell0;

    // occRing0.xyz
    ivec4 occRing0;
} vgeRelightParams;

#endif // LUMONSCENE_RELIGHT_PARAMS_UBO_GLSL
