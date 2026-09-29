#ifndef VGE_ATMOSPHERE_SKY_MAPPING
#define VGE_ATMOSPHERE_SKY_MAPPING
/** Concentrated solar scattering is evaluated at the pixel direction, not interpolated from the LUT. */
float atmMieFactor(vec3 direction, vec3 sun)
{
    const float g = .76;
    float cosine = clamp(dot(direction, sun), -1.0, 1.0);
    return (1.0 - g * g) / (12.56637061436 * pow(1.0 + g * g - 2.0 * g * cosine, 1.5));
}
// Matches AtmosphereSkyMapping: endpoints are poles, the middle coordinate is the depressed horizon.
float atmSkyElevation(float v, float horizon)
{
    float t = 2.0 * clamp(v, 0.0, 1.0) - 1.0;
    return horizon + (t < 0.0 ? -(1.570796326795 + horizon) : 1.570796326795 - horizon) * t * t;
}

// Inverse row coordinate; texture lookup separately applies the half-texel endpoint correction.
float atmSkyCoordinate(float elevation, float horizon)
{
    return elevation < horizon
        ? .5 - .5 * sqrt(clamp((horizon - elevation) / (1.570796326795 + horizon), 0.0, 1.0))
        : .5 + .5 * sqrt(clamp((elevation - horizon) / (1.570796326795 - horizon), 0.0, 1.0));
}

// Exact cell integral for a constant sky; only the upward hemisphere contributes to irradiance.
float atmSkyEnvironmentWeight(int row, int width, int height, float horizon)
{
    float low = max(0.0, atmSkyElevation((float(row) - .5) / float(height - 1), horizon));
    float high = max(0.0, atmSkyElevation((float(row) + .5) / float(height - 1), horizon));
    float a = sin(low), b = sin(high);
    return (b * b - a * a) / float(width);
}
#endif
