#ifndef VGE_LUMON_SH_FSH
#define VGE_LUMON_SH_FSH
// ═══════════════════════════════════════════════════════════════════════════
// LumOn Spherical Harmonics Helper Functions
// ═══════════════════════════════════════════════════════════════════════════
// Provides SH L1 (4 coefficients) encoding and evaluation for radiance caching.
// Used by probe trace and gather passes.

// SH L1 basis functions (normalized)
// Y0 = 0.282095 (DC)
// Y1 = 0.488603 * y
// Y2 = 0.488603 * z  
// Y3 = 0.488603 * x

const float SH_C0 = 0.282095;  // 1 / (2 * sqrt(pi))
const float SH_C1 = 0.488603;  // sqrt(3) / (2 * sqrt(pi))

// Cosine lobe coefficients for diffuse BRDF convolution
// These are the zonal harmonics coefficients for max(dot(n, l), 0)
const float SH_COSINE_A0 = 3.141592654;  // pi
const float SH_COSINE_A1 = 2.094395102;  // 2*pi/3

/**
 * Encode a direction into SH L1 basis weights.
 * @param dir Normalized direction vector
 * @return vec4 containing (Y0, Y1, Y2, Y3) basis weights
 */
vec4 shEncode(vec3 dir)
{
    return vec4(
        SH_C0,           // Y0: constant (DC)
        SH_C1 * dir.y,   // Y1: Y component
        SH_C1 * dir.z,   // Y2: Z component
        SH_C1 * dir.x    // Y3: X component
    );
}

/**
 * Evaluate SH L1 for a given direction.
 * Reconstructs irradiance from SH coefficients.
 * @param sh vec4 containing SH L1 coefficients (c0, c1, c2, c3)
 * @param dir Normalized direction to evaluate
 * @return Scalar irradiance value
 */
float shEvaluate(vec4 sh, vec3 dir)
{
    vec4 basis = shEncode(dir);
    return max(0.0, dot(sh, basis));
}

/**
 * Evaluate RGB SH L1 for a given direction.
 * Reconstructs color irradiance from separate R, G, B SH coefficient sets.
 * @param shR SH L1 coefficients for red channel
 * @param shG SH L1 coefficients for green channel
 * @param shB SH L1 coefficients for blue channel
 * @param dir Normalized direction to evaluate
 * @return RGB irradiance color
 */
vec3 shEvaluateRGB(vec4 shR, vec4 shG, vec4 shB, vec3 dir)
{
    vec4 basis = shEncode(dir);
    return max(vec3(0.0), vec3(
        dot(shR, basis),
        dot(shG, basis),
        dot(shB, basis)
    ));
}

/**
 * Project incoming radiance from a direction into SH L1 coefficients.
 * Accumulates into existing SH with optional weight.
 * @param sh Existing SH coefficients (in/out)
 * @param dir Direction of incoming radiance
 * @param radiance Incoming radiance value
 * @param weight Optional weight for this sample (default 1.0)
 */
void shProject(inout vec4 sh, vec3 dir, float radiance, float weight)
{
    vec4 basis = shEncode(dir);
    sh += basis * radiance * weight;
}

/**
 * Project a radiance sample onto SH L1 basis (non-accumulating).
 * @param dir      Normalized direction the radiance came from
 * @param radiance RGB radiance value
 * @param shR      Output: SH coefficients for red channel
 * @param shG      Output: SH coefficients for green channel
 * @param shB      Output: SH coefficients for blue channel
 */
void shProjectOut(vec3 dir, vec3 radiance,
                  out vec4 shR, out vec4 shG, out vec4 shB)
{
    vec4 basis = shEncode(dir);
    shR = basis * radiance.r;
    shG = basis * radiance.g;
    shB = basis * radiance.b;
}

/**
 * Project RGB radiance from a direction into separate R, G, B SH L1 coefficients.
 * Accumulates into existing SH coefficients.
 * @param shR Red SH coefficients (in/out)
 * @param shG Green SH coefficients (in/out)
 * @param shB Blue SH coefficients (in/out)
 * @param dir Direction of incoming radiance
 * @param radiance RGB radiance value
 * @param weight Optional weight for this sample
 */
void shProjectRGB(inout vec4 shR, inout vec4 shG, inout vec4 shB, vec3 dir, vec3 radiance, float weight)
{
    vec4 basis = shEncode(dir);
    shR += basis * radiance.r * weight;
    shG += basis * radiance.g * weight;
    shB += basis * radiance.b * weight;
}

