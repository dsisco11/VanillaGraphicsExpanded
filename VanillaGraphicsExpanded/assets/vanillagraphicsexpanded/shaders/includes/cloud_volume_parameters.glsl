#ifndef VGE_CLOUD_VOLUME_PARAMETERS
#define VGE_CLOUD_VOLUME_PARAMETERS
#ifndef VGE_CLOUD_SELF_SHADOWING
#define VGE_CLOUD_SELF_SHADOWING 1
#endif
@import "./vge_frame_ubo.glsl"
layout(std140,binding=28) uniform CloudVolumeParameters
{
    vec4 volumeOffsetWidth;
    vec4 volumeAnchorUnits; // material anchor X/Z, metres per world unit, reserved
    vec4 volumeDetail;
    vec4 volumeSamples;
    vec4 volumeSolar;
    vec4 volumeLunar;
    vec4 volumeEnvironment;
    vec4 volumeSun;
    vec4 volumeMoon;
    vec4 volumeAerial;
    vec4 volumeFog;
    vec4 volumeFogColor;
    vec4 volumePrimary; // minimum view count, budget distance, shadow distance, stop transmittance
    vec4 volumeShadowSunU;
    vec4 volumeShadowMoonU;
};
#define VGE_ATMOSPHERE_SCATTERING_DIRECTIONS
#define vge_atmosphereScatteringSun volumeSun.xyz
#define vge_atmosphereScatteringMoon volumeMoon.xyz
#endif
