#ifndef VGE_SCENE_STORAGE_GLSL
#define VGE_SCENE_STORAGE_GLSL
@import "./pbr_frame_lighting.glsl"
@import "./pbr_storage_safety.glsl"

/** Encodes physical lighting and contains exceptional values before a binary16 scene write. */
vec3 VgeStoreSceneLighting(vec3 physical)
{
    vec3 protectedValue;
    bool clipped;
    if (lightingStorage.w < .5) return vec3(0);
    VgeProtectLightingStorage(VgeStoreFrameLight(physical), 0.0, VGE_STORAGE_HALF_MAX,
        protectedValue, clipped);
    // Scene RGB uses an explicit finite fallback. Cache/history producers retain
    // their separate validity and rejection contracts rather than publishing this.
    return protectedValue;
}

/** Contains stored RGB before narrowing to unsigned packed scene composition. */
vec3 VgeProtectPackedSceneLighting(vec3 stored)
{
    vec3 protectedValue;
    bool clipped;
    VgeProtectLightingStorage(stored, 0.0, VGE_STORAGE_B10_MAX, protectedValue, clipped);
    return protectedValue;
}
#endif
