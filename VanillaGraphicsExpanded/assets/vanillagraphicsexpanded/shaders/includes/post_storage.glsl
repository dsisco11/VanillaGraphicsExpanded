#ifndef VGE_POST_STORAGE_GLSL
#define VGE_POST_STORAGE_GLSL
@import "./pbr_storage_safety.glsl"

/** Contains RGB at owned RGBA16F boundaries; NaN uses a neutral current-frame fallback, never old image contents. */
vec3 VgePostStoredLight(vec3 value)
{
    vec3 contained;
    bool clipped;
    VgeProtectLightingStorage(value, 0.0, VGE_STORAGE_HALF_MAX, contained, clipped);
    // These effects have no reusable lighting-history validity. A complete neutral
    // write prevents a rejected sample from exposing last frame's differently scaled pixel.
    if (any(isnan(value))) return vec3(0);
    return contained;
}

/** Validates display-only camera arithmetic independently of the frozen scene scale. */
float VgePostExposure(float ev)
{
    if (isnan(ev) || isinf(ev)) return 0.0;
    return exp2(clamp(ev, -48.0, 24.0)) * lightingStorage.y;
}
#endif
