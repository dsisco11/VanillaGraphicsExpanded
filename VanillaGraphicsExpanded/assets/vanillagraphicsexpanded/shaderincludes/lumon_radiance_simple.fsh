#ifndef VGE_LUMON_RADIANCE_SIMPLE_FSH
#define VGE_LUMON_RADIANCE_SIMPLE_FSH
// ═══════════════════════════════════════════════════════════════════════════
// LumOn Simplified Radiance Encoding
// ═══════════════════════════════════════════════════════════════════════════
// Alternative to full SH L1 that uses ambient + dominant direction encoding.
// Simpler math, fewer textures, good enough for M1 milestone.
// 
// Storage: 2 RGBA16F textures
//   Texture 0: RGB = ambient color, A = directional ratio
//   Texture 1: RGB = dominant direction (encoded), A = intensity

// ═══════════════════════════════════════════════════════════════════════════
// Data Structure
// ═══════════════════════════════════════════════════════════════════════════

struct SimpleRadiance {
    vec3 ambient;           // Average incoming radiance (RGB)
    vec3 dominantDir;       // Primary light direction (view-space, normalized)
    float directionalRatio; // 0 = pure ambient, 1 = fully directional
    float intensity;        // Overall brightness multiplier
};

// ═══════════════════════════════════════════════════════════════════════════
// Encoding / Decoding
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Encode SimpleRadiance into 2 texture outputs.
 * @param rad Input radiance structure
 * @param tex0 Output: ambient RGB + directional ratio
 * @param tex1 Output: encoded direction + intensity
 */
void encodeRadiance(SimpleRadiance rad, out vec4 tex0, out vec4 tex1)
{
    tex0.rgb = rad.ambient;
    tex0.a = rad.directionalRatio;
    
    // Encode direction from [-1,1] to [0,1] range
    tex1.rgb = rad.dominantDir * 0.5 + 0.5;
    tex1.a = rad.intensity;
}

/**
 * Decode SimpleRadiance from 2 texture inputs.
 * @param tex0 Input: ambient RGB + directional ratio
 * @param tex1 Input: encoded direction + intensity
 * @return Decoded radiance structure
 */
SimpleRadiance decodeRadiance(vec4 tex0, vec4 tex1)
{
    SimpleRadiance rad;
    rad.ambient = tex0.rgb;
    rad.directionalRatio = tex0.a;
    
    // Decode direction from [0,1] to [-1,1] range
    rad.dominantDir = tex1.rgb * 2.0 - 1.0;
    rad.intensity = tex1.a;
    
    return rad;
}

// ═══════════════════════════════════════════════════════════════════════════
// Evaluation
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Evaluate radiance for a given surface normal.
 * Combines ambient and directional contributions based on normal alignment.
 * @param rad Radiance data
 * @param normal Surface normal (view-space)
 * @return RGB radiance value
 */
vec3 evaluateRadiance(SimpleRadiance rad, vec3 normal)
{
    // Directional contribution based on how much normal faces the dominant direction
    float dirContrib = max(dot(normal, rad.dominantDir), 0.0);
    
    // Directional lighting (scaled by how directional the lighting is)
    vec3 directional = rad.ambient * dirContrib * rad.directionalRatio;
    
    // Ambient baseline (reduced by half the directional ratio to conserve energy)
    vec3 ambient = rad.ambient * (1.0 - rad.directionalRatio * 0.5);
    
    return (ambient + directional) * rad.intensity;
}

/**
 * Fast evaluation directly from texture values.
 * @param tex0 Ambient RGB + directional ratio
 * @param tex1 Encoded direction + intensity
 * @param normal Surface normal
 * @return RGB radiance value
 */
vec3 evaluateRadianceFast(vec4 tex0, vec4 tex1, vec3 normal)
{
    vec3 ambient = tex0.rgb;
    float dirRatio = tex0.a;
    vec3 dominantDir = tex1.rgb * 2.0 - 1.0;
    float intensity = tex1.a;
    
    float dirContrib = max(dot(normal, dominantDir), 0.0);
    vec3 directional = ambient * dirContrib * dirRatio;
    vec3 ambientBase = ambient * (1.0 - dirRatio * 0.5);
    
    return (ambientBase + directional) * intensity;
}

// ═══════════════════════════════════════════════════════════════════════════
// Accumulation
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Initialize a SimpleRadiance structure for accumulation.
 * @return Zero-initialized structure
 */
SimpleRadiance initRadiance()
{
    SimpleRadiance rad;
    rad.ambient = vec3(0.0);
    rad.dominantDir = vec3(0.0);
    rad.directionalRatio = 0.0;
    rad.intensity = 0.0;
    return rad;
}

/**
 * Accumulate a radiance sample into the running sum.
 * @param accum Running accumulator (in/out)
 * @param sampleDir Direction the sample came from
 * @param sampleRadiance RGB radiance of the sample
 * @param weight Sample weight
 */
void accumulateRadiance(inout SimpleRadiance accum,
                        vec3 sampleDir, vec3 sampleRadiance, float weight)
{
    // Accumulate ambient (total radiance)
    accum.ambient += sampleRadiance * weight;
    
    // Accumulate direction weighted by luminance
    float lum = dot(sampleRadiance, vec3(0.2126, 0.7152, 0.0722));
    accum.dominantDir += sampleDir * lum * weight;
    
    // Track total weight for normalization
    accum.intensity += weight;
}

/**
 * Normalize accumulated radiance after all samples collected.
 * Converts accumulated values to final radiance structure.
 * @param rad Radiance to normalize (in/out)
 */
