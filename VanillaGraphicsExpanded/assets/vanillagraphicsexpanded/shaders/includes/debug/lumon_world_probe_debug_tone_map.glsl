#ifndef LUMON_DEBUG_LUMON_WORLD_PROBE_DEBUG_TONE_MAP_GLSL
#define LUMON_DEBUG_LUMON_WORLD_PROBE_DEBUG_TONE_MAP_GLSL


/** Implements lumon world probe debug tone map for its explicit view entrypoint. */
vec3 lumonWorldProbeDebugToneMap(vec3 hdr)
{
    hdr = max(hdr, vec3(0.0));
    return hdr / (hdr + vec3(1.0));
}
#endif
