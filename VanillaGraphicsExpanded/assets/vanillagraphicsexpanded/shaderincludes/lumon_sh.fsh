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
 * Project RGB radiance from a direction into separate R, G, B SH L1 coefficients.
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
 * Simplified layout for 2 textures:
 *   tex0.rgb = (shR.x, shG.x, shB.x)  - DC coefficients
 *   tex0.a   = shR.y                   - Red Y1
 *   tex1.rg  = (shG.y, shB.y)         - Green/Blue Y1  
 *   tex1.b   = shR.z                   - Red Y2
 *   tex1.a   = shG.z                   - Green Y2
 * 
 * @param shR Red channel SH coefficients
 * @param shG Green channel SH coefficients
 * @param shB Blue channel SH coefficients
 * @param tex0 Output texture 0
 * @param tex1 Output texture 1
 */
void shPackToTextures(vec4 shR, vec4 shG, vec4 shB, out vec4 tex0, out vec4 tex1)
{
    // Texture 0: DC (coeff 0) for R,G,B + Red coeff 1
    tex0 = vec4(shR.x, shG.x, shB.x, shR.y);
    
    // Texture 1: Remaining coefficients packed
    // Layout: (G.y, B.y, R.z, G.z) - B.z, R.w, G.w, B.w stored implicitly
    // For SH L1, we have 4 coeffs per channel = 12 total
    // With 2 RGBA16F textures = 8 values, we need compression
    // Simplified: just store most important coefficients
    tex1 = vec4(shG.y, shB.y, shR.z, shG.z);
}

/**
 * Unpack SH from 2 textures.
 * @param tex0 Input texture 0
 * @param tex1 Input texture 1
 * @param shR Red channel SH coefficients (out)
 * @param shG Green channel SH coefficients (out)
 * @param shB Blue channel SH coefficients (out)
 */
void shUnpackFromTextures(vec4 tex0, vec4 tex1, out vec4 shR, out vec4 shG, out vec4 shB)
{
    // Unpack from packed format
    shR = vec4(tex0.x, tex0.w, tex1.z, 0.0);  // R: DC, Y1, Y2, (Y3=0)
    shG = vec4(tex0.y, tex1.x, tex1.w, 0.0);  // G: DC, Y1, Y2, (Y3=0)
    shB = vec4(tex0.z, tex1.y, 0.0, 0.0);     // B: DC, Y1, (Y2=0, Y3=0)
    
    // Note: This is lossy compression. For full quality, use 3 textures
    // or compute textures.
}

// Cosine-weighted hemisphere integration constants.
// For diffuse lighting, SH coefficients are pre-multiplied by this
// when evaluating against a surface normal.
const float SH_COSINE_LOBE_C0 = 0.886227;  // pi * SH_C0
const float SH_COSINE_LOBE_C1 = 1.023327;  // (2*pi/3) * SH_C1

/**
 * Evaluate cosine-weighted irradiance for diffuse surfaces.
 * More physically correct than raw shEvaluate for diffuse materials.
 * @param shR Red channel SH coefficients
 * @param shG Green channel SH coefficients
 * @param shB Blue channel SH coefficients
 * @param normal Surface normal direction
 * @return RGB irradiance color
 */
vec3 shEvaluateDiffuseRGB(vec4 shR, vec4 shG, vec4 shB, vec3 normal)
{
    // Apply cosine lobe weighting to SH basis
    vec4 basis = vec4(
        SH_COSINE_LOBE_C0,
        SH_COSINE_LOBE_C1 * normal.y,
        SH_COSINE_LOBE_C1 * normal.z,
        SH_COSINE_LOBE_C1 * normal.x
    );
    
    return max(vec3(0.0), vec3(
        dot(shR, basis),
        dot(shG, basis),
        dot(shB, basis)
    ));
}
#endif // VGE_LUMON_SH_FSH
