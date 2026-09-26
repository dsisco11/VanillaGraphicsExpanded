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
@import "./includes/debug/get_view_matrix.glsl"
@import "./includes/debug/world_to_view_pos.glsl"
@import "./includes/debug/reconstruct_history_normal.glsl"
@import "./includes/debug/reproject_to_history.glsl"

/** Implements render temporal rejection debug for its explicit view entrypoint. */
vec4 renderTemporalRejectionDebug(vec2 screenPos)
{
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);

    vec4 posData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = posData.a;

    if (valid < 0.1)
    {
        return vec4(0.2, 0.2, 0.2, 1.0);  // Dark gray for invalid probes
    }

    vec3 posWS = posData.xyz;
    vec3 posVS = worldToViewPos(posWS);
    vec3 normalWS = lumonDecodeNormal(texelFetch(probeAnchorNormal, probeCoord, 0).xyz);
    vec3 normalVS = normalize(mat3(getViewMatrix()) * normalWS);
    float currentDepthLin = -posVS.z;

    // Reproject to history UV
    vec2 historyUV = reprojectToHistory(posWS);

    // Check bounds
    if (historyUV.x < 0.0 || historyUV.x > 1.0 ||
        historyUV.y < 0.0 || historyUV.y > 1.0)
    {
        return vec4(1.0, 0.0, 0.0, 1.0);  // Red = out of bounds
    }

    // Sample history metadata
    vec4 histMeta = texture(historyMeta, historyUV);
    float historyDepthLin = histMeta.r;
    vec2 historyNormal2D = histMeta.gb * 2.0 - 1.0;

    if (historyDepthLin < 0.001)
    {
        return vec4(0.5, 0.0, 0.5, 1.0);  // Purple = no history data
    }

    // Check depth rejection
    float depthDiff = abs(currentDepthLin - historyDepthLin) / max(currentDepthLin, 0.001);
    if (depthDiff > depthRejectThreshold)
    {
        return vec4(1.0, 1.0, 0.0, 1.0);  // Yellow = depth reject
    }

    // Check normal rejection
    vec3 historyNormal = reconstructHistoryNormal(historyNormal2D, normalVS);
    float normalDot = dot(normalize(normalVS), historyNormal);
    if (normalDot < normalRejectThreshold)
    {
        return vec4(1.0, 0.5, 0.0, 1.0);  // Orange = normal reject
    }

    // Valid history
    return vec4(0.0, 1.0, 0.0, 1.0);  // Green = valid
}

/** Renders only the TemporalRejection view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderTemporalRejectionDebug(screenPos);
}
