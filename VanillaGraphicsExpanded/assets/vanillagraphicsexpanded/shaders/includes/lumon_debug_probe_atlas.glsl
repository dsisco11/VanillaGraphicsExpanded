// Debug modes 12-17, 45-48: Screen-probe atlas debug views

vec4 renderProbeAtlasMetaConfidenceDebug()
{
    float conf = texture(probeAtlasMeta, uv).r;
    return vec4(vec3(clamp(conf, 0.0, 1.0)), 1.0);
}

vec4 renderProbeAtlasTemporalAlphaDebug()
{
    float confHist = texture(probeAtlasMeta, uv).r;
    float alphaEff = clamp(temporalAlpha * clamp(confHist, 0.0, 1.0), 0.0, 1.0);
    return vec4(vec3(alphaEff), 1.0);
}

vec4 renderProbeAtlasMetaFlagsDebug()
{
    float conf;
    uint flags;
    lumonDecodeMeta(texture(probeAtlasMeta, uv).rg, conf, flags);

    float hit = (flags & LUMON_META_HIT) != 0u ? 1.0 : 0.0;
    float sky = (flags & LUMON_META_SKY_MISS) != 0u ? 1.0 : 0.0;
    float world = (flags & LUMON_META_WORLDPROBE_FALLBACK) != 0u ? 1.0 : 0.0;

    // Encode additional bits as brightness boost so they pop without hiding base flags.
    float exit = (flags & LUMON_META_SCREEN_EXIT) != 0u ? 0.25 : 0.0;
    float early = (flags & LUMON_META_EARLY_TERMINATED) != 0u ? 0.25 : 0.0;
    float thick = (flags & LUMON_META_THICKNESS_UNCERT) != 0u ? 0.25 : 0.0;

    vec3 base = vec3(hit, sky, world);
    base = clamp(base + vec3(exit + early + thick), 0.0, 1.0);
    return vec4(base, 1.0);
}

vec4 renderProbeAtlasFilteredRadianceDebug()
{
    vec3 rgb = texture(probeAtlasFiltered, uv).rgb;
    return vec4(rgb, 1.0);
}

vec4 renderProbeAtlasCurrentRadianceDebug()
{
    vec3 rgb = texture(probeAtlasCurrent, uv).rgb;
    return vec4(rgb, 1.0);
}

vec4 renderProbeAtlasTraceRadianceDebug()
{
    vec3 rgb = texture(probeAtlasTrace, uv).rgb;
    return vec4(rgb, 1.0);
}

vec4 renderProbeAtlasGatherInputRadianceDebug()
{
    vec3 rgb = texture(probeAtlasGatherInput, uv).rgb;
    return vec4(rgb, 1.0);
}

vec4 renderProbeAtlasHitDistanceDebug()
{
    // Atlas hit distance is stored in A as log(distance + 1).
    // Use the gather-input atlas since that's what downstream shading consumes.
    float distLog = texture(probeAtlasGatherInput, uv).a;
    // Scale log distances into [0,1] in a somewhat stable way.
    float t = clamp(distLog / 5.0, 0.0, 1.0);
    return vec4(heatmap(t), 1.0);
}

vec4 renderProbeAtlasFilterDeltaDebug()
{
    vec3 curr = texture(probeAtlasCurrent, uv).rgb;
    vec3 filt = texture(probeAtlasFiltered, uv).rgb;
    float d = length(filt - curr);
    // Scale a bit so subtle changes show up.
    return vec4(heatmap(clamp(d * 4.0, 0.0, 1.0)), 1.0);
}

vec4 renderProbeAtlasGatherInputSourceDebug()
{
    // Solid color to make it obvious what the renderer is feeding into gather.
    // Red = trace/raw, Yellow = temporal/current, Green = filtered
    if (gatherAtlasSource == 2) return vec4(0.1, 1.0, 0.1, 1.0);
    if (gatherAtlasSource == 1) return vec4(1.0, 1.0, 0.1, 1.0);
    return vec4(1.0, 0.1, 0.1, 1.0);
}

