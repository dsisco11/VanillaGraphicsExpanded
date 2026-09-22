#ifndef LUMON_DEBUG_SCENES_OVERVIEW_GLSL
#define LUMON_DEBUG_SCENES_OVERVIEW_GLSL
vec3 VgeHashColorU(uint key)
{
    uint h = Squirrel3HashU(key);
    vec3 c = vec3(
        float((h) & 255U) / 255.0,
        float((h >> 8U) & 255U) / 255.0,
        float((h >> 16U) & 255U) / 255.0);

    // Snap to visible bands (reduces noisy gradients).
    return floor(c * 6.0 + 0.5) / 6.0;
}

/** Compares surface lighting with shared geometry classification and material identity. */
vec4 RenderDebug_LumOnScenesOverview(vec2 screenPos)
{
    float x = screenPos.x / screenSize.x;
    if (abs(x - 1.0/3.0) < 1.0/screenSize.x || abs(x - 2.0/3.0) < 1.0/screenSize.x) return vec4(0.0, 0.0, 0.0, 1.0);
    if (x < 1.0/3.0) return renderLumonSceneIrradianceDebug();
    if (x < 2.0/3.0) return traceSceneDebugSample(screenPos, 56);
    float depth = texelFetch(primaryDepth, ivec2(screenPos), 0).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);
    uint geometry;
    int status = lumonTraceSceneReadGeometry(traceSceneDebugSurfaceCell(screenPos, depth), TRACE_SCENE_SURFACE, geometry);
    if (status != TRACE_SCENE_READY) return traceSceneStatusColor(status);
    return vec4((geometry & 3u) == 1u ? vec3(0.0) : VgeHashColorU(geometry >> 2), 1.0);
}
#endif