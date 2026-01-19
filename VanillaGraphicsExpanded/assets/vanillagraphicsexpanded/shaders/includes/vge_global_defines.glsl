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

// Short-range AO toggle (formerly: VGE_LUMON_ENABLE_BENT_NORMAL)
//
// Backwards-compatibility:
// - If callers inject the legacy define, mirror it into the new define.
// - If callers inject the new define, mirror it into the legacy define.
//
// This keeps older mod builds/tests working while we migrate terminology.
#ifndef VGE_LUMON_ENABLE_SHORT_RANGE_AO
  #ifdef VGE_LUMON_ENABLE_BENT_NORMAL
    #define VGE_LUMON_ENABLE_SHORT_RANGE_AO VGE_LUMON_ENABLE_BENT_NORMAL
  #else
    #define VGE_LUMON_ENABLE_SHORT_RANGE_AO 1
  #endif
#endif

#ifndef VGE_LUMON_ENABLE_BENT_NORMAL
  #define VGE_LUMON_ENABLE_BENT_NORMAL VGE_LUMON_ENABLE_SHORT_RANGE_AO
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

// Phase 6: Loop-bound knobs (compile-time)
#ifndef VGE_LUMON_RAYS_PER_PROBE
  #define VGE_LUMON_RAYS_PER_PROBE 12
#endif

#ifndef VGE_LUMON_RAY_STEPS
  #define VGE_LUMON_RAY_STEPS 10
#endif

#ifndef VGE_LUMON_ATLAS_TEXELS_PER_FRAME
  #define VGE_LUMON_ATLAS_TEXELS_PER_FRAME 16
#endif

// Phase 6: Ray/trace tuning knobs (compile-time)
#ifndef VGE_LUMON_RAY_MAX_DISTANCE
  #define VGE_LUMON_RAY_MAX_DISTANCE 4.0
#endif

#ifndef VGE_LUMON_RAY_THICKNESS
  #define VGE_LUMON_RAY_THICKNESS 0.5
#endif

#ifndef VGE_LUMON_HZB_COARSE_MIP
  #define VGE_LUMON_HZB_COARSE_MIP 4
#endif

#ifndef VGE_LUMON_SKY_MISS_WEIGHT
  #define VGE_LUMON_SKY_MISS_WEIGHT 0.5
#endif
