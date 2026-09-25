#ifndef LUMON_DEBUG_TRACESCENE_GLSL
#define LUMON_DEBUG_TRACESCENE_GLSL
@import "./lumon_common.glsl"
@import "./lumon_trace_scene_trace.glsl"
@import "./lumon_frame_worldspace_bridge.glsl"

/** Keeps unknown publication, unsupported shapes and out-of-domain samples distinct from known air. */
vec4 traceSceneStatusColor(int status)
{
    if (status == TRACE_SCENE_OUTSIDE) return vec4(0.0, 0.2, 0.8, 1.0);
    if (status == TRACE_SCENE_UNSUPPORTED) return vec4(1.0, 0.0, 0.0, 1.0);
    return vec4(0.5, 0.0, 1.0, 1.0);
}

/** Reconstructs the solid side of a visible surface without converting absolute world coordinates to float. */
ivec3 traceSceneDebugSurfaceCell(vec2 screenPos, float depth)
{
    vec3 view = lumonReconstructViewPos(screenPos / screenSize, depth, invProjectionMatrix);
    vec3 relative = (invViewMatrix * vec4(view, 1.0)).xyz;
    uvec4 surfacePatch = texelFetch(gBufferPatchId, ivec2(screenPos), 0);
    vec3 normal = texelFetch(gBufferNormal, ivec2(screenPos), 0).xyz * 2.0 - 1.0;
    if (surfacePatch.y != 0u)
    {
        uint axis = (surfacePatch.y - 1u) % 6u;
        normal = vec3(0.0); normal[int(axis / 2u)] = (axis % 2u) == 0u ? 1.0 : -1.0;
    }
    if (dot(normal, normal) > 1e-6) relative -= normalize(normal) * 0.51;
    return LumonFrameMatrixSpacePosToWorldCell(relative);
}

/** Reads logical surface coverage and coherent payloads from the same storage used by tracing. */
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

/** Visualizes bounded surface-domain traversal using exact integer origin arithmetic. */
vec4 RenderDebug_TraceScene(vec2 screenPos)
{
    if (debugMode != 67) return traceSceneDebugSample(screenPos, debugMode);
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
#endif
