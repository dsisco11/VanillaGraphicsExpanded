#version 330 core

out vec4 outColor;

// ============================================================================
// LumOn Combine/Integrate Pass (SPG-009)
// 
// Combines indirect diffuse lighting from LumOn with the scene's direct lighting.
// Applies proper material modulation (albedo for diffuse, metallic rejection).
//
// This pass implements the final lighting integration from spec Section 4.
// ============================================================================

// Import common utilities
@import "./includes/lumon_common.glsl"

// Import material utilities
// Import PBR/material utilities
@import "./includes/lumon_pbr.glsl"

// ============================================================================
// Textures
// ============================================================================

// Direct lighting scene (captured before GI application)
uniform sampler2D sceneDirect;

// LumOn indirect diffuse output (upsampled to full resolution)
uniform sampler2D indirectDiffuse;

// G-Buffer for material properties
uniform sampler2D gBufferAlbedo;      // Surface albedo/color
uniform sampler2D gBufferMaterial;    // Material properties (roughness, metallic, etc.)
uniform sampler2D gBufferNormal;      // World-space normals
uniform sampler2D primaryDepth;       // Depth for sky detection

// ============================================================================
// Uniforms
// ============================================================================

// Intensity and color adjustment
uniform float indirectIntensity;      // Global multiplier (default: 1.0)
uniform vec3 indirectTint;            // RGB tint applied to indirect light

// Feature toggle
uniform int lumOnEnabled;             // 0 = disabled, 1 = enabled

// Phase 15: Physically-plausible compositing (optional)
uniform int enablePbrComposite;
uniform int enableAO;
uniform int enableBentNormal;
uniform float diffuseAOStrength;
uniform float specularAOStrength;

// Matrices for view vector reconstruction / normal transform
uniform mat4 invProjectionMatrix;
uniform mat4 viewMatrix;

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(sceneDirect, 0));

    // Sample direct lighting (the scene before GI)
    vec3 directLight = texture(sceneDirect, uv).rgb;
    
    // If LumOn is disabled, pass through direct lighting unchanged
    if (lumOnEnabled == 0) {
        outColor = vec4(directLight, 1.0);
        return;
    }
    
    // Check for sky - no indirect lighting contribution
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) {
        outColor = vec4(directLight, 1.0);
        return;
    }
    
    // Sample LumOn indirect diffuse
    vec3 indirect = texture(indirectDiffuse, uv).rgb;
    
    // Sample material properties using shared utilities
    vec3 albedo = lumonGetAlbedo(gBufferAlbedo, uv);
    float roughness;
    float metallic;
    float emissive;
    float reflectivity;
    lumonGetMaterialProperties(gBufferMaterial, uv, roughness, metallic, emissive, reflectivity);

    // Apply user intensity + tint to the incoming indirect radiance
    indirect *= indirectIntensity;
    indirect *= indirectTint;
    
    vec3 finalColor;

    if (enablePbrComposite == 0)
    {
        // Legacy path: diffuse-only indirect (kept for backwards compatibility)
        finalColor = lumonCombineLighting(directLight, indirect, albedo, metallic,
                                          1.0, vec3(1.0));
    }
    else
    {
        // Reconstruct view direction in view-space
        vec3 viewPosVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
        vec3 viewDirVS = normalize(-viewPosVS);

        // Normal comes in as world-space; convert to view-space for consistent dot products
        vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);
        vec3 normalVS = normalize((viewMatrix * vec4(normalWS, 0.0)).xyz);

        float ao = enableAO == 1 ? reflectivity : 1.0;

        // Bent normal: when enabled, apply a cheap approximation that bends the
        // normal toward +Y (view-up) as AO decreases. This provides a usable
        // visibility-ish signal without requiring a dedicated bent-normal buffer.
        vec3 bentNormalVS = normalVS;
        if (enableBentNormal == 1)
        {
            float bend = clamp((1.0 - clamp(ao, 0.0, 1.0)) * 0.5, 0.0, 0.5);
            bentNormalVS = normalize(mix(normalVS, vec3(0.0, 1.0, 0.0), bend));
        }

        vec3 indirectDiffuseContrib;
        vec3 indirectSpecularContrib;

        lumonComputeIndirectSplit(
            indirect,
            albedo,
            bentNormalVS,
            viewDirVS,
            roughness,
            metallic,
            ao,
            diffuseAOStrength,
            specularAOStrength,
            indirectDiffuseContrib,
            indirectSpecularContrib);

        finalColor = directLight + indirectDiffuseContrib + indirectSpecularContrib;
    }
    
    // Clamp to prevent negative values (shouldn't happen, but safety)
    finalColor = max(finalColor, vec3(0.0));
    
    outColor = vec4(finalColor, 1.0);
}
