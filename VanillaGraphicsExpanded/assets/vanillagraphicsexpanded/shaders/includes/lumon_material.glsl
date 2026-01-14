#ifndef LUMON_MATERIAL_FSH
#define LUMON_MATERIAL_FSH
// ═══════════════════════════════════════════════════════════════════════════
// LumOn Material Utility Functions
// ═══════════════════════════════════════════════════════════════════════════
// Shared functions for sampling and interpreting G-buffer material properties.
// Include this file in any LumOn shader that needs material data.
//
// G-Buffer Material Layout (gBufferMaterial - RGBA8):
//   R = Roughness (0 = smooth, 1 = rough)
//   G = Metallic (0 = dielectric, 1 = metal)
//   B = Emissive strength
//   A = Reflectivity
// ═══════════════════════════════════════════════════════════════════════════

// ═══════════════════════════════════════════════════════════════════════════
// Albedo Sampling
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Extract albedo/base color from G-buffer.
 * @param gBufferAlbedo Albedo texture sampler
 * @param texCoord      UV coordinates
 * @return RGB albedo color
 */
vec3 lumonGetAlbedo(sampler2D gBufferAlbedo, vec2 texCoord) {
    return texture(gBufferAlbedo, texCoord).rgb;
}

/**
 * Extract albedo with alpha from G-buffer.
 * @param gBufferAlbedo Albedo texture sampler
 * @param texCoord      UV coordinates
 * @return RGBA albedo color with alpha
 */
vec4 lumonGetAlbedoAlpha(sampler2D gBufferAlbedo, vec2 texCoord) {
    return texture(gBufferAlbedo, texCoord);
}

// ═══════════════════════════════════════════════════════════════════════════
// Material Property Sampling
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Extract roughness from material buffer.
 * @param gBufferMaterial Material texture sampler
 * @param texCoord        UV coordinates
 * @return Roughness value (0 = smooth/mirror, 1 = rough/diffuse)
 */
float lumonGetRoughness(sampler2D gBufferMaterial, vec2 texCoord) {
    return texture(gBufferMaterial, texCoord).r;
}

/**
 * Extract metallic value from material buffer.
 * @param gBufferMaterial Material texture sampler
 * @param texCoord        UV coordinates
 * @return Metallic value (0 = dielectric, 1 = metal)
 */
float lumonGetMetallic(sampler2D gBufferMaterial, vec2 texCoord) {
    return texture(gBufferMaterial, texCoord).g;
}

/**
 * Extract emissive strength from material buffer.
 * @param gBufferMaterial Material texture sampler
 * @param texCoord        UV coordinates
 * @return Emissive strength (0 = not emissive)
 */
float lumonGetEmissive(sampler2D gBufferMaterial, vec2 texCoord) {
    return texture(gBufferMaterial, texCoord).b;
}

/**
 * Extract reflectivity from material buffer.
 * @param gBufferMaterial Material texture sampler
 * @param texCoord        UV coordinates
 * @return Reflectivity value
 */
float lumonGetReflectivity(sampler2D gBufferMaterial, vec2 texCoord) {
    return texture(gBufferMaterial, texCoord).a;
}

/**
 * Extract all material properties at once.
 * More efficient than multiple texture() calls.
 * @param gBufferMaterial Material texture sampler
 * @param texCoord        UV coordinates
 * @param roughness       Output: roughness value
 * @param metallic        Output: metallic value
 * @param emissive        Output: emissive strength
 * @param reflectivity    Output: reflectivity
 */
void lumonGetMaterialProperties(sampler2D gBufferMaterial, vec2 texCoord,
                                out float roughness, out float metallic,
                                out float emissive, out float reflectivity) {
    vec4 mat = texture(gBufferMaterial, texCoord);
    roughness = mat.r;
    metallic = mat.g;
    emissive = mat.b;
    reflectivity = mat.a;
}

// ═══════════════════════════════════════════════════════════════════════════
// Material-Based Weights
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Compute diffuse weight based on metallic value.
 * Metals don't receive diffuse lighting (only specular reflections).
 * @param metallic Metallic value (0 = dielectric, 1 = metal)
 * @return Diffuse contribution weight (0 for metals, 1 for dielectrics)
 */
float lumonDiffuseWeight(float metallic) {
    return 1.0 - metallic;
}

/**
 * Compute specular weight based on metallic value.
 * Both metals and dielectrics receive specular, but metals are stronger.
 * @param metallic Metallic value (0 = dielectric, 1 = metal)
 * @return Specular contribution weight
 */
float lumonSpecularWeight(float metallic) {
    // Dielectrics have ~4% specular (Fresnel F0 = 0.04)
    // Metals have full specular reflection
    return mix(0.04, 1.0, metallic);
}

/**
 * Compute Fresnel F0 (reflectance at normal incidence) for a surface.
 * @param albedo   Surface albedo
 * @param metallic Metallic value
 * @return F0 for Fresnel calculations
 */
vec3 lumonComputeF0(vec3 albedo, float metallic) {
    // Dielectrics: F0 = 0.04 (4% reflectance)
    // Metals: F0 = albedo (colored reflections)
    return mix(vec3(0.04), albedo, metallic);
}

// ═══════════════════════════════════════════════════════════════════════════
// Lighting Combination Utilities
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Apply albedo modulation to indirect diffuse lighting.
 * @param indirect Raw indirect diffuse radiance
 * @param albedo   Surface albedo
 * @param metallic Metallic value (reduces diffuse for metals)
 * @return Modulated indirect diffuse contribution
 */
vec3 lumonModulateIndirectDiffuse(vec3 indirect, vec3 albedo, float metallic) {
    float diffuseWeight = lumonDiffuseWeight(metallic);
    return indirect * albedo * diffuseWeight;
}

/**
 * Combine direct and indirect lighting with material modulation.
 * @param directLight Scene with direct lighting
 * @param indirect    Raw indirect diffuse from GI
 * @param albedo      Surface albedo
 * @param metallic    Metallic value
 * @param intensity   Indirect intensity multiplier
 * @param tint        Indirect color tint
 * @return Combined final color
 */
vec3 lumonCombineLighting(vec3 directLight, vec3 indirect,
                          vec3 albedo, float metallic,
                          float intensity, vec3 tint) {
    // Modulate indirect with material properties
    vec3 indirectContrib = lumonModulateIndirectDiffuse(indirect, albedo, metallic);
    
    // Apply intensity and tint
    indirectContrib *= intensity;
    indirectContrib *= tint;
    
    // Combine: direct + indirect
    return directLight + indirectContrib;
}

#endif // LUMON_MATERIAL_FSH
