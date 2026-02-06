// LumOnScene CaptureVoxel params UBO
//
// Non-opaque uniforms for lumonscene_capture_voxel.csh.

#ifndef LUMONSCENE_CAPTURE_VOXEL_PARAMS_UBO_GLSL
#define LUMONSCENE_CAPTURE_VOXEL_PARAMS_UBO_GLSL

@import "./vge_ubo_bindings.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeLumOnSceneCaptureVoxelParamsUBO
{
    // tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels
    uvec4 atlasLayout;

    // occOriginMinCell0.xyz
    ivec4 occOriginMinCell0;

    // occRing0.xyz
    ivec4 occRing0;

    // x = occResolution, yzw reserved
    ivec4 occInts0;
} vgeCaptureVoxelParams;

#endif // LUMONSCENE_CAPTURE_VOXEL_PARAMS_UBO_GLSL
