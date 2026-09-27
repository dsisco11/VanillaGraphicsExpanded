#ifndef VGE_PBR_DIRECT_BRDF_GLSL
#define VGE_PBR_DIRECT_BRDF_GLSL
/** Shared direct-light BRDF for deferred and forward receivers. */
void addDirectLight(
    vec3 baseColor,
    vec3 N,
    vec3 V,
    vec3 L,
    vec3 lightRgb,
    float roughness,
    float metallic,
    float reflectivity,
    inout vec3 accumDiffuse,
    inout vec3 accumSpecular)
{
    float NdotL = max(dot(N, L), 0.0);
    if (NdotL <= 0.0) return;

    vec3 dielectricF0 = vec3(0.04) * clamp(reflectivity, 0.0, 1.0);
    vec3 F0 = mix(dielectricF0, baseColor, clamp(metallic, 0.0, 1.0));

    vec3 H = normalize(V + L);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

    vec3 kD = pbrDiffuseFactorFromFresnel(F, metallic);
    vec3 kS = pbrSpecularFactorFromFresnel(F);

    // NOTE: VS's lighting inputs here are not calibrated as physical radiance.
    // Using the normalized Lambert term (albedo / PI) makes this pass look far too dark
    // compared to the game's legacy lighting model. Treat the inputs as already-integrated
    // irradiance and do not apply 1/PI.
    vec3 diffuseBrdf = kD * baseColor;
    vec3 specularBrdf = cookTorranceBRDF(N, V, L, F0, roughness);

    // Radiance split
    accumDiffuse += diffuseBrdf * lightRgb * NdotL;
    accumSpecular += (kS * specularBrdf) * lightRgb * NdotL;
}

#endif
