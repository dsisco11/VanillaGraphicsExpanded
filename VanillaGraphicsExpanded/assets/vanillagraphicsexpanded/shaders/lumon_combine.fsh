#version 330 core

in vec2 uv;

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
@import "lumon_common.fsh"

// Import material utilities
@import "lumon_material.fsh"

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
uniform sampler2D primaryDepth;       // Depth for sky detection

// ============================================================================
// Uniforms
// ============================================================================

// Intensity and color adjustment
uniform float indirectIntensity;      // Global multiplier (default: 1.0)
uniform vec3 indirectTint;            // RGB tint applied to indirect light

// Feature toggle
uniform int lumOnEnabled;             // 0 = disabled, 1 = enabled

// ============================================================================
// Main
// ============================================================================

void main(void)
{
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
    float metallic = lumonGetMetallic(gBufferMaterial, uv);
    
    // Combine lighting with material modulation
    vec3 finalColor = lumonCombineLighting(directLight, indirect, albedo, metallic,
                                           indirectIntensity, indirectTint);
    
    // Clamp to prevent negative values (shouldn't happen, but safety)
    finalColor = max(finalColor, vec3(0.0));
    
    outColor = vec4(finalColor, 1.0);
}
