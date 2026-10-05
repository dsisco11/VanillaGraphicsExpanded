#ifndef VGE_WATER_RECEIVER_PUBLICATION_GLSL
#define VGE_WATER_RECEIVER_PUBLICATION_GLSL

/** Validates receiver color metadata and finite values representable in RGBA16F. */
bool VgeWaterReceiverColorValid(vec4 color)
{
    // Finite float32 radiance can overflow the publication's half-float storage.
    return color.a >= .5 && !any(isnan(color)) && !any(isinf(color))
        && all(lessThanEqual(abs(color), vec4(65504.0)));
}

/** Validates the complete receiver pair before publishing depth as physical coverage. */
bool VgeWaterReceiverPairValid(vec4 color, float depth)
{
    // Invalid samples keep depth at the sky sentinel instead of authorizing geometry.
    return !isnan(depth) && !isinf(depth) && depth > 0.0 && depth < .999999
        && VgeWaterReceiverColorValid(color);
}
#endif