/**
 * Pack 3 RGB SH coefficient sets into 2 RGBA textures for storage.
 * 
 * Layout matches design doc Section 4.1:
 *   tex0 = (shR.x, shG.x, shB.x, shR.y)  - DC coefficients + Red Y1
 *   tex1.rg = (shG.y, shB.y)             - Green/Blue Y1
 *   tex1.b = luminance(avgZX)            - Compressed Z+X coefficients
 *   tex1.a = reserved
 * 
 * @param shR Red channel SH coefficients
 * @param shG Green channel SH coefficients
 * @param shB Blue channel SH coefficients
 * @param tex0 Output texture 0
 * @param tex1 Output texture 1
 */
void shPackToTextures(vec4 shR, vec4 shG, vec4 shB, out vec4 tex0, out vec4 tex1)
{
    // Texture 0: DC terms + first directional R
    tex0 = vec4(shR.x, shG.x, shB.x, shR.y);
    
    // Texture 1: Remaining directional terms (compressed)
    // Pack Y directions
    tex1.r = shG.y;
    tex1.g = shB.y;
    
    // Pack Z and X directions (averaged for compression)
    // This loses some precision but fits in 2 textures
    vec3 avgZX = (vec3(shR.z, shG.z, shB.z) + vec3(shR.w, shG.w, shB.w)) * 0.5;
    tex1.b = dot(avgZX, vec3(0.2126, 0.7152, 0.0722));  // Luminance
    tex1.a = 0.0;  // Reserved
}

/**
 * Unpack SH from 2 textures.
 * Reconstructs approximate SH from compressed format.
 * @param tex0 Input texture 0
 * @param tex1 Input texture 1
 * @param shR Red channel SH coefficients (out)
 * @param shG Green channel SH coefficients (out)
 * @param shB Blue channel SH coefficients (out)
 */
void shUnpackFromTextures(vec4 tex0, vec4 tex1, out vec4 shR, out vec4 shG, out vec4 shB)
{
    // DC terms from texture 0
    shR.x = tex0.r;
    shG.x = tex0.g;
    shB.x = tex0.b;
    
    // Y directional
    shR.y = tex0.a;
    shG.y = tex1.r;
    shB.y = tex1.g;
    
    // Z and X (reconstruct from compressed luminance)
    // This is approximate reconstruction
    float lumZX = tex1.b;
    shR.z = shR.w = lumZX * 0.5;
    shG.z = shG.w = lumZX * 0.5;
    shB.z = shB.w = lumZX * 0.5;
    
    // Note: This is lossy compression. For full quality, use 3 textures
    // or compute textures.
}

/**
 * Evaluate cosine-weighted irradiance for diffuse surfaces.
 * This is what you want for Lambertian surfaces.
 * Convolves SH with cosine lobe (zonal harmonic).
 * @param shR Red channel SH coefficients
 * @param shG Green channel SH coefficients
 * @param shB Blue channel SH coefficients
 * @param normal Surface normal direction
 * @return Irradiance (integrate incoming radiance * cos(theta))
 */
vec3 shEvaluateDiffuseRGB(vec4 shR, vec4 shG, vec4 shB, vec3 normal)
{
    // Evaluate SH basis at normal direction
    vec4 basis = shEncode(normal);
    
    // Apply cosine lobe convolution weights
    // Result is: A0*c0 + A1*(c1*ny + c2*nz + c3*nx)
    vec4 cosineWeights = vec4(
        SH_COSINE_A0,  // DC term
        SH_COSINE_A1,  // Directional terms
        SH_COSINE_A1,
        SH_COSINE_A1
    );
    
    vec4 convolved = basis * cosineWeights;
    
    // Divide by pi for diffuse BRDF
    return max(vec3(0.0), vec3(
        dot(shR, convolved),
        dot(shG, convolved),
        dot(shB, convolved)
    )) / 3.141592654;
}

/**
 * Evaluate cosine-weighted irradiance for diffuse surfaces (scalar SH).
 * @param sh SH L1 coefficients
 * @param normal Surface normal direction
 * @return Scalar irradiance
 */
float shEvaluateDiffuse(vec4 sh, vec3 normal)
{
    vec4 basis = shEncode(normal);

    vec4 cosineWeights = vec4(
        SH_COSINE_A0,
        SH_COSINE_A1,
        SH_COSINE_A1,
        SH_COSINE_A1
    );

    vec4 convolved = basis * cosineWeights;
    return max(0.0, dot(sh, convolved)) / 3.141592654;
}

