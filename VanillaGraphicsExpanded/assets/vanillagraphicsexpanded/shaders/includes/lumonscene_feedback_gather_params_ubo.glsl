// LumOnScene FeedbackGather params UBO
//
// Non-opaque uniforms for lumonscene_feedback_gather.csh.

#ifndef LUMONSCENE_FEEDBACK_GATHER_PARAMS_UBO_GLSL
#define LUMONSCENE_FEEDBACK_GATHER_PARAMS_UBO_GLSL

@import "./vge_ubo_bindings.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeLumOnSceneFeedbackGatherParamsUBO
{
    // x = maxRequests
    // y = frameIndex (stored as uint)
    // z = sampleCount
    // w reserved
    uvec4 u0;

    // x = screenWidth
    // y = screenHeight
    // z/w reserved
    uvec4 u1;
} vgeFeedbackGatherParams;

#endif // LUMONSCENE_FEEDBACK_GATHER_PARAMS_UBO_GLSL
