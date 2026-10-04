#ifndef VGE_WATER_REFRACTION_SELECTION_GLSL
#define VGE_WATER_REFRACTION_SELECTION_GLSL
#if !defined(VGE_WATER_REFRACTION_QUALITY) || VGE_WATER_REFRACTION_QUALITY > 0
@import "./refraction.glsl"
#endif
@import "./uv_distortion.glsl"

/** Prefers budgeted geometric coverage, then a separately classified validated approximate receiver. */
#ifdef VGE_WATER_REFRACTION_QUALITY
VgeWaterReceiver VgeWaterSelectRefraction(vec3 surface, vec3 normalVS, bool underwater)
#else
VgeWaterReceiver VgeWaterSelectRefraction(vec3 surface, vec3 normalVS, bool underwater, int quality)
#endif
{
#ifdef VGE_WATER_REFRACTION_QUALITY
#if VGE_WATER_REFRACTION_QUALITY == 0
    return VgeWaterUvRefraction(surface, normalVS, underwater);
#else
    const int budget = 1 << VGE_WATER_REFRACTION_QUALITY;
#endif
#else
    if (quality == 0) return VgeWaterUvRefraction(surface, normalVS, underwater);
    int budget = quality == 1 ? 2 : quality == 2 ? 4 : 8;
#endif
#if !defined(VGE_WATER_REFRACTION_QUALITY) || VGE_WATER_REFRACTION_QUALITY > 0
    VgeRefractionSupport seed;
    bool seedValid;
    VgeWaterReceiver receiver = VgeWaterRefraction(surface, normalVS, underwater, budget, seed, seedValid);
    // Only the distance-zero support belongs to UV thickness estimation. A later
    // probe cannot repair an invalid seed or replace it with different geometry.
    if (receiver.valid || !seedValid) return receiver;
    return VgeWaterUvRefractionFromSeed(surface, normalVS, underwater, receiver.refractedDirectionVS, seed);
#endif
}
#endif
