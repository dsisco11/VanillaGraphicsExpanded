#ifndef VGE_PBRFUNCTIONS_FSH
#define VGE_PBRFUNCTIONS_FSH

@import "./common_constants.glsl"
/*
@description Fresnel-Schlick approximation for reflectance
@param cosTheta - Cosine of the angle between view and halfway vector
@param F0 - Base reflectivity at normal incidence
@return Approximated reflectance
*/
vec3 fresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

/*
@description Energy-conserving diffuse weight derived from Fresnel term.
             Useful for splitting an incoming (already-integrated) lighting term
             into diffuse/specular contributions.
@param F - Fresnel term (kS)
@param metallic - Metallic factor in [0..1]
@return Diffuse factor kD
*/
vec3 pbrDiffuseFactorFromFresnel(vec3 F, float metallic)
{
    vec3 kS = clamp(F, 0.0, 1.0);
    vec3 kD = (vec3(1.0) - kS) * (1.0 - clamp(metallic, 0.0, 1.0));
    return max(kD, vec3(0.0));
}

/*
@description Specular weight derived from Fresnel term.
@param F - Fresnel term
@return Specular factor kS
*/
vec3 pbrSpecularFactorFromFresnel(vec3 F)
{
    return clamp(F, 0.0, 1.0);
}

/*
@description GGX normal distribution function
@param N - Normal vector
@param H - Halfway vector
@param roughness - Surface roughness
@return Normal distribution value
*/
float distributionGGX(vec3 N, vec3 H, float roughness) {
    float a = clamp(roughness, 0.04, 1.0);
    a *= a;
    float a2 = a * a;
    float NdotH = clamp(dot(N, H), 0.0, 1.0);
    float NdotH2 = NdotH * NdotH;
    
    float num = a2;
    // Preserve the narrow mirror peak without subtracting nearly equal numbers.
    if (NdotH == 1.0) return 1.0 / (VGE_PI * a2);
    float denom = (1.0 - NdotH2) + a2 * NdotH2;
    denom = VGE_PI * denom * denom;
    
    return num / denom;
}

/** Evaluates height-correlated Smith GGX visibility, with the projected-area factors canceled analytically. */
float VgeSmithGgxVisibility(float NdotV, float NdotL, float roughness)
{
    if (NdotV <= 0.0 || NdotL <= 0.0) return 0.0;
    float r = clamp(roughness, 0.04, 1.0);
    float a2 = r * r * r * r;
    float viewLength = sqrt(a2 + (1.0 - a2) * NdotV * NdotV);
    float lightLength = sqrt(a2 + (1.0 - a2) * NdotL * NdotL);
    // Both terms are positive in the admitted hemisphere. An additive epsilon would alter grazing energy.
    return 0.5 / (NdotL * viewLength + NdotV * lightLength);
}

/*
@description Smith geometry function
@param N - Normal vector
@param V - View vector
@param L - Light vector
@param roughness - Surface roughness
@return Geometry attenuation
*/
float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    return 4.0 * NdotV * NdotL * VgeSmithGgxVisibility(NdotV, NdotL, roughness);
}

/*
@description Cook-Torrance BRDF model
@param N - Normal vector
@param V - View vector
@param L - Light vector
@param F0 - Base reflectivity at normal incidence
@param roughness - Surface roughness
@return Specular reflection component
*/
vec3 cookTorranceBRDF(vec3 N, vec3 V, vec3 L, vec3 F0, float roughness) {
    float nv = max(dot(N, V), 0.0), nl = max(dot(N, L), 0.0);
    vec3 halfVector = V + L;
    if (nv <= 0.0 || nl <= 0.0 || dot(halfVector, halfVector) <= 0.0) return vec3(0.0);
    vec3 H = normalize(halfVector);
    
    float NDF = distributionGGX(N, H, roughness);
    float visibility = VgeSmithGgxVisibility(nv, nl, roughness);
    vec3 F    = fresnelSchlick(max(dot(H, V), 0.0), F0);
    
    return NDF * F * visibility;
}
#endif // VGE_PBRFUNCTIONS_FSH
