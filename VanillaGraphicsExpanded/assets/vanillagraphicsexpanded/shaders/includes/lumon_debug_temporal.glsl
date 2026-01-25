// Debug Mode 6-7: Temporal debug views

vec3 reconstructHistoryNormal(vec2 historyNormal2D, vec3 currentNormal)
{
    float z2 = max(1.0 - dot(historyNormal2D, historyNormal2D), 0.0);
    float z = sqrt(z2);
    float zSign = (currentNormal.z >= 0.0) ? 1.0 : -1.0;
    return normalize(vec3(historyNormal2D, z * zSign));
}

/// Reproject world-space position to previous frame UV
vec2 reprojectToHistory(vec3 posWS)
{
    vec4 prevClip = prevViewProjMatrix * vec4(posWS, 1.0);
    vec3 prevNDC = prevClip.xyz / prevClip.w;
    return prevNDC.xy * 0.5 + 0.5;
}

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

// Program entry: Temporal
vec4 RenderDebug_Temporal(vec2 screenPos)
{
    switch (debugMode)
    {
        case 6: return renderTemporalWeightDebug(screenPos);
        case 7: return renderTemporalRejectionDebug(screenPos);
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
