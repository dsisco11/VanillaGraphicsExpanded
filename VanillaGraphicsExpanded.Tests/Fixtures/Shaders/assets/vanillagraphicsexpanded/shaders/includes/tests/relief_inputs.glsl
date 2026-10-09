#define VGE_VIEW_INPUTS 1
@import "../vge_frame_ubo.glsl"
#define VGE_VIEW_MATRIX vgeFrame.viewMatrix
#define VGE_TERRAIN_NORMAL_INPUTS 1
layout(std140, binding = 28) uniform ReliefInputs
{
    int vge_twoSidedTerrain;
    vec2 metric;
};
