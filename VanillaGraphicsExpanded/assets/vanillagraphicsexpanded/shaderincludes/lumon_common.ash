#ifndef LUMON_COMMON_ASH
#define LUMON_COMMON_ASH
// ═══════════════════════════════════════════════════════════════════════════
// LumOn Common Utility Functions
// ═══════════════════════════════════════════════════════════════════════════
// Shared functions used across all LumOn shader passes.
// Include this file in any LumOn shader that needs depth/position utilities.

// ═══════════════════════════════════════════════════════════════════════════
// Constants
// ═══════════════════════════════════════════════════════════════════════════

const float LUMON_PI = 3.141592653589793;
const float LUMON_TAU = 6.283185307179586;
const float LUMON_GOLDEN_ANGLE = 2.399963229728653;
const float LUMON_PHI = 1.618033988749895;

// Sky depth threshold (values >= this are considered sky)
const float LUMON_SKY_DEPTH_THRESHOLD = 0.9999;

// ═══════════════════════════════════════════════════════════════════════════
// Depth Utilities
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Linearize depth from depth buffer (non-linear to linear view-space Z).
/// </summary>
/// <param name="depth">Raw depth buffer value [0, 1]</param>
/// <param name="zNear">Near clipping plane distance</param>
/// <param name="zFar">Far clipping plane distance</param>
/// <returns>Linear view-space depth</returns>
float lumonLinearizeDepth(float depth, float zNear, float zFar) {
    float z = depth * 2.0 - 1.0;
    return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
}

/// <summary>
/// Check if a depth value represents sky (far plane).
/// </summary>
/// <param name="depth">Raw depth buffer value [0, 1]</param>
/// <returns>True if depth is at or beyond sky threshold</returns>
bool lumonIsSky(float depth) {
    return depth >= LUMON_SKY_DEPTH_THRESHOLD;
}

// ═══════════════════════════════════════════════════════════════════════════
// Position Reconstruction
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Reconstruct view-space position from UV and depth.
/// </summary>
/// <param name="texCoord">Screen-space UV coordinates [0, 1]</param>
/// <param name="depth">Raw depth buffer value [0, 1]</param>
/// <param name="invProjectionMatrix">Inverse projection matrix</param>
/// <returns>View-space position</returns>
vec3 lumonReconstructViewPos(vec2 texCoord, float depth, mat4 invProjectionMatrix) {
    vec4 ndc = vec4(texCoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = invProjectionMatrix * ndc;
    return viewPos.xyz / viewPos.w;
}

/// <summary>
/// Project view-space position to screen UV coordinates.
/// </summary>
/// <param name="viewPos">View-space position</param>
/// <param name="projectionMatrix">Projection matrix</param>
/// <returns>Screen-space UV coordinates [0, 1]</returns>
vec2 lumonProjectToScreen(vec3 viewPos, mat4 projectionMatrix) {
    vec4 clipPos = projectionMatrix * vec4(viewPos, 1.0);
    return (clipPos.xy / clipPos.w) * 0.5 + 0.5;
}

// ═══════════════════════════════════════════════════════════════════════════
// Normal Utilities
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Decode normal from G-buffer format [0,1] to [-1,1].
/// </summary>
/// <param name="encoded">G-buffer encoded normal [0, 1]</param>
/// <returns>Decoded and normalized normal [-1, 1]</returns>
vec3 lumonDecodeNormal(vec3 encoded) {
    return normalize(encoded * 2.0 - 1.0);
}

/// <summary>
/// Encode normal to G-buffer format [-1,1] to [0,1].
/// </summary>
/// <param name="normal">World/view-space normal [-1, 1]</param>
/// <returns>Encoded normal [0, 1]</returns>
vec3 lumonEncodeNormal(vec3 normal) {
    return normal * 0.5 + 0.5;
}

// ═══════════════════════════════════════════════════════════════════════════
// Sampling Utilities
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Generate cosine-weighted hemisphere direction for importance sampling.
/// </summary>
/// <param name="u">Random values in [0, 1]</param>
/// <param name="normal">Surface normal to orient hemisphere around</param>
/// <returns>Cosine-weighted sample direction</returns>
vec3 lumonCosineSampleHemisphere(vec2 u, vec3 normal) {
    // Generate uniform disk sample
    float r = sqrt(u.x);
    float theta = LUMON_TAU * u.y;
    float x = r * cos(theta);
    float y = r * sin(theta);
    float z = sqrt(max(0.0, 1.0 - u.x));
    
    // Build tangent frame
    vec3 tangent = abs(normal.y) < 0.999 
        ? normalize(cross(vec3(0.0, 1.0, 0.0), normal))
        : normalize(cross(vec3(1.0, 0.0, 0.0), normal));
    vec3 bitangent = cross(normal, tangent);
    
    // Transform to world/view space
    return normalize(tangent * x + bitangent * y + normal * z);
}

// ═══════════════════════════════════════════════════════════════════════════
// Sky/Environment Utilities
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Get sky color for a ray direction (simple gradient + sun).
/// </summary>
/// <param name="rayDir">Ray direction (normalized)</param>
/// <param name="sunPosition">Sun direction (normalized)</param>
/// <param name="sunColor">Sun color and intensity</param>
/// <param name="ambientColor">Ambient/sky base color</param>
/// <param name="skyMissWeight">Weight multiplier for sky contribution</param>
/// <returns>Sky radiance for the given direction</returns>
vec3 lumonGetSkyColor(vec3 rayDir, vec3 sunPosition, vec3 sunColor, vec3 ambientColor, float skyMissWeight) {
    float skyFactor = max(0.0, rayDir.y) * 0.5 + 0.5;
    vec3 skyColor = ambientColor * skyFactor;
    
    // Add sun contribution
    float sunDot = max(0.0, dot(rayDir, normalize(sunPosition)));
    skyColor += sunColor * pow(sunDot, 32.0) * 0.5;
    
    return skyColor * skyMissWeight;
}

// ═══════════════════════════════════════════════════════════════════════════
// Probe Grid Utilities
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Convert probe grid coordinates to texture UV.
/// </summary>
/// <param name="probeCoord">Integer probe grid coordinates</param>
/// <param name="probeGridSize">Total probe grid dimensions</param>
/// <returns>UV coordinates for sampling probe textures</returns>
vec2 lumonProbeCoordToUV(ivec2 probeCoord, vec2 probeGridSize) {
    return (vec2(probeCoord) + 0.5) / probeGridSize;
}

/// <summary>
/// Convert screen position to probe grid position (fractional).
/// </summary>
/// <param name="screenPos">Screen position in pixels</param>
/// <param name="probeSpacing">Pixels between probes</param>
/// <returns>Fractional probe grid position</returns>
vec2 lumonScreenToProbePos(vec2 screenPos, float probeSpacing) {
    return screenPos / probeSpacing;
}

/// <summary>
/// Get the screen UV that a probe samples (center of its cell).
/// </summary>
/// <param name="probeCoord">Integer probe grid coordinates</param>
/// <param name="probeSpacing">Pixels between probes</param>
/// <param name="screenSize">Screen dimensions in pixels</param>
/// <returns>Screen UV coordinates [0, 1]</returns>
vec2 lumonProbeToScreenUV(ivec2 probeCoord, float probeSpacing, vec2 screenSize) {
    vec2 screenPos = (vec2(probeCoord) + 0.5) * probeSpacing;
    return screenPos / screenSize;
}
#endif // LUMON_COMMON_ASH