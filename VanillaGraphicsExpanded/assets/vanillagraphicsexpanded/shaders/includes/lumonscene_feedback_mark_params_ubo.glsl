// LumOnScene FeedbackMarkPages params UBO
//
// Non-opaque uniforms for lumonscene_feedback_mark_pages.csh.

#ifndef LUMONSCENE_FEEDBACK_MARK_PARAMS_UBO_GLSL
#define LUMONSCENE_FEEDBACK_MARK_PARAMS_UBO_GLSL

@import "./vge_ubo_bindings.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeLumOnSceneFeedbackMarkParamsUBO
{
    // x = frameStamp
    uvec4 u0;
} vgeFeedbackMarkParams;

#endif // LUMONSCENE_FEEDBACK_MARK_PARAMS_UBO_GLSL
