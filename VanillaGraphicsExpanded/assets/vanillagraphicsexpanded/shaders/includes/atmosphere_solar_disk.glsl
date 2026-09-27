#ifndef VGE_ATMOSPHERE_SOLAR_DISK
#define VGE_ATMOSPHERE_SOLAR_DISK
// Match AtmosphereSolarDisk: 0.5357 degree diameter, constant radiance over the visible segment.
const float atmSunRadius = .004675;

// Series for a thin segment of height h; avoids catastrophic acos/chord cancellation.
float atmSunSmallSegment(float h)
{
    return (4.0 * sqrt(2.0) / (3.0 * 3.14159265359)) * h * sqrt(h)
        * (1.0 - h * (3.0 / 20.0 + h * (3.0 / 224.0 + h / 384.0)));
}

// Analytic circular segment avoids discrete ray visibility jumps at sunrise and sunset.
float atmSunVisibility(float elevation, float horizon)
{
    float q = clamp((elevation - horizon) / atmSunRadius, -1.0, 1.0);
    if (q < -.9) return atmSunSmallSegment(1.0 + q);
    if (q > .9) return 1.0 - atmSunSmallSegment(1.0 - q);
    return (acos(-q) + q * sqrt(max(0.0, 1.0 - q * q))) / 3.14159265359;
}

// First area moment of the visible circular segment, relative to the centre elevation.
float atmSunVisibleElevation(float elevation, float horizon, float visible)
{
    float q = clamp((elevation - horizon) / atmSunRadius, -1.0, 1.0);
    // Factoring retains the thin chord without a fused/unfused multiply-subtract discrepancy.
    float chord = max(0.0, (1.0 - q) * (1.0 + q));
    return elevation + atmSunRadius * (2.0 / (3.0 * 3.14159265359)) * chord * sqrt(chord) / visible;
}
#endif
