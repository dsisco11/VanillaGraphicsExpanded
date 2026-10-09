#ifndef VGE_LIGHTS_UBO_GLSL
#define VGE_LIGHTS_UBO_GLSL
#extension GL_ARB_shading_language_420pack : require
@import "./vge_ubo_bindings.glsl"
// Exact engine view-space positions and calibrated colors; attenuation stays with the shading model.
// std140: count at 0, positions at 16, colors at 1616, total 3216 bytes.
layout(std140, binding = VGE_UBO_LIGHTS_BINDING) uniform VgeLightsUBO
{
    uint lightCount;
    vec4 positions[100];
    vec4 colors[100];
} vgeLights;
#endif
