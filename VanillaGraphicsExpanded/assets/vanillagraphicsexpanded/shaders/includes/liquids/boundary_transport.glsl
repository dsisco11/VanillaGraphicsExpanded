#ifndef VGE_WATER_BOUNDARY_TRANSPORT_GLSL
#define VGE_WATER_BOUNDARY_TRANSPORT_GLSL

/** Validates signed boundary sums and integrates an extinction-weighted constant source approximation. */
bool VgeWaterBoundaryTransport(vec4 boundaries, vec4 source, vec3 cameraExtinction, vec3 sourceUpper,
    bool startsInWater, float receiverDistance, out vec3 transmission, out vec3 inScattering, out float waterLength)
{
    float initial = startsInWater ? 1.0 : 0.0;
    float terminal = initial + source.w;
    waterLength = boundaries.w + initial * receiverDistance;
    vec3 opticalDepth = boundaries.rgb + cameraExtinction * receiverDistance;
    transmission = vec3(1);
    inScattering = vec3(0);
    if (isnan(receiverDistance) || isinf(receiverDistance) || receiverDistance < 0.0) return false;
    // Invalid winding or negative/overlong segments are unresolved, not an invented deep-water path.
    if (any(isnan(boundaries)) || any(isinf(boundaries)) || any(isnan(source)) || any(isinf(source))
        || any(isnan(cameraExtinction)) || any(isinf(cameraExtinction))
        || any(lessThan(cameraExtinction, vec3(0)))
        || any(isnan(sourceUpper)) || any(isinf(sourceUpper)) || any(lessThan(sourceUpper, vec3(0)))
        || any(isnan(opticalDepth)) || any(isinf(opticalDepth)) || isnan(waterLength) || isinf(waterLength)
        || abs(terminal - round(terminal)) > .01 || terminal < -.01 || terminal > 1.01
        || waterLength < -.01 || waterLength > receiverDistance + .01 || any(lessThan(opticalDepth, vec3(-.01)))) return false;
    waterLength = clamp(waterLength, 0.0, receiverDistance);
    opticalDepth = max(opticalDepth, vec3(0));
    vec3 sourceLimit = sourceUpper * opticalDepth;
    if (any(isnan(sourceLimit)) || any(isinf(sourceLimit))) return false;
    transmission = exp(-opticalDepth);
    for (int channel = 0; channel < 3; channel++)
    {
        float tau = opticalDepth[channel];
        float integral = tau < .001 ? 1.0 - .5 * tau + tau * tau / 6.0 : (1.0 - transmission[channel]) / tau;
        // The signed antiderivative can overestimate heterogeneous lighting. Bound only that
        // excess, preserving the existing homogeneous response and thin/thick integral factor.
        inScattering[channel] = min(max(source[channel], 0.0), sourceLimit[channel]) * integral;
    }
    return !any(isnan(inScattering)) && !any(isinf(inScattering));
}
#endif
