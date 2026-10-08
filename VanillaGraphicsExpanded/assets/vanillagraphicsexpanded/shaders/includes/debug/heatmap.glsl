#ifndef LUMON_DEBUG_HEATMAP_GLSL
#define LUMON_DEBUG_HEATMAP_GLSL


/** Implements heatmap for its explicit view entrypoint. */
vec3 heatmap(float t)
{
    // Blue -> Cyan -> Green -> Yellow -> Red
    t = clamp(t, 0.0, 1.0);
    vec3 c;
    if (t < 0.25)
    {
        c = mix(vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 1.0), t * 4.0);
    }
    else if (t < 0.5)
    {
        c = mix(vec3(0.0, 1.0, 1.0), vec3(0.0, 1.0, 0.0), (t - 0.25) * 4.0);
    }
    else if (t < 0.75)
    {
        c = mix(vec3(0.0, 1.0, 0.0), vec3(1.0, 1.0, 0.0), (t - 0.5) * 4.0);
    }
    else
    {
        c = mix(vec3(1.0, 1.0, 0.0), vec3(1.0, 0.0, 0.0), (t - 0.75) * 4.0);
    }
    return c;
}
#endif
