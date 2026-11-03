#version 450 core
layout(std140, binding = 28) uniform AerialLookupInputs
{
    vec3 displacement;
    float visibility;
    vec4 baseRadiance;
    vec4 scatteringSun;
    vec4 scatteringMoon;
    vec4 aerialParameters;
    int directOutput;
};
#define VGE_ATMOSPHERE_SCATTERING_DIRECTIONS
#define vge_atmosphereScatteringSun scatteringSun.xyz
#define vge_atmosphereScatteringMoon scatteringMoon.xyz
@import "../includes/atmosphere_aerial.glsl"


layout(location=0) out vec4 result;
/** Keeps the legacy two-band observation and adds one typed three-band direct decoder observation. */
void main()
{
    vec3 source=directOutput!=0?baseRadiance.rgb:vec3(1.0);
    vec3 parameters=directOutput!=0?aerialParameters.xyz:vec3(.001,0,16);
    result=vec4(VgeApplyAerial(source,displacement,visibility,parameters,vec3(0,1,0)),1.0);
}
