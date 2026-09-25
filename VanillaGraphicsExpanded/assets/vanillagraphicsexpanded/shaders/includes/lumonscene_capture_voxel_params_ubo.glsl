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

    // First X lane enables bounded dispatch diagnostics; remaining lanes preserve the existing layout.
    ivec4 reservedGeometry[3];
} vgeCaptureVoxelParams;

#endif // LUMONSCENE_CAPTURE_VOXEL_PARAMS_UBO_GLSL
