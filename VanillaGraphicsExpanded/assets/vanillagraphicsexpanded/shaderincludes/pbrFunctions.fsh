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
@description GGX normal distribution function
@param N - Normal vector
@param H - Halfway vector
@param roughness - Surface roughness
@return Normal distribution value
*/
float distributionGGX(vec3 N, vec3 H, float roughness) {
    const float PI = 3.141592653589793;
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    
    float num = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;
    
    return num / denom;
}

/*
@description Schlick-GGX geometry function
@param NdotV - Dot product of normal and view
@param roughness - Surface roughness
@return Geometry attenuation
*/
float geometrySchlickGGX(float NdotV, float roughness) {
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    
    float num = NdotV;
    float denom = NdotV * (1.0 - k) + k;
    
    return num / denom;
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
    float ggx2 = geometrySchlickGGX(NdotV, roughness);
    float ggx1 = geometrySchlickGGX(NdotL, roughness);
    
    return ggx1 * ggx2;
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
    vec3 H = normalize(V + L);
    
    float NDF = distributionGGX(N, H, roughness);
    float G   = geometrySmith(N, V, L, roughness);
    vec3 F    = fresnelSchlick(max(dot(H, V), 0.0), F0);
    
    vec3 numerator    = NDF * G * F;
    float denominator = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.000001; // Prevent divide by zero
    vec3 specular     = numerator / denominator;
    
    return specular;
}
