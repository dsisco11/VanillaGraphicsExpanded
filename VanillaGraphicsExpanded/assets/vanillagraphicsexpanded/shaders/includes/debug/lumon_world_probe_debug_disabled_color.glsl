#ifndef LUMON_DEBUG_LUMON_WORLD_PROBE_DEBUG_DISABLED_COLOR_GLSL
#define LUMON_DEBUG_LUMON_WORLD_PROBE_DEBUG_DISABLED_COLOR_GLSL


/** Implements lumon world probe debug disabled color for its explicit view entrypoint. */
vec4 lumonWorldProbeDebugDisabledColor()
{
    // Visual cue that the world-probe debug path is compile-time disabled (vs just "no data in bounds").
    float v = 0.5 + 0.5 * sin(uv.x * 80.0) * sin(uv.y * 80.0);
    vec3 a = vec3(0.15, 0.0, 0.2);
    vec3 b = vec3(0.55, 0.0, 0.7);
    return vec4(mix(a, b, v), 1.0);
}
#endif
