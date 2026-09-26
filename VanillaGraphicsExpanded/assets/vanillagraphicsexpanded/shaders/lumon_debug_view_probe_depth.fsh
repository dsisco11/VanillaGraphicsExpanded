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
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/heatmap.glsl"
@import "./includes/debug/world_to_view_pos.glsl"

/** Implements render probe depth debug for its explicit view entrypoint. */
vec4 renderProbeDepthDebug(vec2 screenPos)
{
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);

    vec4 probeData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = probeData.a;

    if (valid < 0.1)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black for invalid
    }

    // Probe anchors are in world-space; compute view-space depth.
    float probeDepth = -worldToViewPos(probeData.xyz).z;

    // Normalize to reasonable range (0-100m)
    float normalizedDepth = probeDepth / 100.0;

    return vec4(heatmap(normalizedDepth), 1.0);
}

/** Renders only the ProbeDepth view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeDepthDebug(screenPos);
}
