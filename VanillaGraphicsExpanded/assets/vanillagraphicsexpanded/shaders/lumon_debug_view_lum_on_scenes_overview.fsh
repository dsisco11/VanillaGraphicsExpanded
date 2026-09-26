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
@import "./includes/lumonscene_surface_cache.glsl"
@import "./includes/lumonscene_material_packing.glsl"
@import "./includes/lumon_near_field_scene.glsl"
@import "./includes/lumon_trace_scene_trace.glsl"
@import "./includes/lumon_frame_worldspace_bridge.glsl"
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/render_lumon_scene_irradiance_debug.glsl"
@import "./includes/debug/trace_scene_status_color.glsl"
@import "./includes/debug/trace_scene_debug_surface_cell.glsl"
@import "./includes/debug/trace_scene_debug_sample.glsl"

/** Implements vge hash color u for its explicit view entrypoint. */
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

/** Implements render debug lum on scenes overview for its explicit view entrypoint. */
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

/** Renders only the LumOnScenesOverview view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = RenderDebug_LumOnScenesOverview(screenPos);
}
