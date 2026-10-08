#ifndef LUMON_DEBUG_TRACE_SCENE_DEBUG_SAMPLE_GLSL
#define LUMON_DEBUG_TRACE_SCENE_DEBUG_SAMPLE_GLSL
@import "./trace_scene_status_color.glsl"
@import "./trace_scene_debug_surface_cell.glsl"

/** Implements trace scene debug sample for its explicit view entrypoint. */
vec4 traceSceneDebugSample(vec2 screenPos, int mode)
{
    float depth = texelFetch(primaryDepth, ivec2(screenPos), 0).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);
    ivec3 cell = traceSceneDebugSurfaceCell(screenPos, depth);
    uint geometry;
    int status = lumonTraceSceneReadGeometry(cell, TRACE_SCENE_SURFACE, geometry);
    if (mode == 55)
    {
        bool inside = traceSurfaceMin.w != 0 && all(greaterThanEqual(cell, traceSurfaceMin.xyz)) && all(lessThan(cell, traceSurfaceMax.xyz));
        return inside ? vec4(0.15, 0.85, 0.15, 1.0) : vec4(0.85, 0.15, 0.15, 1.0);
    }
    if (status != TRACE_SCENE_READY) return traceSceneStatusColor(status);
    if (mode == 56) return vec4(vec3((geometry & 3u) == 2u ? 1.0 : 0.0), 1.0);
    uint payload = texelFetch(traceSceneLegacy, lumonNearFieldWrap(cell, nearFieldOriginResolution.w), 0).r;
    return vec4(float(min(payload & 63u, 32u)) / 32.0, float(min((payload >> 6) & 63u, 32u)) / 32.0,
        float((payload >> 12) & 63u) / 63.0, 1.0);
}
#endif
