#version 330 core

out vec4 outColor;

@import "./includes/vge_global_defines.glsl"

void main()
{
#if VGE_LUMON_ENABLED
    // Exercise additional global defines too.
    #if VGE_LUMON_PBR_COMPOSITE && VGE_LUMON_ENABLE_AO && VGE_LUMON_ENABLE_SHORT_RANGE_AO
        outColor = vec4(1.0, 0.0, 0.0, 1.0);
    #else
        outColor = vec4(0.0, 1.0, 0.0, 1.0);
    #endif
#else
    outColor = vec4(0.0, 0.0, 0.0, 1.0);
#endif
}
