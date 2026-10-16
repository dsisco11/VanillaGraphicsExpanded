#ifndef VGE_PBR_DIRECT_BRDF_GLSL
#define VGE_PBR_DIRECT_BRDF_GLSL
@import "./pbr_transmission.glsl"
@import "./pbr_finite_disk_brdf.glsl"
@import "./pbr_photometric.glsl"
@import "./pbr_microfacet_energy.glsl"
/** Computes the shared metallic material response using the emitter's center direction. */
vec3 VgeDirectDiffuseBRDF(vec3 baseColor, vec3 N, vec3 V, float metallic, float roughness,
    out vec3 F0, out VgeLobeEnergy energy)
{
    // Material alpha is not an independent dielectric reflectance control.
    metallic = clamp(metallic, 0.0, 1.0);
    baseColor = clamp(baseColor,vec3(0),vec3(1));
    F0 = mix(vec3(0.04), baseColor, metallic);
    energy = VgeEvaluateMicrofacetEnergy(F0,roughness,max(dot(N,V),0.0));
    // Every direct source supplies incident lux before orientation; diffuse response is per steradian.
    return energy.valid ? (vec3(1)-energy.reflectance)*(1.0-metallic)*baseColor/VGE_PHOTOMETRIC_PI : vec3(0);
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
    VgeLobeEnergy energy;
    vec3 diffuseBrdf = VgeDirectDiffuseBRDF(baseColor,N,V,metallic,roughness,F0,energy);
    if(!energy.valid) return;
    vec3 specularBrdf = cookTorranceBRDF(N, V, L, F0, roughness)*energy.compensation;

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
    VgeLobeEnergy energy;
    vec3 diffuseBrdf = VgeDirectDiffuseBRDF(baseColor,N,V,metallic,roughness,F0,energy);
    if(!energy.valid) return;
    vec3 specularBrdf = VgeFiniteDiskSpecularBRDF(N,V,L,F0,roughness,angularRadius)*energy.compensation;
    accumDiffuse += diffuseBrdf * lightRgb * NdotL;
    // The finite-disk response includes Fresnel, just as the punctual material BRDF does.
    accumSpecular += specularBrdf * lightRgb * NdotL;
}

#endif
