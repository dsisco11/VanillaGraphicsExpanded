#ifndef LUMON_DEBUG_TRACE_SCENE_STATUS_COLOR_GLSL
#define LUMON_DEBUG_TRACE_SCENE_STATUS_COLOR_GLSL


/** Implements trace scene status color for its explicit view entrypoint. */
vec4 traceSceneStatusColor(int status)
{
    if (status == TRACE_SCENE_OUTSIDE) return vec4(0.0, 0.2, 0.8, 1.0);
    if (status == TRACE_SCENE_UNSUPPORTED) return vec4(1.0, 0.0, 0.0, 1.0);
    return vec4(0.5, 0.0, 1.0, 1.0);
}
#endif
