// VGE worldprobe orbs/points params UBO
//
// Non-opaque uniforms for vge_worldprobe_orbs_points.vsh.

#ifndef VGE_WORLDPROBE_ORBS_POINTS_PARAMS_UBO_GLSL
#define VGE_WORLDPROBE_ORBS_POINTS_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeWorldProbeOrbsPointsParamsUBO
{
    mat4 modelViewProjectionMatrix;

    // cameraPos.xyz, reserved.w
    vec4 cameraPos0;

    // worldOffset.xyz, pointSize.w
    vec4 worldOffset_pointSize;

    // fadeNear.x, fadeFar.y, reserved.zw
    vec4 fade0;
} vgeWorldProbeOrbsPointsParams;

#define modelViewProjectionMatrix (vgeWorldProbeOrbsPointsParams.modelViewProjectionMatrix)
#define cameraPos (vgeWorldProbeOrbsPointsParams.cameraPos0.xyz)
#define worldOffset (vgeWorldProbeOrbsPointsParams.worldOffset_pointSize.xyz)
#define pointSize (vgeWorldProbeOrbsPointsParams.worldOffset_pointSize.w)
#define fadeNear (vgeWorldProbeOrbsPointsParams.fade0.x)
#define fadeFar (vgeWorldProbeOrbsPointsParams.fade0.y)

#endif // VGE_WORLDPROBE_ORBS_POINTS_PARAMS_UBO_GLSL
