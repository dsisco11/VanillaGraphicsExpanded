// LumOnScene FeedbackCompactPages params UBO
//
// Non-opaque uniforms for lumonscene_feedback_compact_pages.csh.

#ifndef LUMONSCENE_FEEDBACK_COMPACT_PARAMS_UBO_GLSL
#define LUMONSCENE_FEEDBACK_COMPACT_PARAMS_UBO_GLSL

@import "./vge_ubo_bindings.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeLumOnSceneFeedbackCompactParamsUBO
{
    // x = maxRequests
    // y = frameStamp
    // z = scanOffset
    // w = compactMode (0=emit mapped pages only, 1=emit unmapped pages only)
    uvec4 u0;
} vgeFeedbackCompactParams;

#endif // LUMONSCENE_FEEDBACK_COMPACT_PARAMS_UBO_GLSL
