#ifndef LUMON_COMMON_ASH
#define LUMON_COMMON_ASH
// ═══════════════════════════════════════════════════════════════════════════
// LumOn Common Utility Functions
// ═══════════════════════════════════════════════════════════════════════════
// Shared functions used across all LumOn shader passes.
// Include this file in any LumOn shader that needs depth/position utilities.

// #region Constants

const float LUMON_PI = 3.141592653589793;
const float LUMON_TAU = 6.283185307179586;
const float LUMON_GOLDEN_ANGLE = 2.399963229728653;
const float LUMON_PHI = 1.618033988749895;

// Sky depth threshold (values >= this are considered sky)
const float LUMON_SKY_DEPTH_THRESHOLD = 0.9999;

// #endregion

// #region Depth Utilities

/**
 * Linearize depth from depth buffer (non-linear to linear view-space Z).
 * @param depth Raw depth buffer value [0, 1]
 * @param zNear Near clipping plane distance
 * @param zFar Far clipping plane distance
 * @return Linear view-space depth
 */
float lumonLinearizeDepth(float depth, float zNear, float zFar) {
    float z = depth * 2.0 - 1.0;
    return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
}

/**
 * Check if a depth value represents sky (far plane).
 * @param depth Raw depth buffer value [0, 1]
 * @return True if depth is at or beyond sky threshold
 */
bool lumonIsSky(float depth) {
    return depth >= LUMON_SKY_DEPTH_THRESHOLD;
}

// #endregion

// #region Position Reconstruction

/**
 * Reconstruct view-space position from UV and depth.
 * @param texCoord Screen-space UV coordinates [0, 1]
 * @param depth Raw depth buffer value [0, 1]
 * @param invProjectionMatrix Inverse projection matrix
 * @return View-space position
 */
