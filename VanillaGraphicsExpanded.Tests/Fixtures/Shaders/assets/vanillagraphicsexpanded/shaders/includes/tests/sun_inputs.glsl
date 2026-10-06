#define VGE_ATMOSPHERE_SUN_INPUTS 1
layout(std140, binding = 28) uniform SunInputs
{
    int vge_atmosphereSunDraw;
    int vge_sceneLinear;
    vec4 vge_atmosphereSun;
    vec4 vge_atmosphereDisk;
    vec3 camera;
};