/**
 * Evaluate cosine-weighted hemisphere irradiance (no 1/pi diffuse BRDF factor).
 * Use this when you want the integrated incoming energy over the hemisphere.
 */
vec3 shEvaluateHemisphereIrradianceRGB(vec4 shR, vec4 shG, vec4 shB, vec3 normal)
{
    vec4 basis = shEncode(normal);

    vec4 cosineWeights = vec4(
        SH_COSINE_A0,
        SH_COSINE_A1,
        SH_COSINE_A1,
        SH_COSINE_A1
    );

    vec4 convolved = basis * cosineWeights;

    return max(vec3(0.0), vec3(
        dot(shR, convolved),
        dot(shG, convolved),
        dot(shB, convolved)
    ));
}

/**
 * Evaluate cosine-weighted hemisphere irradiance (scalar SH, no 1/pi factor).
 */
float shEvaluateHemisphereIrradiance(vec4 sh, vec3 normal)
{
    vec4 basis = shEncode(normal);

    vec4 cosineWeights = vec4(
        SH_COSINE_A0,
        SH_COSINE_A1,
        SH_COSINE_A1,
        SH_COSINE_A1
    );

    vec4 convolved = basis * cosineWeights;
    return max(0.0, dot(sh, convolved));
}

// ═══════════════════════════════════════════════════════════════════════════
// Accumulation & Blending
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Accumulate SH coefficients (add sample to running sum).
 * @param shR Red channel SH coefficients (in/out)
 * @param shG Green channel SH coefficients (in/out)
 * @param shB Blue channel SH coefficients (in/out)
 * @param sampleR Red channel sample to add
 * @param sampleG Green channel sample to add
 * @param sampleB Blue channel sample to add
 */
void shAccumulate(inout vec4 shR, inout vec4 shG, inout vec4 shB,
                  vec4 sampleR, vec4 sampleG, vec4 sampleB)
{
    shR += sampleR;
    shG += sampleG;
    shB += sampleB;
}

/**
 * Scale SH coefficients by a scalar value.
 * @param shR Red channel SH coefficients (in/out)
 * @param shG Green channel SH coefficients (in/out)
 * @param shB Blue channel SH coefficients (in/out)
 * @param scale Scale factor
 */
void shScale(inout vec4 shR, inout vec4 shG, inout vec4 shB, float scale)
{
    shR *= scale;
    shG *= scale;
    shB *= scale;
}

/**
 * Linear interpolation of SH coefficients.
 * @param shR_a First set red coefficients
 * @param shG_a First set green coefficients
 * @param shB_a First set blue coefficients
 * @param shR_b Second set red coefficients
 * @param shG_b Second set green coefficients
 * @param shB_b Second set blue coefficients
 * @param t Interpolation factor [0, 1]
 * @param shR Output red coefficients
 * @param shG Output green coefficients
 * @param shB Output blue coefficients
 */
void shLerp(vec4 shR_a, vec4 shG_a, vec4 shB_a,
            vec4 shR_b, vec4 shG_b, vec4 shB_b,
            float t,
            out vec4 shR, out vec4 shG, out vec4 shB)
{
    shR = mix(shR_a, shR_b, t);
    shG = mix(shG_a, shG_b, t);
    shB = mix(shB_a, shB_b, t);
}

/**
 * Bilinear interpolation of 4 SH probes and evaluate at direction.
 * @param shR_00, shG_00, shB_00 Bottom-left probe SH
 * @param shR_10, shG_10, shB_10 Bottom-right probe SH
 * @param shR_01, shG_01, shB_01 Top-left probe SH
 * @param shR_11, shG_11, shB_11 Top-right probe SH
 * @param weights Bilinear interpolation weights (x, y)
 * @param evalDir Direction to evaluate SH at
 * @return Interpolated and evaluated RGB radiance
 */
vec3 shBilinearEvaluate(
    vec4 shR_00, vec4 shG_00, vec4 shB_00,
    vec4 shR_10, vec4 shG_10, vec4 shB_10,
    vec4 shR_01, vec4 shG_01, vec4 shB_01,
    vec4 shR_11, vec4 shG_11, vec4 shB_11,
    vec2 weights,
    vec3 evalDir)
{
    // Interpolate along X for bottom and top rows
    vec4 shR_0 = mix(shR_00, shR_10, weights.x);
    vec4 shG_0 = mix(shG_00, shG_10, weights.x);
    vec4 shB_0 = mix(shB_00, shB_10, weights.x);

    vec4 shR_1 = mix(shR_01, shR_11, weights.x);
    vec4 shG_1 = mix(shG_01, shG_11, weights.x);
    vec4 shB_1 = mix(shB_01, shB_11, weights.x);

    // Interpolate along Y
    vec4 shR = mix(shR_0, shR_1, weights.y);
    vec4 shG = mix(shG_0, shG_1, weights.y);
    vec4 shB = mix(shB_0, shB_1, weights.y);

    // Evaluate at direction
    return shEvaluateRGB(shR, shG, shB, evalDir);
}