void normalizeRadiance(inout SimpleRadiance rad)
{
    if (rad.intensity > 0.0) {
        // Normalize ambient by total weight
        rad.ambient /= rad.intensity;
        
        // Compute directional ratio from accumulated direction vector
        float dirLen = length(rad.dominantDir);
        if (dirLen > 0.001) {
            rad.dominantDir /= dirLen;
            // Ratio is how much direction was accumulated vs total weight
            rad.directionalRatio = min(dirLen / rad.intensity, 1.0);
        } else {
            // No clear direction - use up vector as default
            rad.dominantDir = vec3(0.0, 1.0, 0.0);
            rad.directionalRatio = 0.0;
        }
        
        // Reset intensity to 1.0 (normalization complete)
        rad.intensity = 1.0;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Blending
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Linear interpolation between two radiance values.
 * @param a First radiance
 * @param b Second radiance
 * @param t Interpolation factor [0, 1]
 * @return Interpolated radiance
 */
SimpleRadiance lerpRadiance(SimpleRadiance a, SimpleRadiance b, float t)
{
    SimpleRadiance result;
    result.ambient = mix(a.ambient, b.ambient, t);
    
    // Interpolate direction with re-normalization
    vec3 mixedDir = mix(a.dominantDir, b.dominantDir, t);
    float mixedLen = length(mixedDir);
    result.dominantDir = mixedLen > 0.001 ? mixedDir / mixedLen : vec3(0.0, 1.0, 0.0);
    
    result.directionalRatio = mix(a.directionalRatio, b.directionalRatio, t);
    result.intensity = mix(a.intensity, b.intensity, t);
    
    return result;
}

/**
 * Bilinear interpolation of 4 radiance values.
 * @param r00 Bottom-left radiance
 * @param r10 Bottom-right radiance
 * @param r01 Top-left radiance
 * @param r11 Top-right radiance
 * @param weights Bilinear weights (x, y)
 * @return Interpolated radiance
 */
SimpleRadiance bilinearRadiance(
    SimpleRadiance r00, SimpleRadiance r10,
    SimpleRadiance r01, SimpleRadiance r11,
    vec2 weights)
{
    // Interpolate along X
    SimpleRadiance r0 = lerpRadiance(r00, r10, weights.x);
    SimpleRadiance r1 = lerpRadiance(r01, r11, weights.x);
    
    // Interpolate along Y
    return lerpRadiance(r0, r1, weights.y);
}

/**
 * Bilinear evaluation directly from texture values.
 * More efficient when you just need the final radiance value.
 * @param tex0_00, tex1_00 Bottom-left textures
 * @param tex0_10, tex1_10 Bottom-right textures
 * @param tex0_01, tex1_01 Top-left textures
 * @param tex0_11, tex1_11 Top-right textures
 * @param weights Bilinear weights
 * @param normal Surface normal
 * @return Interpolated and evaluated RGB radiance
 */
vec3 bilinearEvaluateRadiance(
    vec4 tex0_00, vec4 tex1_00,
    vec4 tex0_10, vec4 tex1_10,
    vec4 tex0_01, vec4 tex1_01,
    vec4 tex0_11, vec4 tex1_11,
    vec2 weights,
    vec3 normal)
{
    // Interpolate texture values directly
    vec4 tex0_0 = mix(tex0_00, tex0_10, weights.x);
    vec4 tex1_0 = mix(tex1_00, tex1_10, weights.x);
    vec4 tex0_1 = mix(tex0_01, tex0_11, weights.x);
    vec4 tex1_1 = mix(tex1_01, tex1_11, weights.x);
    
    vec4 tex0 = mix(tex0_0, tex0_1, weights.y);
    vec4 tex1 = mix(tex1_0, tex1_1, weights.y);
    
    // Evaluate at interpolated values
    return evaluateRadianceFast(tex0, tex1, normal);
}

// ═══════════════════════════════════════════════════════════════════════════
// Temporal Blending
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Temporal blend between current and history radiance.
 * Uses exponential moving average for stability.
 * @param current Current frame radiance
 * @param history Previous frame radiance
 * @param alpha Blend factor (0 = all history, 1 = all current)
 * @return Temporally blended radiance
 */
SimpleRadiance temporalBlendRadiance(SimpleRadiance current, SimpleRadiance history, float alpha)
{
    return lerpRadiance(history, current, alpha);
}

/**
 * Temporal blend directly on texture values.
 * @param currentTex0, currentTex1 Current frame textures
 * @param historyTex0, historyTex1 History frame textures
 * @param alpha Blend factor
 * @param outTex0, outTex1 Output blended textures
 */
void temporalBlendTextures(
    vec4 currentTex0, vec4 currentTex1,
    vec4 historyTex0, vec4 historyTex1,
    float alpha,
    out vec4 outTex0, out vec4 outTex1)
{
    outTex0 = mix(historyTex0, currentTex0, alpha);
    
    // Direction needs special handling to maintain normalization
    vec3 histDir = historyTex1.rgb * 2.0 - 1.0;
    vec3 currDir = currentTex1.rgb * 2.0 - 1.0;
    vec3 blendDir = mix(histDir, currDir, alpha);
    float blendLen = length(blendDir);
    blendDir = blendLen > 0.001 ? blendDir / blendLen : vec3(0.0, 1.0, 0.0);
    
    outTex1.rgb = blendDir * 0.5 + 0.5;
    outTex1.a = mix(historyTex1.a, currentTex1.a, alpha);
}

#endif // VGE_LUMON_RADIANCE_SIMPLE_FSH
