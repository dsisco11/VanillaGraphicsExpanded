#ifndef VGE_MATERIAL_GLSL
#define VGE_MATERIAL_GLSL

// Requires: `uniform sampler2D vge_materialParamsTex;`
// Encoding: RGB16F = (roughness, metallic, emissive)

vec3 ReadMaterialParams(vec2 uv)
{
    // Same UVs as terrainTex sampling.
    return texture(vge_materialParamsTex, uv).rgb;
}

// Placeholder until we plumb noise deltas + deterministic seed inputs.
// This preserves a stable ABI for patched shaders.
vec3 ApplyMaterialNoise(vec3 params, vec2 uv)
{
    return params;
}

vec3 ApplyMaterialNoise(vec3 params, vec2 uv, int renderFlags)
{
    return params;
}

float ComputeReflectivity(float roughness, float metallic)
{
    // v1: simple relationship. Can be replaced with a more physically-based curve later.
    return metallic;
}

#endif // VGE_MATERIAL_GLSL
