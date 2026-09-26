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
@import "./includes/lumon_worldprobe.glsl"
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/lumon_world_probe_debug_disabled_color.glsl"
@import "./includes/debug/lumon_world_probe_debug_nearest.glsl"

/** Implements render world probe flags heatmap debug for its explicit view entrypoint. */
vec4 renderWorldProbeFlagsHeatmapDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#endif

    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;

    int level;
    ivec2 ac;
    if (!lumonWorldProbeDebugNearest(posWS, level, ac))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // R=stale/dirty, G=in-flight, B=valid.
    vec3 color = texelFetch(worldProbeDebugState0, ac, 0).rgb;
    return vec4(color, 1.0);
}

/** Renders only the WorldProbeMetaFlagsHeatmap view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderWorldProbeFlagsHeatmapDebug();
}
