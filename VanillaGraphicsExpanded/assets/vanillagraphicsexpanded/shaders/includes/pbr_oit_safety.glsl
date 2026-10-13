#ifndef VGE_OIT_SAFETY_GLSL
#define VGE_OIT_SAFETY_GLSL

/** Checks coverage and the actual rounded log input before bucket weighting. */
bool VgeOitInputsValid(vec4 color, float glow, float depth)
{
    if (any(isnan(color)) || any(isinf(color)) || color.a < 0.0 || color.a > 1.0
        || isnan(glow) || isinf(glow) || isnan(depth) || isinf(depth)) return false;
    // The engine scales depth first. Checking depth > -30 alone misses values
    // whose rounded addition is zero; the exponential also uses this scaled depth.
    float scaledDepth = depth / 30.0;
    float logInput = scaledDepth + 1.0;
    return !isnan(logInput) && !isinf(logInput) && logInput > 0.0;
}

#endif
