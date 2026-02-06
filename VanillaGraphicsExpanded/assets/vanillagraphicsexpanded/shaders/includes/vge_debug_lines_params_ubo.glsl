// VGE debug-lines params UBO
//
// Non-opaque uniforms for vge_debug_lines.vsh.

#ifndef VGE_DEBUG_LINES_PARAMS_UBO_GLSL
#define VGE_DEBUG_LINES_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeDebugLinesParamsUBO
{
    mat4 modelViewProjectionMatrix;
    // worldOffset.xyz, reserved.w
    vec4 worldOffset0;
} vgeDebugLinesParams;

#define modelViewProjectionMatrix (vgeDebugLinesParams.modelViewProjectionMatrix)
#define worldOffset (vgeDebugLinesParams.worldOffset0.xyz)

#endif // VGE_DEBUG_LINES_PARAMS_UBO_GLSL
