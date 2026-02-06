// LumOn world-probe resolve params UBO
//
// Non-opaque uniforms for lumon_worldprobe_*_resolve.vsh.

#ifndef LUMON_WORLDPROBE_RESOLVE_PARAMS_UBO_GLSL
#define LUMON_WORLDPROBE_RESOLVE_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeLumOnWorldProbeResolveParamsUBO
{
    // atlasSize.xy, reserved.zw
    vec4 atlasSize0;
} vgeLumOnWorldProbeResolveParams;

#define atlasSize (vgeLumOnWorldProbeResolveParams.atlasSize0.xy)

#endif // LUMON_WORLDPROBE_RESOLVE_PARAMS_UBO_GLSL
