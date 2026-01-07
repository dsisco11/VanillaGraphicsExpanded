#ifndef VGE_LUMON_SH_PACK_FSH
#define VGE_LUMON_SH_PACK_FSH
// ═══════════════════════════════════════════════════════════════════════════
// LumOn SH Packing Helpers
// ═══════════════════════════════════════════════════════════════════════════
// Advanced packing/unpacking functions for SH storage in textures.
// Supports both 2-texture (compressed) and 3-texture (full fidelity) layouts.
//
// Texture Layouts:
//
// 2-TEXTURE LAYOUT (Compressed, used in M1-M2):
//   Texture 0: (SH0_R, SH0_G, SH0_B, SH1_R) - DC terms + Red Y1
//   Texture 1: (SH1_G, SH1_B, SH2_R, SH2_G) - G/B Y1, R/G Y2
//   Note: SH2_B, SH3_R, SH3_G, SH3_B are approximated/lost
//
// 3-TEXTURE LAYOUT (Full Fidelity, for M3+):
//   Texture 0: (SH0_R, SH0_G, SH0_B, SH1_R) - DC terms + Red Y1
//   Texture 1: (SH1_G, SH1_B, SH2_R, SH2_G) - G/B Y1, R/G Y2
//   Texture 2: (SH2_B, SH3_R, SH3_G, SH3_B) - B Y2, all Y3 (X direction)

// ═══════════════════════════════════════════════════════════════════════════
// 2-Texture Layout (Compressed) - Design Doc Section 4.1
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Pack SH RGB into 2 RGBA16F textures using luminance compression.
 * Matches design doc Section 4.1 exactly.
 * @param shR Red channel SH L1 coefficients (c0, c1, c2, c3)
 * @param shG Green channel SH L1 coefficients
 * @param shB Blue channel SH L1 coefficients
 * @param tex0 Output texture 0
 * @param tex1 Output texture 1
 */
void shPack2Tex(vec4 shR, vec4 shG, vec4 shB, out vec4 tex0, out vec4 tex1)
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
 * Unpack SH RGB from 2 RGBA16F textures.
 * Reconstructs approximate SH from luminance-compressed format.
 * @param tex0 Input texture 0
 * @param tex1 Input texture 1
 * @param shR Red channel SH L1 coefficients (out)
 * @param shG Green channel SH L1 coefficients (out)
 * @param shB Blue channel SH L1 coefficients (out)
 */
void shUnpack2Tex(vec4 tex0, vec4 tex1, out vec4 shR, out vec4 shG, out vec4 shB)
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
}

/**
 * Pack with DC-proportional luminance distribution for better color accuracy.
 * Alternative to shPack2Tex with improved reconstruction quality.
 * @param shR Red channel SH coefficients
 * @param shG Green channel SH coefficients
 * @param shB Blue channel SH coefficients
 * @param tex0 Output texture 0
 * @param tex1 Output texture 1
 */
void shPack2TexProportional(vec4 shR, vec4 shG, vec4 shB, out vec4 tex0, out vec4 tex1)
{
    // Texture 0: DC terms + Red Y1
    tex0 = vec4(shR.x, shG.x, shB.x, shR.y);
    
    // Pack Y directions
    tex1.r = shG.y;
    tex1.g = shB.y;
    
    // Pack Z and X directions as luminance-weighted average
    vec3 avgZX = (vec3(shR.z, shG.z, shB.z) + vec3(shR.w, shG.w, shB.w)) * 0.5;
    tex1.b = dot(avgZX, vec3(0.2126, 0.7152, 0.0722));  // Luminance
    tex1.a = 0.0;  // Reserved for future use
}

/**
 * Unpack with DC-proportional distribution for better color accuracy.
 * Distributes luminance proportionally to DC terms.
 * @param tex0 Input texture 0
 * @param tex1 Input texture 1
 * @param shR Red channel SH coefficients (out)
 * @param shG Green channel SH coefficients (out)
 * @param shB Blue channel SH coefficients (out)
 */