// ═══════════════════════════════════════════════════════════════════════════
// Normalization & Clamping
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Normalize accumulated SH by sample count.
 * @param shR Red channel SH coefficients (in/out)
 * @param shG Green channel SH coefficients (in/out)
 * @param shB Blue channel SH coefficients (in/out)
 * @param sampleCount Number of samples accumulated
 */
void shNormalize(inout vec4 shR, inout vec4 shG, inout vec4 shB, float sampleCount)
{
    if (sampleCount > 0.0) {
        float invCount = 1.0 / sampleCount;
        shR *= invCount;
        shG *= invCount;
        shB *= invCount;
    }
}

/**
 * Clamp SH to prevent negative radiance reconstruction.
 * Ensures DC term is positive and limits directional terms.
 * @param shR Red channel SH coefficients (in/out)
 * @param shG Green channel SH coefficients (in/out)
 * @param shB Blue channel SH coefficients (in/out)
 */
void shClampNegative(inout vec4 shR, inout vec4 shG, inout vec4 shB)
{
    // Ensure DC term is positive
    shR.x = max(shR.x, 0.0);
    shG.x = max(shG.x, 0.0);
    shB.x = max(shB.x, 0.0);

    // Limit directional terms to not exceed DC (prevents negative reconstruction)
    float maxDirR = shR.x * 0.5;
    float maxDirG = shG.x * 0.5;
    float maxDirB = shB.x * 0.5;

    shR.yzw = clamp(shR.yzw, vec3(-maxDirR), vec3(maxDirR));
    shG.yzw = clamp(shG.yzw, vec3(-maxDirG), vec3(maxDirG));
    shB.yzw = clamp(shB.yzw, vec3(-maxDirB), vec3(maxDirB));
}

// ═══════════════════════════════════════════════════════════════════════════
// Utility Functions
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Extract dominant light direction from SH L1 coefficients.
 * @param shR Red channel SH coefficients
 * @param shG Green channel SH coefficients
 * @param shB Blue channel SH coefficients
 * @return Normalized dominant direction (defaults to up if no direction)
 */
vec3 shDominantDirection(vec4 shR, vec4 shG, vec4 shB)
{
    // Directional components are stored as (DC, Y, Z, X)
    // Extract (X, Y, Z) from coefficients 3, 1, 2
    vec3 dirR = vec3(shR.w, shR.y, shR.z);
    vec3 dirG = vec3(shG.w, shG.y, shG.z);
    vec3 dirB = vec3(shB.w, shB.y, shB.z);

    // Weight by luminance coefficients
    vec3 dir = dirR * 0.2126 + dirG * 0.7152 + dirB * 0.0722;

    float len = length(dir);
    return len > 0.001 ? dir / len : vec3(0.0, 1.0, 0.0);
}

/**
 * Get ambient (DC) term as RGB color.
 * @param shR Red channel SH coefficients
 * @param shG Green channel SH coefficients
 * @param shB Blue channel SH coefficients
 * @return Ambient RGB color
 */
vec3 shAmbient(vec4 shR, vec4 shG, vec4 shB)
{
    return vec3(shR.x, shG.x, shB.x);
}

/**
 * Project radiance with cosine weighting for hemisphere integration.
 * @param shR Red channel SH coefficients (in/out)
 * @param shG Green channel SH coefficients (in/out)
 * @param shB Blue channel SH coefficients (in/out)
 * @param dir Direction of incoming radiance
 * @param radiance RGB radiance value
 * @param normal Surface normal for cosine weighting
 * @param weight Sample weight
 */
void shProjectCosineRGB(inout vec4 shR, inout vec4 shG, inout vec4 shB,
                        vec3 dir, vec3 radiance, vec3 normal, float weight)
{
    float cosTheta = max(dot(normal, dir), 0.0);
    vec3 weighted = radiance * cosTheta;
    shProjectRGB(shR, shG, shB, dir, weighted, weight);
}

#endif // VGE_LUMON_SH_FSH
