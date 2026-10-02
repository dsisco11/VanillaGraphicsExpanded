#ifndef VGE_WATER_BOUNDARY_TRANSPORT_GLSL
#define VGE_WATER_BOUNDARY_TRANSPORT_GLSL

/** Validates signed boundary sums and integrates an extinction-weighted constant source approximation. */
bool VgeWaterBoundaryTransport(vec4 boundaries, vec4 source, vec3 cameraExtinction,
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
        || abs(terminal - round(terminal)) > .01 || terminal < -.01 || terminal > 1.01
        || waterLength < -.01 || waterLength > receiverDistance + .01 || any(lessThan(opticalDepth, vec3(-.01)))) return false;
    waterLength = clamp(waterLength, 0.0, receiverDistance);
    opticalDepth = max(opticalDepth, vec3(0));
    transmission = exp(-opticalDepth);
    for (int channel = 0; channel < 3; channel++)
    {
        float tau = opticalDepth[channel];
        float integral = tau < .001 ? 1.0 - .5 * tau + tau * tau / 6.0 : (1.0 - transmission[channel]) / tau;
        inScattering[channel] = max(source[channel], 0.0) * integral;
    }
    return true;
}
#endif
