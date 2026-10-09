#define VGE_VIEW_INPUTS 1
@import "../vge_frame_ubo.glsl"
#define VGE_VIEW_MATRIX vgeFrame.viewMatrix
#define VGE_TERRAIN_NORMAL_INPUTS 1
layout(std140, binding = 28) uniform EyeInputs
{
    vec3 surface;
    int outputMode;
    int vge_twoSidedTerrain;
};
