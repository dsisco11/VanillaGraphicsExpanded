#ifndef VGE_WATER_RECEIVER_PUBLICATION_GLSL
#define VGE_WATER_RECEIVER_PUBLICATION_GLSL

/** Validates receiver metadata and finite values before storing the RGBA16F/R32F pair. */
bool VgeWaterReceiverPairValid(vec4 color, float depth)
{
    // Finite float32 radiance can overflow the publication's half-float storage.
    // Invalid samples keep depth at the sky sentinel instead of authorizing geometry.
    return !isnan(depth) && !isinf(depth) && depth > 0.0 && depth < .999999
        && color.a >= .5 && !any(isnan(color)) && !any(isinf(color))
        && all(lessThanEqual(abs(color), vec4(65504.0)));
}
#endif
