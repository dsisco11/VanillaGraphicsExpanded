#define VGE_VIEW_INPUTS 1
#define VGE_TERRAIN_NORMAL_INPUTS 1
layout(std140, binding = 28) uniform ReliefInputs
{
    mat4 modelViewMatrix;
    int vge_twoSidedTerrain;
    vec2 metric;
};
