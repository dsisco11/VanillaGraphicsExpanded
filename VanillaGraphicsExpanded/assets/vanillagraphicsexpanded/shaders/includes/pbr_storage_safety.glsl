#ifndef VGE_STORAGE_SAFETY_GLSL
#define VGE_STORAGE_SAFETY_GLSL
const float VGE_STORAGE_HALF_MAX = 65504.0;
const float VGE_STORAGE_R11_MAX = 65024.0;
const float VGE_STORAGE_B10_MAX = 64512.0;
const float VGE_STORAGE_FLOAT_MAX = 3.402823466e38;
/** Contains exceptional scaled RGB at a destination boundary while preserving invalidity and reporting clipping. */
bool VgeProtectLightingStorage(vec3 stored, float lower, float upper, out vec3 protectedValue, out bool clipped)
{
    protectedValue = vec3(0);
    clipped = false;
    if (isnan(lower) || isnan(upper) || isinf(lower) || isinf(upper) || lower > 0.0 || upper < 0.0 || lower > upper)
        return false;
    bool valid = true;
    // Explicit branches avoid driver-dependent NaN min/max semantics. A finite neutral payload
    // never authorizes cache/history publication when this function reports invalidity.
    for (int channel = 0; channel < 3; ++channel)
    {
        float value = stored[channel];
        if (isnan(value)) { valid = false; clipped = true; continue; }
        if (isinf(value)) { valid = false; protectedValue[channel] = value > 0.0 ? upper : lower; clipped = true; continue; }
        protectedValue[channel] = clamp(value, lower, upper);
        clipped = clipped || protectedValue[channel] != value;
    }
    return valid;
}
#endif
