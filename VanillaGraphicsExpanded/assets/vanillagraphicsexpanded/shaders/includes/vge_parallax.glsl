#ifndef VGE_PARALLAX_GLSL
#define VGE_PARALLAX_GLSL

// Parallax mapping helpers for vanilla shader patches.
//
// Requires:
// - vge_normaldepth.glsl (ReadHeightSigned + VgeTryBuildTbnFromDerivatives)
// - `worldPosWs` is camera-relative world space (VS chunk shaders use worldPos.xyz that is camera-relative)

#ifndef VGE_PBR_ENABLE_PARALLAX
  #define VGE_PBR_ENABLE_PARALLAX 0
#endif

#ifndef VGE_PBR_PARALLAX_SCALE
  #define VGE_PBR_PARALLAX_SCALE 0.03
#endif

vec2 VgeApplyParallaxUv_WithTbn(vec2 uv, mat3 tbn, float handedness, vec3 worldPosWs)
{
#if VGE_PBR_ENABLE_PARALLAX
    // Camera-relative convention: fragment-to-camera is -worldPosWs.
    vec3 viewDirWs = normalize(-worldPosWs);

    // Transform view direction into tangent space.
    vec3 viewDirTs = transpose(tbn) * viewDirWs;
    viewDirTs.y *= handedness;

    // Height is centered around 0 (signed) and scaled.
    float height = ReadHeightSigned(uv);

    // VGE: Reduce shimmer/swimming by fading parallax at grazing angles and with distance.
    // - Parallax is most unstable/noticeable when viewDirTs.z is small (grazing).
    // - Far away, sub-texel offsets shimmer due to screen-space sampling.
    float angleWeight = smoothstep(0.35, 0.85, viewDirTs.z);
    float dist = length(worldPosWs);
    float distWeight = 1.0 - smoothstep(3.0, 14.0, dist);
    float weight = angleWeight * distWeight;

    // Basic parallax offset mapping (cheap, stable).
    float denom = max(viewDirTs.z, 0.6);
    vec2 offset = (viewDirTs.xy / denom) * (height * float(VGE_PBR_PARALLAX_SCALE) * weight);

    // VGE: Terrain UVs are atlas UVs. A too-large parallax scale will jump across atlas tiles,
    // which looks like global texture corruption. Clamp to a small number of atlas texels.
    // Note: this intentionally keeps behavior stable even if config scale is set too high.
    vec2 texel = 1.0 / vec2(textureSize(vge_normalDepthTex, 0));
    vec2 maxOffset = texel * 2.0;
    offset = clamp(offset, -maxOffset, maxOffset);

    // Extra safety: if anything goes invalid, disable parallax for this fragment.
    if (any(isnan(offset)) || any(isinf(offset)))
    {
      return uv;
    }

    return uv + offset;
#else
    return uv;
#endif
}

  vec2 VgeApplyParallaxUv(vec2 uv, vec3 geometricNormalWs, vec3 worldPosWs)
  {
  #if VGE_PBR_ENABLE_PARALLAX
    vec3 nGeom = normalize(geometricNormalWs);

    mat3 tbn;
    float handedness;
    VgeTryBuildTbnFromDerivatives(worldPosWs, uv, nGeom, tbn, handedness);

    return VgeApplyParallaxUv_WithTbn(uv, tbn, handedness, worldPosWs);
  #else
    return uv;
  #endif
  }

#endif // VGE_PARALLAX_GLSL
