#ifndef VGE_PBR_DIRECT_BRDF_GLSL
#define VGE_PBR_DIRECT_BRDF_GLSL
@import "./pbr_foliage_transmission.glsl"
/** Shared direct-light BRDF for deferred and forward receivers. */
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

    // The metallic workflow retains dielectric reflection for nonmetals.
    // Material alpha is not an independent dielectric reflectance control.
    metallic = clamp(metallic, 0.0, 1.0);
    vec3 F0 = mix(vec3(0.04), baseColor, metallic);

    vec3 H = normalize(V + L);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

    vec3 kD = pbrDiffuseFactorFromFresnel(F, metallic);

    // NOTE: VS's lighting inputs here are not calibrated as physical radiance.
    // Using the normalized Lambert term (albedo / PI) makes this pass look far too dark
    // compared to the game's legacy lighting model. Treat the inputs as already-integrated
    // irradiance and do not apply 1/PI.
    // Callers normalize solar diffuse by pi; engine point lights retain their existing calibration.
    vec3 diffuseBrdf = kD * baseColor;
    vec3 specularBrdf = cookTorranceBRDF(N, V, L, F0, roughness);

    // Radiance split
    accumDiffuse += diffuseBrdf * lightRgb * NdotL;
    // Cook-Torrance already includes Fresnel; applying it again would square its attenuation.
    accumSpecular += specularBrdf * lightRgb * NdotL;
}

#endif
