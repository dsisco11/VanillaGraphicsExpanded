#version 330 core

vec2 uv;
out vec4 outColor;

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_sh.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"
@import "./includes/velocity_common.glsl"
@import "./includes/lumon_pbr.glsl"
@import "./includes/vge_global_defines.glsl"
@import "./includes/squirrel3.glsl"
@import "./includes/lumon_near_field_scene.glsl"
@import "./includes/lumon_trace_scene_trace.glsl"
@import "./includes/lumon_frame_worldspace_bridge.glsl"
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/trace_scene_status_color.glsl"

/** Implements render debug trace scene for its explicit view entrypoint. */
vec4 RenderDebug_TraceScene(vec2 screenPos)
{
    if (traceSurfaceMin.w == 0) return traceSceneStatusColor(TRACE_SCENE_OUTSIDE);
    vec3 camera = (invViewMatrix * vec4(0.0, 0.0, 0.0, 1.0)).xyz + matrixSpaceWorldBlockOffsetRem;
    ivec3 originCell = ivec3(floor(camera)) + matrixSpaceWorldChunkCoordOffset * 32;
    vec3 local = vec3(originCell - traceSurfaceMin.xyz) + fract(camera);
    vec3 extent = vec3(traceSurfaceMax.xyz - traceSurfaceMin.xyz);
    vec3 view = lumonReconstructViewPos(screenPos / screenSize, 0.5, invProjectionMatrix);
    vec3 direction = normalize((invViewMatrix * vec4(normalize(view), 0.0)).xyz);
    float enter = 0.0, leave = 1e30;
    for (int axis = 0; axis < 3; axis++)
    {
        if (abs(direction[axis]) < 1e-8)
        {
            if (local[axis] < 0.0 || local[axis] >= extent[axis]) return traceSceneStatusColor(TRACE_SCENE_OUTSIDE);
        }
        else
        {
            float a = -local[axis] / direction[axis], b = (extent[axis] - local[axis]) / direction[axis];
            enter = max(enter, min(a, b)); leave = min(leave, max(a, b));
        }
    }
    if (leave <= enter) return traceSceneStatusColor(TRACE_SCENE_OUTSIDE);
    vec3 start = clamp(local + direction * (enter + 0.0001), vec3(0.0), extent - vec3(0.0001));
    LumonTraceSceneHit hit = lumonTraceScene(traceSurfaceMin.xyz + ivec3(floor(start)), fract(start), direction,
        max(leave - enter - 0.0002, 0.000001), 512, TRACE_SCENE_SURFACE, 0);
    if (hit.outcome == LUMON_NEAR_FIELD_HIT) return vec4(vec3(1.0 / (1.0 + (enter + hit.distance) * 0.05)), 1.0);
    if (hit.outcome == LUMON_NEAR_FIELD_CLEAR) return vec4(0.0, 0.0, 0.0, 1.0);
    if (hit.outcome == LUMON_NEAR_FIELD_BUDGET) return vec4(1.0, 1.0, 0.0, 1.0);
    return traceSceneStatusColor(hit.reason);
}

/** Renders only the TraceSceneDdaDistanceL0 view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = RenderDebug_TraceScene(screenPos);
}
