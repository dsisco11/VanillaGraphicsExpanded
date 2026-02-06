// PBR normal-depth bake params UBO
//
// Non-opaque uniforms for pbr_normaldepth_bake.fsh.

#ifndef PBR_NORMALDEPTH_BAKE_PARAMS_UBO_GLSL
#define PBR_NORMALDEPTH_BAKE_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgePbrNormalDepthBakeParamsUBO
{
    // vge_useLuminanceDepth.x, reserved.yzw
    ivec4 ints0;
} vgePbrNormalDepthBakeParams;

#define vge_useLuminanceDepth (vgePbrNormalDepthBakeParams.ints0.x)

#endif // PBR_NORMALDEPTH_BAKE_PARAMS_UBO_GLSL
