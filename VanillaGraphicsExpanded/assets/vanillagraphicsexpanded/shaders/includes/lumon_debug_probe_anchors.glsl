// Debug Mode 1-3: Probe Anchor / Grid debug views

vec4 renderProbeGridDebug(vec2 screenPos)
{
    // Sample the scene as background
    float depth = texture(primaryDepth, uv).r;
    vec3 baseColor = vec3(0.1);

    if (!lumonIsSky(depth))
    {
        // Show darkened scene as background
        vec3 normal = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);
        baseColor = normal * 0.3 + 0.2;
    }

    // Calculate which probe cell this pixel is in
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));

    // Calculate the center of this probe cell in screen space
    vec2 probeCenter = (vec2(probeCoord) + 0.5) * float(probeSpacing);

    // Distance from pixel to probe center
    float dist = length(screenPos - probeCenter);

    // Probe dot radius
    float dotRadius = max(3.0, float(probeSpacing) * 0.25);

    // Draw probe dots
    if (dist < dotRadius)
    {
        // Clamp probe coord to valid range
        if (probeCoord.x >= 0 && probeCoord.y >= 0 &&
            probeCoord.x < int(probeGridSize.x) && probeCoord.y < int(probeGridSize.y))
        {
            // Sample probe validity from anchor texture
            vec4 probeData = texelFetch(probeAnchorPosition, probeCoord, 0);
            float valid = probeData.a;

            // Color by validity
            vec3 probeColor;
            if (valid > 0.9)
            {
                probeColor = vec3(0.0, 1.0, 0.0);  // Green = fully valid
            }
            else if (valid > 0.4)
            {
                probeColor = vec3(1.0, 1.0, 0.0);  // Yellow = edge (partial validity)
            }
            else
            {
                probeColor = vec3(1.0, 0.0, 0.0);  // Red = invalid
            }

            // Smooth edge falloff
            float alpha = smoothstep(dotRadius, dotRadius * 0.5, dist);

            return vec4(mix(baseColor, probeColor, alpha), 1.0);
        }
    }

    // Draw grid lines between probes
    vec2 gridPos = mod(screenPos, float(probeSpacing));
    float lineWidth = 1.0;
    if (gridPos.x < lineWidth || gridPos.y < lineWidth)
    {
        return vec4(mix(baseColor, vec3(0.5), 0.4), 1.0);
    }

    return vec4(baseColor, 1.0);
}

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

vec4 renderProbeNormalDebug(vec2 screenPos)
{
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);

    vec4 posData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = posData.a;

    if (valid < 0.1)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black for invalid
    }

    // Decode normal from [0,1] to [-1,1], then re-encode for visualization
    vec3 probeNormalEncoded = texelFetch(probeAnchorNormal, probeCoord, 0).xyz;
    vec3 probeNormalDecoded = lumonDecodeNormal(probeNormalEncoded);
    // Display as color: remap [-1,1] to [0,1] so all directions are visible
    return vec4(probeNormalDecoded * 0.5 + 0.5, 1.0);
}

// Program entry: ProbeAnchors
vec4 RenderDebug_ProbeAnchors(vec2 screenPos)
{
    switch (debugMode)
    {
        case 1: return renderProbeGridDebug(screenPos);
        case 2: return renderProbeDepthDebug(screenPos);
        case 3: return renderProbeNormalDebug(screenPos);
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
