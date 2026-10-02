#ifndef VGE_WATER_MEDIUM_GLSL
#define VGE_WATER_MEDIUM_GLSL

/** Homogeneous medium in inverse metres, with dimensionless Henyey-Greenstein anisotropy. */
struct VgeWaterMedium
{
    vec3 absorption;
    vec3 scattering;
    float anisotropy;
};

/** Bounded directional scattering, normalized over solid angle; cosine compares incoming and outgoing photon directions. */
float VgeWaterPhase(float cosine, float anisotropy)
{
    float g = clamp(anisotropy, -.95, .95);
    float denominator = max(1.0 + g * g - 2.0 * g * clamp(cosine, -1.0, 1.0), .0025);
    return (1.0 - g * g) / (12.56637061436 * denominator * sqrt(denominator));
}

/** Evaluates Beer-Lambert transmission for a supplied submerged path length in metres, independent of the ray-selection policy. */
vec3 VgeWaterTransmittance(VgeWaterMedium medium, float lengthMetres)
{
    return exp(-max(medium.absorption + medium.scattering, vec3(0)) * max(lengthMetres, 0.0));
}

/** Integrates a constant single-scattering source analytically, retaining RGB rather than scalar optical depth. */
vec3 VgeWaterInScattering(VgeWaterMedium medium, float lengthMetres, vec3 sourceRadiance)
{
    vec3 extinction = max(medium.absorption + medium.scattering, vec3(0));
    vec3 opticalDepth = extinction * max(lengthMetres, 0.0);
    // The series avoids cancellation for optically thin paths and includes the zero-extinction limit.
    vec3 integral;
    for (int channel = 0; channel < 3; ++channel)
        integral[channel] = opticalDepth[channel] < .001
            ? max(lengthMetres, 0.0) * (1.0 - .5 * opticalDepth[channel] + opticalDepth[channel] * opticalDepth[channel] / 6.0)
            : (1.0 - exp(-opticalDepth[channel])) / extinction[channel];
    return max(sourceRadiance, vec3(0)) * max(medium.scattering, vec3(0)) * integral;
}
#endif
