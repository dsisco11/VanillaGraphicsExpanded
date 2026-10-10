#ifndef VGE_PBR_ENVIRONMENT_GLSL
#define VGE_PBR_ENVIRONMENT_GLSL
@import "./pbr_common.glsl"

/** Bounded local approximation: engine sky propagation supplies enclosure visibility, not direct shadow visibility. */
vec3 VgeLocalEnvironment(vec3 blockLight, vec3 exposedSky)
{
    return max(blockLight, vec3(0.0)) + max(exposedSky, vec3(0.0)) * 0.35;
}

/** Splits integrated environment response into diffuse and roughness-attenuated specular before visibility is applied. */
void VgeEnvironmentSplit(vec3 irradiance, vec3 albedo, float metallic, float roughness, float nDotV,
    out vec3 diffuse, out vec3 specular)
{
    metallic=clamp(metallic,0.0,1.0);
    vec3 fresnel=fresnelSchlick(clamp(nDotV,0.0,1.0),mix(vec3(0.04),albedo,metallic));
    vec3 incoming=max(irradiance,vec3(0.0));
    diffuse=incoming*pbrDiffuseFactorFromFresnel(fresnel,metallic)*albedo;
    specular=incoming*fresnel*(1.0-clamp(roughness,0.0,1.0));
}
/** Returns the unoccluded environment response for forward receivers without a matched screen-space AO publication. */
vec3 VgeEnvironmentResponse(vec3 irradiance, vec3 albedo, float metallic, float roughness, float nDotV)
{
    vec3 diffuse,specular;
    VgeEnvironmentSplit(irradiance,albedo,metallic,roughness,nDotV,diffuse,specular);
    return diffuse+specular;
}
#endif
