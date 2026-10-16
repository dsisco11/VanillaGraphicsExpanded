#ifndef VGE_PBR_ENVIRONMENT_GLSL
#define VGE_PBR_ENVIRONMENT_GLSL
@import "./pbr_common.glsl"
@import "./pbr_microfacet_energy.glsl"

/** Combines calibrated block and scattered-sky E/pi; availability is supplied by the publisher. */
vec3 VgeLocalEnvironment(vec3 blockLight, vec3 exposedSky)
{
    return max(blockLight, vec3(0.0)) + max(exposedSky, vec3(0.0));
}

/** Evaluates a directionless incident E/pi approximation using compensated material reflectance before visibility. */
void VgeEnvironmentSplit(vec3 irradiance, vec3 albedo, float metallic, float roughness, float nDotV,
    out vec3 diffuse, out vec3 specular)
{
    metallic=clamp(metallic,0.0,1.0);
    VgeLobeEnergy energy=VgeEvaluateMicrofacetEnergy(mix(vec3(0.04),albedo,metallic),roughness,nDotV);
    if(!energy.valid) { diffuse=vec3(0); specular=vec3(0); return; }
    vec3 incoming=max(irradiance,vec3(0.0));
    diffuse=incoming*(vec3(1)-energy.reflectance)*(1.0-metallic)*albedo;
    specular=incoming*energy.reflectance;
}
/** Returns the unoccluded environment response for forward receivers without a matched screen-space AO publication. */
vec3 VgeEnvironmentResponse(vec3 irradiance, vec3 albedo, float metallic, float roughness, float nDotV)
{
    vec3 diffuse,specular;
    VgeEnvironmentSplit(irradiance,albedo,metallic,roughness,nDotV,diffuse,specular);
    return diffuse+specular;
}
#endif
