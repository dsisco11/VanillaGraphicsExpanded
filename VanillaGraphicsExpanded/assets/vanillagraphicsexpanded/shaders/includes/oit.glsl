#ifndef VGE_OIT_GLSL
#define VGE_OIT_GLSL
// Bucket OIT uses six draw buffers: multiplicative revealage at 0-1,
// alpha-blended glow at 2, and additive premultiplied accumulation at 3-5.
layout(location = 2) out vec4 outOitGlow;
layout(location = 0) out vec4 outOitRevealBins;
layout(location = 1) out vec4 outRevealage;
layout(location = 3) out vec4 outOitAccumulation0;
layout(location = 4) out vec4 outOitAccumulation1;
layout(location = 5) out vec4 outOitAccumulation2;

const float VGE_OIT_BIN_SCALE = 30.0;

/** Weights a fragment within a transparency depth bin. */
float vgeOitBellCurve(float t)
{
    float n = t / 0.832;
    return exp(-n * n);
}

/** Converts straight-alpha radiance into the engine's bucket accumulation and revealage outputs. */
void writeOit(vec4 color, float glow)
{
    float depth = ((gl_FragCoord.z * 2.0) - 1.0) / gl_FragCoord.w;
    float bin = log((depth / VGE_OIT_BIN_SCALE) + 1.0);
    vec4 weightedColor = vec4(color.rgb * color.a, color.a) * exp(-depth / 3000.0);
    outOitGlow = vec4(glow, 0.0, 0.0, color.a);

    float bin0 = vgeOitBellCurve(bin);
    float bin1 = vgeOitBellCurve(bin - 1.0);
    float bin2 = vgeOitBellCurve(bin - 2.0);
    if (bin > 2.0)
    {
        bin2 = 1.0;
    }

    outOitAccumulation0 = weightedColor * bin0;
    outOitAccumulation1 = weightedColor * bin1;
    outOitAccumulation2 = weightedColor * bin2;
    // Revealage blending is DST_COLOR, ZERO: emit remaining transmission,
    // not opacity. Each fragment multiplies the destination by this value.
    outOitRevealBins = vec4(
        1.0 - color.a * bin0,
        1.0 - color.a * bin1,
        1.0 - color.a * bin2,
        1.0);
    outRevealage = vec4(1.0 - color.a);
}

/** Writes a non-emissive transparent surface. */
void writeOit(vec4 color) { writeOit(color, 0.0); }

#endif
