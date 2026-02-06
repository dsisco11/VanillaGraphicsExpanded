// ============================================================================
// VGE std140 UBO Layout Helper
//
// GLSL < 420 cannot compile layout(binding=...) for uniform blocks without
// GL_ARB_shading_language_420pack. Use this macro to keep sources portable:
// - On 420+: emits layout(std140, binding = N)
// - On older: emits layout(std140) and bindings are assigned via C# using
//   glUniformBlockBinding.
// ============================================================================

#ifndef VGE_UBO_LAYOUT_GLSL
#define VGE_UBO_LAYOUT_GLSL

@import "./vge_ubo_bindings.glsl"

#if __VERSION__ >= 420
  #define VGE_UBO_LAYOUT(BINDING) layout(std140, binding = BINDING)
#else
  #define VGE_UBO_LAYOUT(BINDING) layout(std140)
#endif

#endif // VGE_UBO_LAYOUT_GLSL
