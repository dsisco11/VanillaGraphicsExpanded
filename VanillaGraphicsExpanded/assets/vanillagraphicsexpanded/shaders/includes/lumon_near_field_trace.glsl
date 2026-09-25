#ifndef LUMON_NEAR_FIELD_GLSL
#define LUMON_NEAR_FIELD_GLSL
@import "./lumon_trace_scene_trace.glsl"
#define LumonNearFieldHit LumonTraceSceneHit
/** Applies the near-field consumer's domain and step budget to shared traversal. */
LumonNearFieldHit lumonTraceNearField(ivec3 startCell, vec3 fraction, vec3 direction, float maxDistance)
{
    return lumonTraceScene(startCell, fraction, direction, maxDistance, nearFieldBudget.x, TRACE_SCENE_NEAR, 0);
}
#endif
