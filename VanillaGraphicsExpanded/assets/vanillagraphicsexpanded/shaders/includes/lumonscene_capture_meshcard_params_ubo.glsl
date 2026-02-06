// LumOnScene CaptureMeshCard params UBO
//
// Non-opaque uniforms for lumonscene_capture_meshcard.csh.

#ifndef LUMONSCENE_CAPTURE_MESHCARD_PARAMS_UBO_GLSL
#define LUMONSCENE_CAPTURE_MESHCARD_PARAMS_UBO_GLSL

@import "./vge_ubo_bindings.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeLumOnSceneCaptureMeshCardParamsUBO
{
    // tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels
    uvec4 atlasLayout;

    // x = captureDepthRange, yzw reserved
    vec4 captureFloats0;
} vgeCaptureMeshCardParams;

#endif // LUMONSCENE_CAPTURE_MESHCARD_PARAMS_UBO_GLSL
