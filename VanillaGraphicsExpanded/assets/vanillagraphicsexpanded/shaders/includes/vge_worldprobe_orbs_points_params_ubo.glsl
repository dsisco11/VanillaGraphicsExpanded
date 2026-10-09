// VGE worldprobe orbs/points params UBO
//
// Non-opaque uniforms for vge_worldprobe_orbs_points.vsh.

#ifndef VGE_WORLDPROBE_ORBS_POINTS_PARAMS_UBO_GLSL
#define VGE_WORLDPROBE_ORBS_POINTS_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"
@import "./vge_frame_ubo.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeWorldProbeOrbsPointsParamsUBO
{
    // worldOffset.xyz, pointSize.w
    vec4 worldOffset_pointSize;

    // fadeNear.x, fadeFar.y, importanceColorMode.z, reserved.w
    vec4 fade0;
} vgeWorldProbeOrbsPointsParams;

#define worldOffset (vgeWorldProbeOrbsPointsParams.worldOffset_pointSize.xyz)
#define pointSize (vgeWorldProbeOrbsPointsParams.worldOffset_pointSize.w)
#define fadeNear (vgeWorldProbeOrbsPointsParams.fade0.x)
#define fadeFar (vgeWorldProbeOrbsPointsParams.fade0.y)
#define importanceColorMode int(vgeWorldProbeOrbsPointsParams.fade0.z)

#endif // VGE_WORLDPROBE_ORBS_POINTS_PARAMS_UBO_GLSL
