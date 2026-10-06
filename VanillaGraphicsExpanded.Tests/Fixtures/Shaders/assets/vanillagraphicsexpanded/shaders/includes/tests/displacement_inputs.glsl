#define VGE_TESSELLATION_INPUTS 1
layout(std140, binding = 28) uniform DisplacementInputs
{
    mat4 mvpMatrix;
    mat4 modelViewMatrix;
    mat4 projectionMatrix;
    float vge_tessellationFocalPixels;
    int vge_displacementEnabled;
    vec4 vge_tessellationPixels;
    vec2 vge_tessellationDistance;
    vec3 sampleInput;
    float distance;
};
