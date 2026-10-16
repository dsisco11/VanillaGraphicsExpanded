#extension GL_ARB_shading_language_420pack : require
#ifndef VGE_PBR_MICROFACET_ENERGY
#define VGE_PBR_MICROFACET_ENERGY
layout(binding=32) uniform sampler2D vge_microfacetEnergy;
/** Keeps microfacet compensation distinct from finite-emitter footprint compensation. */
struct VgeLobeEnergy { vec3 compensation; vec3 reflectance; bool valid; };

/** Samples E/Ef with hardware filtering on quadratic roughness and quartic view axes. */
vec2 VgeDirectionalAlbedo(float roughness, float nDotV)
{
    vec2 coordinate = vec2(sqrt((clamp(roughness,.04,1.0)-.04)/.96),
        sqrt(sqrt(clamp(nDotV,0.0,1.0))));
    vec2 size = vec2(textureSize(vge_microfacetEnergy, 0));
    // Source endpoints occupy the first/last texel centers at every quality level.
    vec2 uv = (coordinate * (size - 1.0) + .5) / size;
    return textureLod(vge_microfacetEnergy, uv, 0.0).rg;
}

/** Evaluates the compensated directional reflectance for Schlick Fresnel with unit grazing reflectance. */
VgeLobeEnergy VgeEvaluateMicrofacetEnergy(vec3 F0, float roughness, float nDotV)
{
    if (any(isnan(F0)) || any(isinf(F0)) || isnan(roughness) || isinf(roughness)
        || isnan(nDotV) || isinf(nDotV) || any(lessThan(textureSize(vge_microfacetEnergy,0),ivec2(2))))
        return VgeLobeEnergy(vec3(0),vec3(0),false);
    vec2 energy = VgeDirectionalAlbedo(roughness,nDotV);
    if (any(isnan(energy)) || any(isinf(energy)) || energy.x<.25 || energy.x>1.000001
        || energy.y<0.0 || energy.y>energy.x)
        return VgeLobeEnergy(vec3(0),vec3(0),false);
    // The producer validates E>=.25; the denominator guard also contains corrupted/exceptional inputs.
    vec3 f0 = clamp(F0,vec3(0),vec3(1));
    float e = clamp(energy.x,.25,1.0);
    vec3 weight = vec3(1) + f0 * ((1.0-e)/e);
    vec3 reflected = weight * (e*f0 + energy.y*(vec3(1)-f0));
    return VgeLobeEnergy(weight,clamp(reflected,vec3(0),vec3(1)),true);
}
#endif
