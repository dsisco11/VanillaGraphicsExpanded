// LumOn HZB downsample params UBO
//
// Non-opaque uniforms for lumon_hzb_downsample.fsh.

#ifndef LUMON_HZB_PARAMS_UBO_GLSL
#define LUMON_HZB_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeLumOnHzbDownsampleParamsUBO
{
    // srcMip.x, reserved.yzw
    ivec4 ints0;
} vgeLumOnHzbParams;

#define srcMip (vgeLumOnHzbParams.ints0.x)

#endif // LUMON_HZB_PARAMS_UBO_GLSL