vec4 renderProbeAtlasTemporalRejectionDebug()
{
    float conf;
    uint flags;
    lumonDecodeMeta(texture(probeAtlasMeta, uv).rg, conf, flags);

    // Priority ordering: show the most actionable rejection reason.
    if ((flags & LUMON_META_TEMPREJ_REPROJ_OOB) != 0u)
    {
        return vec4(1.0, 0.0, 0.0, 1.0); // Red = reprojection out of bounds
    }

    if ((flags & LUMON_META_TEMPREJ_VELOCITY_TOO_LARGE) != 0u)
    {
        return vec4(1.0, 1.0, 0.0, 1.0); // Yellow = velocity too large
    }

    if ((flags & LUMON_META_TEMPREJ_HITDIST_DELTA) != 0u)
    {
        return vec4(1.0, 0.5, 0.0, 1.0); // Orange = hit-distance delta reject
    }

    if ((flags & LUMON_META_TEMPREJ_HIT_CLASS_MISMATCH) != 0u)
    {
        return vec4(1.0, 0.0, 1.0, 1.0); // Magenta = hit/miss classification changed
    }

    if ((flags & LUMON_META_TEMPREJ_CONFIDENCE_LOW) != 0u)
    {
        return vec4(0.8, 0.2, 0.8, 1.0); // Purple = low history confidence
    }

    if ((flags & LUMON_META_TEMPREJ_HISTORY_INVALID) != 0u)
    {
        return vec4(0.5, 0.0, 0.5, 1.0); // Dark purple = no valid history
    }

    if ((flags & LUMON_META_TEMPREJ_VELOCITY_INVALID) != 0u)
    {
        return vec4(0.0, 0.4, 1.0, 1.0); // Blue = velocity invalid (fell back to non-velocity path)
    }

    // No rejection bits set => history considered valid.
    return vec4(0.0, 1.0, 0.0, 1.0); // Green
}

// Phase 10: visualize which atlas texels are selected by the probe-resolution trace mask.
vec4 renderProbeAtlasPisTraceMaskDebug()
{
    ivec2 atlasSize = textureSize(probeAtlasMeta, 0);
    ivec2 atlasCoord = ivec2(clamp(uv * vec2(atlasSize), vec2(0.0), vec2(atlasSize) - vec2(1.0)));

    ivec2 probeCoord = atlasCoord / LUMON_OCTAHEDRAL_SIZE;
    ivec2 octTexel = atlasCoord - probeCoord * LUMON_OCTAHEDRAL_SIZE;
    int texelIndex = octTexel.y * LUMON_OCTAHEDRAL_SIZE + octTexel.x;

    vec2 maskPacked = texelFetch(probeTraceMask, probeCoord, 0).xy;
    uvec2 maskBits = uvec2(floatBitsToUint(maskPacked.x), floatBitsToUint(maskPacked.y));

    bool validMask = (maskBits.x | maskBits.y) != 0u;
    if (!validMask)
    {
        return vec4(0.8, 0.0, 0.0, 1.0); // Red = mask invalid / not produced
    }

    bool selected;
    if (texelIndex < 32)
    {
        selected = ((maskBits.x >> uint(texelIndex)) & 1u) != 0u;
    }
    else
    {
        selected = ((maskBits.y >> uint(texelIndex - 32)) & 1u) != 0u;
    }

    // Green = traced this frame, Dark gray = preserved
    return selected ? vec4(0.1, 1.0, 0.1, 1.0) : vec4(0.12, 0.12, 0.12, 1.0);
}

// Phase 10: per-probe importance energy heatmap (sum of weights).
// Sampled at probe resolution and expanded across the probe's 8×8 tile.
vec4 renderProbePisEnergyDebug()
{
    ivec2 atlasSize = textureSize(probeAtlasMeta, 0);
    ivec2 atlasCoord = ivec2(clamp(uv * vec2(atlasSize), vec2(0.0), vec2(atlasSize) - vec2(1.0)));
    ivec2 probeCoord = atlasCoord / LUMON_OCTAHEDRAL_SIZE;

    float e = texelFetch(probePisEnergy, probeCoord, 0).r;

    // Log-ish compression to keep ranges visible.
    float t = clamp(log2(1.0 + e) / 8.0, 0.0, 1.0);
    return vec4(heatmap(t), 1.0);
}

// Program entry: ProbeAtlas
vec4 RenderDebug_ProbeAtlas(vec2 screenPos)
{
    switch (debugMode)
    {
        case 12: return renderProbeAtlasMetaConfidenceDebug();
        case 13: return renderProbeAtlasTemporalAlphaDebug();
        case 14: return renderProbeAtlasMetaFlagsDebug();
        case 15: return renderProbeAtlasFilteredRadianceDebug();
        case 16: return renderProbeAtlasFilterDeltaDebug();
        case 17: return renderProbeAtlasGatherInputSourceDebug();
        case 45: return renderProbeAtlasCurrentRadianceDebug();
        case 46: return renderProbeAtlasGatherInputRadianceDebug();
        case 47: return renderProbeAtlasHitDistanceDebug();
        case 48: return renderProbeAtlasTraceRadianceDebug();
        case 49: return renderProbeAtlasTemporalRejectionDebug();
        case 50: return renderProbeAtlasPisTraceMaskDebug();
        case 51: return renderProbePisEnergyDebug();
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