void shUnpack2TexProportional(vec4 tex0, vec4 tex1, out vec4 shR, out vec4 shG, out vec4 shB)
{
    // DC and Y coefficients
    shR.x = tex0.r;
    shG.x = tex0.g;
    shB.x = tex0.b;
    
    shR.y = tex0.a;
    shG.y = tex1.r;
    shB.y = tex1.g;
    
    // Approximate Z and X from luminance
    // Distribute luminance proportionally to DC terms
    float lumZX = tex1.b;
    float dcSum = tex0.r + tex0.g + tex0.b;
    
    if (dcSum > 0.001) {
        shR.z = shR.w = lumZX * (tex0.r / dcSum) * 0.5;
        shG.z = shG.w = lumZX * (tex0.g / dcSum) * 0.5;
        shB.z = shB.w = lumZX * (tex0.b / dcSum) * 0.5;
    } else {
        shR.z = shR.w = lumZX * 0.166;  // 1/6 each
        shG.z = shG.w = lumZX * 0.166;
        shB.z = shB.w = lumZX * 0.166;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3-Texture Layout (Full Fidelity)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Pack full SH L1 RGB into 3 RGBA16F textures.
 * No loss of precision - all 12 coefficients stored.
 * @param shR Red channel SH coefficients
 * @param shG Green channel SH coefficients
 * @param shB Blue channel SH coefficients
 * @param tex0 Output texture 0
 * @param tex1 Output texture 1
 * @param tex2 Output texture 2
 */
void shPack3Tex(vec4 shR, vec4 shG, vec4 shB,
                out vec4 tex0, out vec4 tex1, out vec4 tex2)
{
    tex0 = vec4(shR.x, shG.x, shB.x, shR.y);  // DC + R.y
    tex1 = vec4(shG.y, shB.y, shR.z, shG.z);  // G.y, B.y, R.z, G.z
    tex2 = vec4(shB.z, shR.w, shG.w, shB.w);  // B.z, R.w, G.w, B.w
}

/**
 * Unpack full SH L1 RGB from 3 RGBA16F textures.
 * @param tex0 Input texture 0
 * @param tex1 Input texture 1
 * @param tex2 Input texture 2
 * @param shR Red channel SH coefficients (out)
 * @param shG Green channel SH coefficients (out)
 * @param shB Blue channel SH coefficients (out)
 */
void shUnpack3Tex(vec4 tex0, vec4 tex1, vec4 tex2,
                  out vec4 shR, out vec4 shG, out vec4 shB)
{
    shR = vec4(tex0.r, tex0.a, tex1.b, tex2.g);  // R: DC, Y1, Y2, Y3
    shG = vec4(tex0.g, tex1.r, tex1.a, tex2.b);  // G: DC, Y1, Y2, Y3
    shB = vec4(tex0.b, tex1.g, tex2.r, tex2.a);  // B: DC, Y1, Y2, Y3
}

// ═══════════════════════════════════════════════════════════════════════════
// Texture Sampling Helpers
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Sample and unpack SH from 2-texture layout.
 * @param probeRadiance0 Sampler for texture 0
 * @param probeRadiance1 Sampler for texture 1
 * @param probeUV UV coordinates for probe
 * @param shR Red channel SH coefficients (out)
 * @param shG Green channel SH coefficients (out)
 * @param shB Blue channel SH coefficients (out)
 */
void shSample2Tex(sampler2D probeRadiance0, sampler2D probeRadiance1,
                  vec2 probeUV, out vec4 shR, out vec4 shG, out vec4 shB)
{
    vec4 tex0 = texture(probeRadiance0, probeUV);
    vec4 tex1 = texture(probeRadiance1, probeUV);
    shUnpack2Tex(tex0, tex1, shR, shG, shB);
}

/**
 * Sample and unpack SH from 3-texture layout.
 * @param probeRadiance0 Sampler for texture 0
 * @param probeRadiance1 Sampler for texture 1
 * @param probeRadiance2 Sampler for texture 2
 * @param probeUV UV coordinates for probe
 * @param shR Red channel SH coefficients (out)
 * @param shG Green channel SH coefficients (out)
 * @param shB Blue channel SH coefficients (out)
 */
void shSample3Tex(sampler2D probeRadiance0, sampler2D probeRadiance1, sampler2D probeRadiance2,
                  vec2 probeUV, out vec4 shR, out vec4 shG, out vec4 shB)
{
    vec4 tex0 = texture(probeRadiance0, probeUV);
    vec4 tex1 = texture(probeRadiance1, probeUV);
    vec4 tex2 = texture(probeRadiance2, probeUV);
    shUnpack3Tex(tex0, tex1, tex2, shR, shG, shB);
}

// ═══════════════════════════════════════════════════════════════════════════
// Quality Selection Macros
// ═══════════════════════════════════════════════════════════════════════════

// Define LUMON_SH_3TEX to use full 3-texture layout
// Default is 2-texture compressed layout

#ifdef LUMON_SH_3TEX
    #define shPackToStorage shPack3Tex
    #define shUnpackFromStorage shUnpack3Tex
#else
    #define shPackToStorage(shR, shG, shB, tex0, tex1, tex2) shPack2Tex(shR, shG, shB, tex0, tex1)
    #define shUnpackFromStorage(tex0, tex1, tex2, shR, shG, shB) shUnpack2Tex(tex0, tex1, shR, shG, shB)
#endif

#endif // VGE_LUMON_SH_PACK_FSH
