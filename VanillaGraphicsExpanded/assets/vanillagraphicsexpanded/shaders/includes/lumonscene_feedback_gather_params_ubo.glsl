// LumOnScene FeedbackGather params UBO
//
// Non-opaque uniforms for lumonscene_feedback_gather.csh.

#ifndef LUMONSCENE_FEEDBACK_GATHER_PARAMS_UBO_GLSL
#define LUMONSCENE_FEEDBACK_GATHER_PARAMS_UBO_GLSL

@import "./vge_ubo_bindings.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeLumOnSceneFeedbackGatherParamsUBO
{
    // x = maxRequests
    // y reserved; randomized sampling uses VgeFrameUBO.frameIndex
    // z = sampleCount
    // w reserved
    uvec4 u0;

    // x = patch-ID source width
    // y = patch-ID source height
    // z/w reserved
    uvec4 u1;
} vgeFeedbackGatherParams;

#endif // LUMONSCENE_FEEDBACK_GATHER_PARAMS_UBO_GLSL
