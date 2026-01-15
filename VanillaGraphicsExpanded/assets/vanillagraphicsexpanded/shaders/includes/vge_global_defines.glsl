// Shared global compile-time defines for VanillaGraphicsExpanded shaders.
//
// Usage:
//   @import "./includes/vge_global_defines.glsl"
//
// Conventions:
// - Use integer (0/1) defines for feature toggles so they can be used with `#if`.
// - Defaults here must represent safe, sane behavior if C# does not inject a define.

#ifndef VGE_LUMON_ENABLED
  #define VGE_LUMON_ENABLED 1
#endif

#ifndef VGE_LUMON_PBR_COMPOSITE
  #define VGE_LUMON_PBR_COMPOSITE 1
#endif

#ifndef VGE_LUMON_ENABLE_AO
  #define VGE_LUMON_ENABLE_AO 1
#endif

#ifndef VGE_LUMON_ENABLE_BENT_NORMAL
  #define VGE_LUMON_ENABLE_BENT_NORMAL 1
#endif

// Phase 4: Upsample toggles
#ifndef VGE_LUMON_UPSAMPLE_DENOISE
  #define VGE_LUMON_UPSAMPLE_DENOISE 1
#endif

#ifndef VGE_LUMON_UPSAMPLE_HOLEFILL
  #define VGE_LUMON_UPSAMPLE_HOLEFILL 1
#endif

// Phase 5: Temporal reprojection mode
#ifndef VGE_LUMON_TEMPORAL_USE_VELOCITY_REPROJECTION
  #define VGE_LUMON_TEMPORAL_USE_VELOCITY_REPROJECTION 1
#endif
