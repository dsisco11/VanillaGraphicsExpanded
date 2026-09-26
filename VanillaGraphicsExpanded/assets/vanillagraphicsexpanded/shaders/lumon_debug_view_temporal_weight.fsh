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

/** Implements render temporal weight debug for its explicit view entrypoint. */
vec4 renderTemporalWeightDebug(vec2 screenPos)
{
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);

    vec4 posData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = posData.a;

    if (valid < 0.1)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black for invalid probes
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
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black = out of bounds
    }

    // Sample history metadata
    // Layout (matches lumon_temporal.fsh):
    // R = linearDepth, G = normal.x encoded, B = normal.y encoded, A = accumCount
    vec4 histMeta = texture(historyMeta, historyUV);
    float historyDepthLin = histMeta.r;
    vec2 historyNormal2D = histMeta.gb * 2.0 - 1.0;

    if (historyDepthLin < 0.001)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);  // No valid history
    }

    // Compute validation confidence
    float depthDiff = abs(currentDepthLin - historyDepthLin) / max(currentDepthLin, 0.001);
    vec3 historyNormal = reconstructHistoryNormal(historyNormal2D, normalVS);
    float normalDot = dot(normalize(normalVS), historyNormal);

    if (depthDiff > depthRejectThreshold || normalDot < normalRejectThreshold)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Rejected
    }

    float depthConf = 1.0 - (depthDiff / depthRejectThreshold);
    float normalConf = (normalDot - normalRejectThreshold) / (1.0 - normalRejectThreshold);
    float confidence = clamp(min(depthConf, normalConf), 0.0, 1.0);

    float weight = temporalAlpha * confidence;
    if (valid < 0.9) weight *= 0.5;  // Edge probe penalty

    // Match temporal ramp-up: early frames use less history
    float prevAccum = histMeta.a;
    weight *= min(prevAccum / 10.0, 1.0);

    // Grayscale: brighter = more history used
    return vec4(weight, weight, weight, 1.0);
}

/** Renders only the TemporalWeight view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderTemporalWeightDebug(screenPos);
}
