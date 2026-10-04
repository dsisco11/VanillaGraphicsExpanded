#ifndef VGE_WATER_MEDIUM_GLSL
#define VGE_WATER_MEDIUM_GLSL

/** Homogeneous medium in inverse metres, with dimensionless Henyey-Greenstein anisotropy. */
struct VgeWaterMedium
{
    vec3 absorption;
    vec3 scattering;
    float anisotropy;
};

/** Shared homogeneous-path evaluation; lengths are metres and optical depth is dimensionless RGB. */
struct VgeWaterPath
{
    float lengthMetres;
    vec3 extinction;
    vec3 opticalDepth;
    vec3 transmittance;
};

/** Evaluates the common attenuation terms once for transmission and source integration. */
VgeWaterPath VgeWaterEvaluatePath(VgeWaterMedium medium, float lengthMetres)
{
    VgeWaterPath path;
    path.lengthMetres = max(lengthMetres, 0.0);
    path.extinction = max(medium.absorption + medium.scattering, vec3(0));
    path.opticalDepth = path.extinction * path.lengthMetres;
    path.transmittance = exp(-path.opticalDepth);
    return path;
}

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

/** Integrates one channel, preserving cancellation resistance and the zero-extinction limit. */
float VgeWaterChannelIntegral(float lengthMetres, float extinction, float opticalDepth, float transmittance)
{
    return opticalDepth < .001
        ? lengthMetres * (1.0 - .5 * opticalDepth + opticalDepth * opticalDepth / 6.0)
        : (1.0 - transmittance) / extinction;
}

/** Integrates a constant single-scattering source analytically, retaining RGB rather than scalar optical depth. */
vec3 VgeWaterInScattering(VgeWaterMedium medium, VgeWaterPath path, vec3 sourceRadiance)
{
    // Clear media still attenuate the background, but need no scattering integral.
    if (!any(greaterThan(medium.scattering, vec3(0)))) return vec3(0);
    // Fixed RGB components avoid dynamic vector indexing and temporary arrays in the compiled shader.
    // Each channel independently selects its thin-path series before any extinction division.
    vec3 integral = vec3(
        VgeWaterChannelIntegral(path.lengthMetres, path.extinction.r, path.opticalDepth.r, path.transmittance.r),
        VgeWaterChannelIntegral(path.lengthMetres, path.extinction.g, path.opticalDepth.g, path.transmittance.g),
        VgeWaterChannelIntegral(path.lengthMetres, path.extinction.b, path.opticalDepth.b, path.transmittance.b));
    return max(sourceRadiance, vec3(0)) * max(medium.scattering, vec3(0)) * integral;
}
#endif
