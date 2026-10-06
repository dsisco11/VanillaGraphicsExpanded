#define VGE_VIEW_INPUTS 1
#define VGE_TERRAIN_NORMAL_INPUTS 1
layout(std140, binding = 28) uniform EyeInputs
{
    mat4 modelViewMatrix;
    vec3 surface;
    int outputMode;
    int vge_twoSidedTerrain;
};
