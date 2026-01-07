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

/// <summary>
/// Encode a direction into SH L1 basis weights.
/// </summary>
/// <param name="dir">Normalized direction vector</param>
/// <returns>vec4 containing (Y0, Y1, Y2, Y3) basis weights</returns>
vec4 shEncode(vec3 dir)
{
    return vec4(
        SH_C0,           // Y0: constant (DC)
        SH_C1 * dir.y,   // Y1: Y component
        SH_C1 * dir.z,   // Y2: Z component
        SH_C1 * dir.x    // Y3: X component
    );
}

/// <summary>
/// Evaluate SH L1 for a given direction.
/// Reconstructs irradiance from SH coefficients.
/// </summary>
/// <param name="sh">vec4 containing SH L1 coefficients (c0, c1, c2, c3)</param>
/// <param name="dir">Normalized direction to evaluate</param>
/// <returns>Scalar irradiance value</returns>
float shEvaluate(vec4 sh, vec3 dir)
{
    vec4 basis = shEncode(dir);
    return max(0.0, dot(sh, basis));
}

/// <summary>
/// Evaluate RGB SH L1 for a given direction.
/// Reconstructs color irradiance from separate R, G, B SH coefficient sets.
/// </summary>
/// <param name="shR">SH L1 coefficients for red channel</param>
/// <param name="shG">SH L1 coefficients for green channel</param>
/// <param name="shB">SH L1 coefficients for blue channel</param>
/// <param name="dir">Normalized direction to evaluate</param>
/// <returns>RGB irradiance color</returns>
vec3 shEvaluateRGB(vec4 shR, vec4 shG, vec4 shB, vec3 dir)
{
    vec4 basis = shEncode(dir);
    return max(vec3(0.0), vec3(
        dot(shR, basis),
        dot(shG, basis),
        dot(shB, basis)
    ));
}

/// <summary>
/// Project incoming radiance from a direction into SH L1 coefficients.
/// Accumulates into existing SH with optional weight.
/// </summary>
/// <param name="sh">Existing SH coefficients (in/out)</param>
/// <param name="dir">Direction of incoming radiance</param>
/// <param name="radiance">Incoming radiance value</param>
/// <param name="weight">Optional weight for this sample (default 1.0)</param>
void shProject(inout vec4 sh, vec3 dir, float radiance, float weight)
{
    vec4 basis = shEncode(dir);
    sh += basis * radiance * weight;
}

/// <summary>
/// Project RGB radiance from a direction into separate R, G, B SH L1 coefficients.
/// </summary>
/// <param name="shR">Red SH coefficients (in/out)</param>
/// <param name="shG">Green SH coefficients (in/out)</param>
/// <param name="shB">Blue SH coefficients (in/out)</param>
/// <param name="dir">Direction of incoming radiance</param>
/// <param name="radiance">RGB radiance value</param>
/// <param name="weight">Optional weight for this sample</param>
void shProjectRGB(inout vec4 shR, inout vec4 shG, inout vec4 shB, vec3 dir, vec3 radiance, float weight)
{
    vec4 basis = shEncode(dir);
    shR += basis * radiance.r * weight;
    shG += basis * radiance.g * weight;
    shB += basis * radiance.b * weight;
}

/// <summary>
/// Pack 3 RGB SH coefficient sets into 2 RGBA textures for storage.
/// Layout:
///   tex0 = (shR.x, shG.x, shB.x, shR.y)
///   tex1 = (shG.y, shB.y, shR.z, shG.z, shB.z, shR.w, shG.w, shB.w)
/// Note: tex1 packs 8 values into RGBA16F using .xyzw for first 4, 
/// but we only have RGBA, so we use a different layout:
///   tex0 = (shR.x, shR.y, shR.z, shR.w) - Red SH
///   tex1 = (shG.x, shG.y, shG.z, shG.w) - Green SH  
///   (Blue is reconstructed or stored separately)
/// 
/// Simplified layout for 2 textures:
///   tex0.rgb = (shR.x, shG.x, shB.x)  - DC coefficients
///   tex0.a   = shR.y                   - Red Y1
///   tex1.rg  = (shG.y, shB.y)         - Green/Blue Y1  
///   tex1.b   = shR.z                   - Red Y2
///   tex1.a   = (packed or separate)
/// </summary>

// Pack SH into 2 textures (simplified: store per-channel SH)
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

// Unpack SH from 2 textures
void shUnpackFromTextures(vec4 tex0, vec4 tex1, out vec4 shR, out vec4 shG, out vec4 shB)
{
    // Unpack from packed format
    shR = vec4(tex0.x, tex0.w, tex1.z, 0.0);  // R: DC, Y1, Y2, (Y3=0)
    shG = vec4(tex0.y, tex1.x, tex1.w, 0.0);  // G: DC, Y1, Y2, (Y3=0)
    shB = vec4(tex0.z, tex1.y, 0.0, 0.0);     // B: DC, Y1, (Y2=0, Y3=0)
    
    // Note: This is lossy compression. For full quality, use 3 textures
    // or compute textures.
}

/// <summary>
/// Cosine-weighted hemisphere integration constant.
/// For diffuse lighting, SH coefficients are pre-multiplied by this
/// when evaluating against a surface normal.
/// </summary>
const float SH_COSINE_LOBE_C0 = 0.886227;  // pi * SH_C0
const float SH_COSINE_LOBE_C1 = 1.023327;  // (2*pi/3) * SH_C1

/// <summary>
/// Evaluate cosine-weighted irradiance for diffuse surfaces.
/// More physically correct than raw shEvaluate for diffuse materials.
/// </summary>
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
