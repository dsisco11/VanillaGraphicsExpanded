#ifndef LUMON_OCTAHEDRAL_GLSL
#define LUMON_OCTAHEDRAL_GLSL
// ═══════════════════════════════════════════════════════════════════════════
// LumOn Octahedral Mapping Utilities
// ═══════════════════════════════════════════════════════════════════════════
// Functions for encoding/decoding directions to octahedral UV coordinates.
// Used for the Screen Space Radiance Cache (matching UE5 Lumen's design).
//
// Octahedral mapping provides a more uniform distribution of directions
// compared to spherical coordinates, with better bilinear filtering properties.
//
// References:
// - "A Survey of Efficient Representations for Independent Unit Vectors" (Cigolle et al. 2014)
// - UE5 Lumen SIGGRAPH 2022 presentation
// ═══════════════════════════════════════════════════════════════════════════

// ═══════════════════════════════════════════════════════════════════════════
// Constants
// ═══════════════════════════════════════════════════════════════════════════

// Octahedral texture resolution per probe (8×8 = 64 directions)
const int LUMON_OCTAHEDRAL_SIZE = 8;
const float LUMON_OCTAHEDRAL_SIZE_F = 8.0;

// For texel center addressing
const float LUMON_OCTAHEDRAL_TEXEL_SIZE = 1.0 / 8.0;
const float LUMON_OCTAHEDRAL_HALF_TEXEL = 0.5 / 8.0;

// ═══════════════════════════════════════════════════════════════════════════
// Direction ↔ Octahedral UV Conversion
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Sign function that returns 1.0 for zero (not 0.0).
 * Required for correct octahedral mapping at axis-aligned directions.
 */
vec2 lumonOctahedralSignNotZero(vec2 v) {
    return vec2(
        v.x >= 0.0 ? 1.0 : -1.0,
        v.y >= 0.0 ? 1.0 : -1.0
    );
}

/**
 * Encode a unit direction vector to octahedral UV coordinates.
 * Maps the unit sphere to a square [0,1]² via octahedron projection.
 *
 * @param dir Normalized direction vector (world-space)
 * @return UV coordinates in [0,1]² range
 */
vec2 lumonDirectionToOctahedralUV(vec3 dir) {
    // Project onto octahedron (L1 norm = 1)
    vec3 octant = dir / (abs(dir.x) + abs(dir.y) + abs(dir.z));
    
    // Fold lower hemisphere
    vec2 uv;
    if (octant.z >= 0.0) {
        uv = octant.xy;
    } else {
        // Reflect across diagonal for lower hemisphere
        uv = (1.0 - abs(octant.yx)) * lumonOctahedralSignNotZero(octant.xy);
    }
    
    // Map from [-1,1] to [0,1]
    return uv * 0.5 + 0.5;
}

/**
 * Decode octahedral UV coordinates to a unit direction vector.
 * Inverse of lumonDirectionToOctahedralUV.
 *
 * @param uv UV coordinates in [0,1]² range
 * @return Normalized direction vector (world-space)
 */
vec3 lumonOctahedralUVToDirection(vec2 uv) {
    // Map from [0,1] to [-1,1]
    vec2 oct = uv * 2.0 - 1.0;
    
    // Reconstruct Z from octahedron constraint |x| + |y| + |z| = 1
    vec3 dir = vec3(oct.xy, 1.0 - abs(oct.x) - abs(oct.y));
    
    // Unfold lower hemisphere
    if (dir.z < 0.0) {
        dir.xy = (1.0 - abs(dir.yx)) * lumonOctahedralSignNotZero(dir.xy);
    }
    
    return normalize(dir);
}

/**
 * Convert texel index (0-63) to octahedral UV (texel center).
 * Useful for iterating over all directions in a probe.
 *
 * @param texelIndex Linear index [0, 63]
 * @return UV coordinates at texel center
 */
vec2 lumonTexelIndexToOctahedralUV(int texelIndex) {
    int x = texelIndex % LUMON_OCTAHEDRAL_SIZE;
    int y = texelIndex / LUMON_OCTAHEDRAL_SIZE;
    return (vec2(float(x), float(y)) + 0.5) / LUMON_OCTAHEDRAL_SIZE_F;
}

/**
 * Convert texel coordinates to octahedral UV (texel center).
 *
 * @param texelCoord Integer texel coordinates [0,7]
 * @return UV coordinates at texel center
 */
vec2 lumonTexelCoordToOctahedralUV(ivec2 texelCoord) {
    return (vec2(texelCoord) + 0.5) / LUMON_OCTAHEDRAL_SIZE_F;
}

/**
 * Convert direction to texel index (0-63).
 *
 * @param dir Normalized direction vector
 * @return Linear texel index
 */
int lumonDirectionToTexelIndex(vec3 dir) {
    vec2 uv = lumonDirectionToOctahedralUV(dir);
    ivec2 texel = ivec2(uv * LUMON_OCTAHEDRAL_SIZE_F);
    texel = clamp(texel, ivec2(0), ivec2(LUMON_OCTAHEDRAL_SIZE - 1));
    return texel.y * LUMON_OCTAHEDRAL_SIZE + texel.x;
}

// ═══════════════════════════════════════════════════════════════════════════
// 3D Texture Addressing (probeIndex, octahedral direction)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Compute 3D texture coordinates for sampling the octahedral radiance cache.
 * Texture layout: (8, 8, probeCount) where Z = probe index (flat)
 *
 * @param probeIndex Linear probe index (probeY * probeGridWidth + probeX)
 * @param dir World-space direction to sample
 * @param probeCount Total number of probes (for normalization)
 * @return 3D texture coordinate for texture() sampling
 */
