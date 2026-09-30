#ifndef VGE_PBR_LIQUID_OPTICS_GLSL
#define VGE_PBR_LIQUID_OPTICS_GLSL
/** Converts hardware depth to positive view distance in blocks. */
float VgeLiquidViewDepth(float depth)
{
    return 2.0 * zNear * zFar / max(zFar + zNear - (depth * 2.0 - 1.0) * (zFar - zNear), 0.0001);
}

/** Dielectric interface reflection, including total internal reflection for an underwater camera. */
float VgeLiquidFresnel(float cosine, bool underwater)
{
    float eta = underwater ? 1.333 : 1.0 / 1.333;
    float sinSquared = eta * eta * max(0.0, 1.0 - cosine * cosine);
    if (sinSquared >= 1.0) return 1.0;
    float transmittedCosine = sqrt(max(0.0, 1.0 - sinSquared));
    float rs = (eta * cosine - transmittedCosine) / max(eta * cosine + transmittedCosine, 0.0001);
    float rp = (cosine - eta * transmittedCosine) / max(cosine + eta * transmittedCosine, 0.0001);
    return clamp(0.5 * (rs * rs + rp * rp), 0.0, 1.0);
}

/** Bounded opaque-depth thickness; missing sky depth uses a finite deep-water fallback. */
float VgeLiquidThickness(float depth, float surfaceDepth, vec3 viewPosition, bool underwater)
{
    if (underwater) return 0.0; // Camera-to-surface transport belongs to the separate water-volume task.
    if (depth >= .999999) return 16.0;
    float rayScale = length(viewPosition) / max(abs(viewPosition.z), .001);
    return clamp((VgeLiquidViewDepth(depth) - VgeLiquidViewDepth(surfaceDepth)) * rayScale, 0.0, 32.0);
}

#endif
