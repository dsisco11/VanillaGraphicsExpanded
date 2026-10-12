#ifndef VGE_PBR_DIRECT_BRDF_GLSL
#define VGE_PBR_DIRECT_BRDF_GLSL
@import "./pbr_transmission.glsl"
@import "./pbr_finite_disk_brdf.glsl"
@import "./pbr_photometric.glsl"
/** Computes the shared metallic material response using the emitter's center direction. */
vec3 VgeDirectDiffuseBRDF(vec3 baseColor, vec3 V, vec3 L, float metallic, out vec3 F0)
{
    // Material alpha is not an independent dielectric reflectance control.
    metallic = clamp(metallic, 0.0, 1.0);
    F0 = mix(vec3(0.04), baseColor, metallic);
    vec3 H = normalize(V + L);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);
    // Every direct source supplies incident lux before orientation; diffuse response is per steradian.
    return pbrDiffuseFactorFromFresnel(F, metallic) * baseColor / VGE_PHOTOMETRIC_PI;
}

/** Accumulates a punctual source using the shared Cook-Torrance material BRDF. */
void addDirectLight(
    vec3 baseColor,
    vec3 N,
    vec3 V,
    vec3 L,
    vec3 lightRgb,
    float roughness,
    float metallic,
    inout vec3 accumDiffuse,
    inout vec3 accumSpecular)
{
    float NdotL = max(dot(N, L), 0.0);
    if (NdotL <= 0.0) return;

    vec3 F0;
    vec3 diffuseBrdf = VgeDirectDiffuseBRDF(baseColor, V, L, metallic, F0);
    vec3 specularBrdf = cookTorranceBRDF(N, V, L, F0, roughness);

    // Radiance split
    accumDiffuse += diffuseBrdf * lightRgb * NdotL;
    // Cook-Torrance already includes Fresnel; applying it again would square its attenuation.
    accumSpecular += specularBrdf * lightRgb * NdotL;
}

/** Accumulates a circular emitter's finite-disk response; radius controls its angular extent. */
void addFiniteDiskDirectLight(
    vec3 baseColor,
    vec3 N,
    vec3 V,
    vec3 L,
    vec3 lightRgb,
    float roughness,
    float metallic,
    float angularRadius,
    inout vec3 accumDiffuse,
    inout vec3 accumSpecular)
{
    float NdotL = max(dot(N, L), 0.0);
    if (NdotL <= 0.0) return;

    // Keep diffuse centered on the emitter while its specular response accounts for disk extent.
    vec3 F0;
    vec3 diffuseBrdf = VgeDirectDiffuseBRDF(baseColor, V, L, metallic, F0);
    vec3 specularBrdf = VgeFiniteDiskSpecularBRDF(N, V, L, F0, roughness, angularRadius);
    accumDiffuse += diffuseBrdf * lightRgb * NdotL;
    // The finite-disk response includes Fresnel, just as the punctual material BRDF does.
    accumSpecular += specularBrdf * lightRgb * NdotL;
}

#endif
