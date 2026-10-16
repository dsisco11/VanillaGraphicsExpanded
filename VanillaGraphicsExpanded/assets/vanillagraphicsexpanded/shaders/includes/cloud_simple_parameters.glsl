#ifndef VGE_CLOUD_SIMPLE_PARAMETERS
#define VGE_CLOUD_SIMPLE_PARAMETERS
@import "./vge_frame_ubo.glsl"
layout(std140, binding = 28) uniform CloudSimpleParameters
{
    mat4 cloudProjection;
    mat4 cloudModelView;
    vec4 cloudOffsetLength;
    vec4 cloudTileClockAlpha;
    vec4 cloudPerception; // effect id, local intensity (native factor 20), time, sphere count
    vec4 cloudPlayer;
    vec4 cloudSolar;
    vec4 cloudLunar;
    vec4 cloudEnvironment;
    vec4 cloudSun;
    vec4 cloudMoon;
    vec4 cloudAerial; // observer altitude, horizon, underwater, lookup decoding
    vec4 cloudFog; // density, minimum, flat density, flat start
    vec4 cloudFogColor;
    vec4 cloudMurk;
    vec4 cloudReserved;
    vec4 cloudSpheres[6]; // pairs of position/radius and density/RGB, at most three spheres
};
#define VGE_ATMOSPHERE_SCATTERING_DIRECTIONS
#define vge_atmosphereScatteringSun cloudSun.xyz
#define vge_atmosphereScatteringMoon cloudMoon.xyz
#endif
