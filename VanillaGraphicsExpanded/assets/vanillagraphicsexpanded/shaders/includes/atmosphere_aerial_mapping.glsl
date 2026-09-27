#ifndef VGE_ATMOSPHERE_AERIAL_MAPPING
#define VGE_ATMOSPHERE_AERIAL_MAPPING
const int vgeAerialDepth = 24;

// Exact zero and boundary slices with logarithmic spacing resolving metre-scale receivers.
float vgeAerialDistance(int slice, float boundary)
{
    // All directions use the same metre distances: trilinear interpolation must not mix
    // an atmosphere-exit ray with a neighbouring ground ray at a different physical range.
    return min(boundary, .001 * (exp(log(1.0 + 2500.0 / .001) * float(slice) / float(vgeAerialDepth - 1)) - 1.0));
}
#endif