vec3 lumonReconstructViewPos(vec2 texCoord, float depth, mat4 invProjectionMatrix) {
    vec4 ndc = vec4(texCoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = invProjectionMatrix * ndc;
    return viewPos.xyz / viewPos.w;
}

/**
 * Project view-space position to screen UV coordinates.
 * @param viewPos View-space position
 * @param projectionMatrix Projection matrix
 * @return Screen-space UV coordinates [0, 1]
 */
vec2 lumonProjectToScreen(vec3 viewPos, mat4 projectionMatrix) {
    vec4 clipPos = projectionMatrix * vec4(viewPos, 1.0);
    return (clipPos.xy / clipPos.w) * 0.5 + 0.5;
}

// #endregion

// #region Normal Utilities

/**
 * Decode normal from G-buffer format [0,1] to [-1,1].
 * @param encoded G-buffer encoded normal [0, 1]
 * @return Decoded and normalized normal [-1, 1]
 */
vec3 lumonDecodeNormal(vec3 encoded) {
    return normalize(encoded * 2.0 - 1.0);
}

/**
 * Encode normal to G-buffer format [-1,1] to [0,1].
 * @param normal World/view-space normal [-1, 1]
 * @return Encoded normal [0, 1]
 */
vec3 lumonEncodeNormal(vec3 normal) {
    return normal * 0.5 + 0.5;
}

// #endregion

// #region Sampling Utilities

/**
 * Generate cosine-weighted hemisphere direction for importance sampling.
 * @param u Random values in [0, 1]
 * @param normal Surface normal to orient hemisphere around
 * @return Cosine-weighted sample direction
 */
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

// #endregion

// #region Guide Sampling Utilities

/**
 * Select the closest (minimum depth) full-res sample within the 2x2 block that
 * corresponds to a half-res pixel.
 *
 * This is used to avoid bilinear depth/normal sampling artifacts at silhouettes
 * when shading at half resolution.
 */
bool lumonSelectGuidesForHalfResCoord(
    ivec2 halfCoord,
    sampler2D depthTex,
    sampler2D normalTex,
    ivec2 screenSizeI,
    out ivec2 outBestFullCoord,
    out float outDepthRaw,
    out vec3 outNormal)
{
    ivec2 baseFull = halfCoord * 2;
    ivec2 maxFull = screenSizeI - 1;

    bool found = false;
    float bestDepthRaw = 1.0;
    ivec2 bestFull = clamp(baseFull, ivec2(0), maxFull);

    for (int dy = 0; dy <= 1; dy++)
    {
        for (int dx = 0; dx <= 1; dx++)
        {
            ivec2 fullCoord = clamp(baseFull + ivec2(dx, dy), ivec2(0), maxFull);
            float d = texelFetch(depthTex, fullCoord, 0).r;
            if (lumonIsSky(d)) continue;

            if (!found || d < bestDepthRaw)
            {
                found = true;
                bestDepthRaw = d;
                bestFull = fullCoord;
            }
        }
    }

    if (!found)
    {
        outBestFullCoord = clamp(baseFull, ivec2(0), maxFull);
        outDepthRaw = 1.0;
        outNormal = vec3(0.0, 0.0, 1.0);
        return false;
    }

    outBestFullCoord = bestFull;
    outDepthRaw = bestDepthRaw;
    outNormal = lumonDecodeNormal(texelFetch(normalTex, bestFull, 0).xyz);
    return true;
}

// #endregion

// #region Sky/Environment Utilities

/**
 * Get sky color for a ray direction (simple gradient + sun).
 * @param rayDir Ray direction (normalized)
 * @param sunPosition Sun direction (normalized)
 * @param sunColor Sun color and intensity
 * @param ambientColor Ambient/sky base color
 * @param skyMissWeight Weight multiplier for sky contribution
 * @return Sky radiance for the given direction
 */
vec3 lumonGetSkyColor(vec3 rayDir, vec3 sunPosition, vec3 sunColor, vec3 ambientColor, float skyMissWeight) {
    float skyFactor = max(0.0, rayDir.y) * 0.5 + 0.5;
    vec3 skyColor = ambientColor * skyFactor;
    
    // Add sun contribution
    float sunDot = max(0.0, dot(rayDir, normalize(sunPosition)));
    skyColor += sunColor * pow(sunDot, 32.0) * 0.5;
    
    return skyColor * skyMissWeight;
}

// #endregion

// #region Probe Grid Utilities

/**
 * Convert probe grid coordinates to texture UV.
 * @param probeCoord Integer probe grid coordinates
 * @param probeGridSize Total probe grid dimensions
 * @return UV coordinates for sampling probe textures
 */
vec2 lumonProbeCoordToUV(ivec2 probeCoord, vec2 probeGridSize) {
    return (vec2(probeCoord) + 0.5) / probeGridSize;
}

/**
 * Convert screen position to probe grid position (fractional).
 * @param screenPos Screen position in pixels
 * @param probeSpacing Pixels between probes
 * @return Fractional probe grid position
 */
vec2 lumonScreenToProbePos(vec2 screenPos, float probeSpacing) {
    return screenPos / probeSpacing;
}

/**
 * Get the screen UV that a probe samples (center of its cell).
 * @param probeCoord Integer probe grid coordinates
 * @param probeSpacing Pixels between probes
 * @param screenSize Screen dimensions in pixels
 * @return Screen UV coordinates [0, 1]
 */
vec2 lumonProbeToScreenUV(ivec2 probeCoord, float probeSpacing, vec2 screenSize) {
    vec2 screenPos = (vec2(probeCoord) + 0.5) * probeSpacing;
    return screenPos / screenSize;
}

/**
 * Convert screen pixel to probe grid coordinate.
 * @param screenPixel Screen pixel coordinates
 * @param probeSpacing Pixels per probe cell
 * @return Probe grid coordinate (integer)
 */
ivec2 lumonScreenToProbeCoord(ivec2 screenPixel, int probeSpacing) {
    return screenPixel / probeSpacing;
}

/**
 * Convert probe grid coordinate to screen pixel (probe center).
 * @param probeCoord Probe grid coordinate
 * @param probeSpacing Pixels per probe cell
 * @return Screen pixel at probe center
 */
ivec2 lumonProbeToScreenCoord(ivec2 probeCoord, int probeSpacing) {
    return probeCoord * probeSpacing + probeSpacing / 2;
}

/**
 * Get the 4 probes surrounding a pixel for bilinear interpolation.
 * @param screenUV Screen UV coordinates [0, 1]
 * @param probeSpacing Pixels per probe cell
 * @param screenSize Screen dimensions in pixels
 * @param probe00 Output: bottom-left probe coordinate
 * @param probe10 Output: bottom-right probe coordinate
 * @param probe01 Output: top-left probe coordinate
 * @param probe11 Output: top-right probe coordinate
 * @param weights Output: bilinear interpolation weights (fract of probe position)
 */
void lumonGetEnclosingProbes(vec2 screenUV, int probeSpacing, ivec2 screenSize,
                             out ivec2 probe00, out ivec2 probe10,
                             out ivec2 probe01, out ivec2 probe11,
                             out vec2 weights) {
    vec2 probeUV = screenUV * vec2(screenSize) / float(probeSpacing) - 0.5;
    ivec2 baseProbe = ivec2(floor(probeUV));
    weights = fract(probeUV);
    
    probe00 = baseProbe;
    probe10 = baseProbe + ivec2(1, 0);
    probe01 = baseProbe + ivec2(0, 1);
    probe11 = baseProbe + ivec2(1, 1);
}

// #endregion
#endif // LUMON_COMMON_ASH