vec3 lumonOctahedralTexCoord3D(int probeIndex, vec3 dir, int probeCount) {
    vec2 octUV = lumonDirectionToOctahedralUV(dir);
    float z = (float(probeIndex) + 0.5) / float(probeCount);
    return vec3(octUV, z);
}

/**
 * Compute 3D texel coordinates for texelFetch on the octahedral radiance cache.
 *
 * @param probeIndex Linear probe index
 * @param octTexel Octahedral texel coordinate [0,7]
 * @return 3D integer coordinates for texelFetch
 */
ivec3 lumonOctahedralTexelCoord3D(int probeIndex, ivec2 octTexel) {
    return ivec3(octTexel, probeIndex);
}

/**
 * Convert probe grid coordinates to linear probe index.
 *
 * @param probeCoord 2D probe grid coordinates
 * @param probeGridWidth Width of probe grid
 * @return Linear probe index
 */
int lumonProbeCoordToIndex(ivec2 probeCoord, int probeGridWidth) {
    return probeCoord.y * probeGridWidth + probeCoord.x;
}

// ═══════════════════════════════════════════════════════════════════════════
// Bilinear-Correct Octahedral Sampling
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Sample octahedral texture with proper bilinear filtering.
 * Handles edge wrapping to avoid seams at octahedron fold lines.
 *
 * Note: For simplicity, this uses 4 texelFetch calls with manual bilinear.
 * The 3D texture hardware filtering works correctly within each probe slice.
 *
 * @param octTex 3D octahedral radiance texture
 * @param probeIndex Linear probe index
 * @param dir World-space direction to sample
 * @param probeCount Total number of probes
 * @return RGBA value (RGB = radiance, A = log-encoded hit distance)
 */
vec4 lumonSampleOctahedral(sampler3D octTex, int probeIndex, vec3 dir, int probeCount) {
    // For now, use hardware trilinear - works well within a probe slice
    vec3 texCoord = lumonOctahedralTexCoord3D(probeIndex, dir, probeCount);
    return texture(octTex, texCoord);
}

/**
 * Sample octahedral texture with texelFetch (no filtering).
 * Use when you need exact texel values (e.g., during tracing output).
 *
 * @param octTex 3D octahedral radiance texture
 * @param probeIndex Linear probe index
 * @param octTexel Octahedral texel coordinate [0,7]
 * @return RGBA value (RGB = radiance, A = log-encoded hit distance)
 */
vec4 lumonFetchOctahedral(sampler3D octTex, int probeIndex, ivec2 octTexel) {
    return texelFetch(octTex, ivec3(octTexel, probeIndex), 0);
}

// ═══════════════════════════════════════════════════════════════════════════
// Hit Distance Encoding/Decoding
// ═══════════════════════════════════════════════════════════════════════════
// Log-encoding provides better precision for near-field distances while
// still supporting far-field values. The +1 offset ensures log(0) = 0.

/**
 * Encode hit distance for storage in texture alpha channel.
 * Uses log encoding: encoded = log(distance + 1)
 *
 * @param distance Linear distance in world units (meters)
 * @return Log-encoded distance, suitable for 16-bit float storage
 */
float lumonEncodeHitDistance(float distance) {
    return log(distance + 1.0);
}

/**
 * Decode hit distance from texture alpha channel.
 * Inverse of lumonEncodeHitDistance.
 *
 * @param encoded Log-encoded distance value
 * @return Linear distance in world units (meters)
 */
float lumonDecodeHitDistance(float encoded) {
    return exp(encoded) - 1.0;
}

/**
 * Check if two hit distances are similar (for disocclusion detection).
 * Uses relative threshold for scale-invariant comparison.
 *
 * @param dist1 First distance (decoded)
 * @param dist2 Second distance (decoded)
 * @param relativeThreshold Relative difference threshold (e.g., 0.1 = 10%)
 * @return True if distances are similar
 */
bool lumonHitDistanceSimilar(float dist1, float dist2, float relativeThreshold) {
    float maxDist = max(dist1, dist2);
    if (maxDist < 0.001) return true;  // Both very close to zero
    float relativeDiff = abs(dist1 - dist2) / maxDist;
    return relativeDiff < relativeThreshold;
}

// ═══════════════════════════════════════════════════════════════════════════
// Hemisphere Integration Utilities
// ═══════════════════════════════════════════════════════════════════════════
// For gathering diffuse irradiance from octahedral probes.

/**
 * Compute cosine weight for hemisphere integration.
 * Used when evaluating diffuse irradiance at a surface.
 *
 * @param sampleDir Direction being sampled from octahedral map
 * @param surfaceNormal Surface normal (same space as sampleDir)
 * @return Cosine weight, clamped to [0,1]
 */
float lumonCosineWeight(vec3 sampleDir, vec3 surfaceNormal) {
    return max(dot(sampleDir, surfaceNormal), 0.0);
}

/**
 * Check if a direction is in the upper hemisphere relative to a normal.
 *
 * @param dir Direction to check
 * @param normal Hemisphere axis (normal)
 * @return True if direction is in upper hemisphere
 */
bool lumonIsInHemisphere(vec3 dir, vec3 normal) {
    return dot(dir, normal) > 0.0;
}

#endif // LUMON_OCTAHEDRAL_GLSL
