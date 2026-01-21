#ifndef VGE_POM_GLSL
#define VGE_POM_GLSL

// Parallax Occlusion Mapping (POM) helpers for atlas-based terrain shaders.
//
// Requirements:
// - vge_normaldepth.glsl for ReadHeight01/ReadHeightSigned and VgeTryBuildTbnFromDerivatives
// - `uniform sampler2D vge_normalDepthTex;`
// - Per-face atlas tile rect: (uvBase, uvExtent) in atlas UV space.
//   If rect is unavailable, call sites should fall back to Tier 0/1.

#ifndef VGE_PBR_ENABLE_POM
  #define VGE_PBR_ENABLE_POM 0
#endif

#ifndef VGE_PBR_POM_SCALE
  #define VGE_PBR_POM_SCALE 0.03
#endif

#ifndef VGE_PBR_POM_MIN_STEPS
  #define VGE_PBR_POM_MIN_STEPS 8
#endif

#ifndef VGE_PBR_POM_MAX_STEPS
  #define VGE_PBR_POM_MAX_STEPS 24
#endif

#ifndef VGE_PBR_POM_REFINEMENT_STEPS
  #define VGE_PBR_POM_REFINEMENT_STEPS 4
#endif

#ifndef VGE_PBR_POM_FADE_START
  #define VGE_PBR_POM_FADE_START 3.0
#endif

#ifndef VGE_PBR_POM_FADE_END
  #define VGE_PBR_POM_FADE_END 14.0
#endif

#ifndef VGE_PBR_POM_MAX_TEXELS
  // Hard safety clamp to avoid cross-tile bleeding and mip/derivative thrash.
  #define VGE_PBR_POM_MAX_TEXELS 2.0
#endif

vec2 VgeClampUvToRect(vec2 uv, vec2 uvBase, vec2 uvExtent)
{
    vec2 uvMin = uvBase;
    vec2 uvMax = uvBase + uvExtent;

    // Keep at least 1 texel margin for stability.
    vec2 texel = 1.0 / vec2(textureSize(vge_normalDepthTex, 0));
    return clamp(uv, uvMin + texel, uvMax - texel);
}

float VgeReadPomDepth01(vec2 uv)
{
    // VGE bake stores height around 0.5 ~= neutral. For POM we treat values below 0.5 as "indentation".
    // This avoids extrusion artifacts and improves temporal stability.
    float h01 = ReadHeight01(uv);
    float depth01 = clamp(0.5 - h01, 0.0, 0.5) * 2.0;
    return depth01;
}

vec2 VgeApplyPomUv_WithTbn(vec2 uv, mat3 tbn, float handedness, vec3 worldPosWs, vec2 uvBase, vec2 uvExtent)
{
#if VGE_PBR_ENABLE_POM
    // Rect is required for atlas safety.
    if (uvBase.x < 0.0 || uvExtent.x <= 0.0 || uvExtent.y <= 0.0)
    {
        return uv;
    }

    // Camera-relative convention: fragment-to-camera is -worldPosWs.
    vec3 viewDirWs = normalize(-worldPosWs);

    // Transform view direction into tangent space.
    vec3 viewDirTs = transpose(tbn) * viewDirWs;
    viewDirTs.y *= handedness;

    // Angle + distance stability fades.
    float angleWeight = smoothstep(0.35, 0.85, viewDirTs.z);
    float dist = length(worldPosWs);
    float distWeight = 1.0 - smoothstep(float(VGE_PBR_POM_FADE_START), float(VGE_PBR_POM_FADE_END), dist);
    float weight = angleWeight * distWeight;

    if (weight <= 0.0)
    {
        return uv;
    }

    // Clamp starting UV to rect for safety.
    vec2 baseUv = VgeClampUvToRect(uv, uvBase, uvExtent);

    // Scale the maximum parallax amount.
    float denom = max(viewDirTs.z, 0.2);
    vec2 parallaxDir = (viewDirTs.xy / denom);

    // Steps: more steps at grazing angles.
    float grazing = 1.0 - clamp(viewDirTs.z, 0.0, 1.0);
    int steps = int(mix(float(VGE_PBR_POM_MIN_STEPS), float(VGE_PBR_POM_MAX_STEPS), grazing));
    steps = max(1, steps);

    float layerHeight = 1.0 / float(steps);
    float currentLayerDepth = 0.0;

    vec2 texel = 1.0 / vec2(textureSize(vge_normalDepthTex, 0));
    vec2 maxOffset = texel * float(VGE_PBR_POM_MAX_TEXELS);

    // Per-step delta in UV space.
    vec2 totalOffset = parallaxDir * (float(VGE_PBR_POM_SCALE) * weight);
    totalOffset = clamp(totalOffset, -maxOffset, maxOffset);
    vec2 deltaUv = totalOffset / float(steps);

    // Ray-march.
    vec2 currUv = baseUv;
    float currDepth = VgeReadPomDepth01(currUv);

    vec2 prevUv = currUv;
    float prevDepth = currDepth;

    while (currentLayerDepth < currDepth && currentLayerDepth < 1.0)
    {
        prevUv = currUv;
        prevDepth = currDepth;

        currUv = VgeClampUvToRect(currUv - deltaUv, uvBase, uvExtent);
        currentLayerDepth += layerHeight;
        currDepth = VgeReadPomDepth01(currUv);
    }

    // Linear interpolate between last two samples.
    float after = currDepth - currentLayerDepth;
    float before = prevDepth - (currentLayerDepth - layerHeight);
    float w = before / (before - after + 1e-6);
    vec2 hitUv = mix(currUv, prevUv, clamp(w, 0.0, 1.0));

    // Small binary refinement.
    vec2 aUv = prevUv;
    vec2 bUv = currUv;
    float aLayer = currentLayerDepth - layerHeight;
    float bLayer = currentLayerDepth;

    for (int i = 0; i < int(VGE_PBR_POM_REFINEMENT_STEPS); i++)
    {
        vec2 mUv = VgeClampUvToRect((aUv + bUv) * 0.5, uvBase, uvExtent);
        float mDepth = VgeReadPomDepth01(mUv);
        float mLayer = (aLayer + bLayer) * 0.5;

        if (mLayer < mDepth)
        {
            aUv = mUv;
            aLayer = mLayer;
        }
        else
        {
            bUv = mUv;
            bLayer = mLayer;
        }

        hitUv = mUv;
    }

    if (any(isnan(hitUv)) || any(isinf(hitUv)))
    {
        return uv;
    }

    return VgeClampUvToRect(hitUv, uvBase, uvExtent);
#else
    return uv;
#endif
}

#endif // VGE_POM_GLSL
