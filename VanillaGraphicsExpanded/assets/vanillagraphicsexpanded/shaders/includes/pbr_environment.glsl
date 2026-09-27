#ifndef VGE_PBR_ENVIRONMENT_GLSL
#define VGE_PBR_ENVIRONMENT_GLSL
@import "./pbr_common.glsl"

/** Bounded local approximation: engine sky propagation supplies enclosure visibility, not direct shadow visibility. */
vec3 VgeLocalEnvironment(vec3 blockLight, vec3 exposedSky)
{
    return max(blockLight, vec3(0.0)) + max(exposedSky, vec3(0.0)) * 0.35;
}

/** Integrated diffuse and roughness-attenuated specular response without a fabricated radiance floor. */
vec3 VgeEnvironmentResponse(vec3 irradiance, vec3 albedo, float metallic, float roughness, float nDotV)
{
    metallic = clamp(metallic, 0.0, 1.0);
    vec3 fresnel = fresnelSchlick(clamp(nDotV, 0.0, 1.0), mix(vec3(0.04), albedo, metallic));
    return max(irradiance, vec3(0.0)) *
        (pbrDiffuseFactorFromFresnel(fresnel, metallic) * albedo + fresnel * (1.0 - clamp(roughness, 0.0, 1.0)));
}
#endif
