// Shared depth hierarchy reduction params UBO
//
// Non-opaque uniforms for vge_depth_reduce.fsh.

#ifndef VGE_DEPTH_HIERARCHY_PARAMS_UBO_GLSL
#define VGE_DEPTH_HIERARCHY_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeDepthHierarchyParamsUBO
{
    // srcMip.x, reserved.yzw
    ivec4 ints0;
} vgeDepthHierarchyParams;

#define srcMip (vgeDepthHierarchyParams.ints0.x)

#endif // VGE_DEPTH_HIERARCHY_PARAMS_UBO_GLSL
