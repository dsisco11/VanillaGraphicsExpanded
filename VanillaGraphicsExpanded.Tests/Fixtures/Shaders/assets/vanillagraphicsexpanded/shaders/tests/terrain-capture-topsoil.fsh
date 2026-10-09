#version 330 core
#extension GL_ARB_shading_language_420pack : require
@import "../includes/pbr_color.glsl"
layout(std140, binding = 28) uniform TerrainCaptureInputs { vec4 material; vec4 controls; };
layout(location=0) out vec4 outColor;
#define NORMALVIEW 0
// The independent main retains the engine capture boundary and cutout-before-publication ordering.
void main()
{
    vec4 texColor = material;
    outColor = texColor;
    float murkiness = 0.0;
    if (material.a < controls.x) discard;
    outColor = vec4(9.0, 8.0, 7.0, material.a);
}
