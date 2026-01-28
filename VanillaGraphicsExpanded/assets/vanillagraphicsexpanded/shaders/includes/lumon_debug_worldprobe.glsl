// Debug modes 31-39, 42-44: World-probe clipmap debug views (Phase 18)

// Debug modes 43-44: Contribution split
vec3 lumonTonemapReinhard(vec3 hdr)
{
    hdr = max(hdr, vec3(0.0));
    return hdr / (hdr + vec3(1.0));
}

vec3 lumonComputeWorldProbeContributionOnly()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec3(0.0);
    }

    float sumW = clamp(texture(indirectHalf, uv).a, 0.0, 1.0);
    if (sumW <= 1e-6)
    {
        return vec3(0.0);
    }

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return vec3(0.0);
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    float worldConf = clamp(wp.confidence, 0.0, 1.0);

    // Reconstruct the screen-first blend weights using the stored final confidence (sumW)
    // and the raw world confidence (worldConf). This matches the derivation used by
    // renderWorldProbeBlendWeightsDebug().
    float screenW = (worldConf >= 0.999)
        ? 0.0
        : clamp((sumW - worldConf) / max(1.0 - worldConf, 1e-6), 0.0, 1.0);
    float worldW = worldConf * (1.0 - screenW);

    vec3 worldContrib = wp.irradiance * (worldW / max(sumW, 1e-6));

    // Match the gather output space (gather pass applies these before writing indirectHalf).
    worldContrib *= indirectIntensity;
    worldContrib *= indirectTint;

    return max(worldContrib, vec3(0.0));
#endif
}

vec4 renderWorldProbeContributionOnlyDebug()
{
    vec3 worldContrib = lumonComputeWorldProbeContributionOnly();
    return vec4(lumonTonemapReinhard(worldContrib), 1.0);
}

vec4 renderScreenSpaceContributionOnlyDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // indirectHalf.rgb contains the *blended* (screen+world) irradiance in gather output space.
    vec3 blended = texture(indirectHalf, uv).rgb;

    // Derive screen portion as blended - worldContribution.
    vec3 worldContrib = lumonComputeWorldProbeContributionOnly();
    vec3 screenContrib = max(blended - worldContrib, vec3(0.0));

    return vec4(lumonTonemapReinhard(screenContrib), 1.0);
}

vec4 lumonWorldProbeDebugDisabledColor()
{
    // Visual cue that the world-probe debug path is compile-time disabled (vs just "no data in bounds").
    float v = 0.5 + 0.5 * sin(uv.x * 80.0) * sin(uv.y * 80.0);
    vec3 a = vec3(0.15, 0.0, 0.2);
    vec3 b = vec3(0.55, 0.0, 0.7);
    return vec4(mix(a, b, v), 1.0);
}

bool lumonWorldProbeDebugNearest(in vec3 worldPosWS, out int outLevel, out ivec2 outAtlasCoord)
{
#if !VGE_LUMON_WORLDPROBE_ENABLED
    outLevel = 0;
    outAtlasCoord = ivec2(0);
    return false;
#else
    int levels = VGE_LUMON_WORLDPROBE_LEVELS;
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;

    if (levels <= 0 || resolution <= 0)
    {
        outLevel = 0;
        outAtlasCoord = ivec2(0);
        return false;
    }

    vec3 worldPosRel = worldPosWS - lumonWorldProbeGetCameraPosWS();
    int level = lumonWorldProbeSelectLevelByExtents(worldPosRel, baseSpacing, levels, resolution);
    float spacing = lumonWorldProbeSpacing(baseSpacing, level);

    vec3 origin = lumonWorldProbeGetOriginMinCorner(level);
    vec3 local = (worldPosRel - origin) / max(spacing, 1e-6);

    // Outside clip volume.
    if (any(lessThan(local, vec3(0.0))) || any(greaterThanEqual(local, vec3(float(resolution)))))
    {
        outLevel = level;
        outAtlasCoord = ivec2(0);
        return false;
    }

    // Probe centers are at cell-centers:
    //   probe(i) center = originMinCorner + (i + 0.5) * spacing
    // So the nearest probe for a point inside the clip volume is the cell that contains it.
    ivec3 idx = ivec3(floor(local));
    idx = clamp(idx, ivec3(0), ivec3(resolution - 1));

    ivec3 ring = ivec3(floor(lumonWorldProbeGetRingOffset(level) + 0.5));
    ivec3 storage = lumonWorldProbeWrapIndex(idx + ring, resolution);

    outLevel = level;
    outAtlasCoord = lumonWorldProbeAtlasCoord(storage, level, resolution);
    return true;
#endif
}

vec3 lumonWorldProbeDebugToneMap(vec3 hdr)
{
    hdr = max(hdr, vec3(0.0));
    return hdr / (hdr + vec3(1.0));
}

vec4 renderWorldProbeIrradianceCombinedDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    vec3 color = lumonWorldProbeDebugToneMap(wp.irradiance);
    return vec4(color, 1.0);
#endif
}

vec4 renderWorldProbeIrradianceLevelDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    int levels = VGE_LUMON_WORLDPROBE_LEVELS;
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;
    if (levels <= 0 || resolution <= 0) return vec4(0.0, 0.0, 0.0, 1.0);

    vec3 posRel = posWS - lumonWorldProbeGetCameraPosWS();
    int level = lumonWorldProbeSelectLevelByExtents(posRel, baseSpacing, levels, resolution);
    float spacing = lumonWorldProbeSpacing(baseSpacing, level);

    LumOnWorldProbeSample sL = lumonWorldProbeSampleLevelTrilinear(
        worldProbeRadianceAtlas, worldProbeVis0, worldProbeMeta0,
        posRel, normalWS,
        lumonWorldProbeGetOriginMinCorner(level), lumonWorldProbeGetRingOffset(level),
        spacing, resolution, level);

    vec3 color = lumonWorldProbeDebugToneMap(sL.irradiance);
    // Encode selected level in alpha for quick inspection.
    float a = (levels > 1) ? float(level) / float(max(levels - 1, 1)) : 0.0;
    return vec4(color, a);
#endif
}

vec4 renderWorldProbeConfidenceDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    float c = clamp(wp.confidence, 0.0, 1.0);
    return vec4(vec3(c), 1.0);
#endif
}

vec4 renderWorldProbeAoDirectionDebug()
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

    vec4 vis = texelFetch(worldProbeVis0, ac, 0);
    vec3 aoDir = lumonOctahedralUVToDirection(vis.xy);
    float aoConf = clamp(vis.w, 0.0, 1.0);

    vec3 color = aoDir * 0.5 + 0.5;
    color *= aoConf;
    return vec4(color, 1.0);
}

vec4 renderWorldProbeAoConfidenceDebug()
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

    float aoConf = clamp(texelFetch(worldProbeVis0, ac, 0).w, 0.0, 1.0);
    return vec4(vec3(aoConf), 1.0);
}

vec4 renderWorldProbeDistanceDebug()
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

    float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float spacing = lumonWorldProbeSpacing(baseSpacing, level);

    float meanLog = texelFetch(worldProbeDist0, ac, 0).x; // log(dist+1)
    float dist = exp(meanLog) - 1.0;

    float maxDist = max(spacing * float(resolution), 1e-3);
    float t = clamp(dist / maxDist, 0.0, 1.0);
    t = sqrt(t);
    return vec4(vec3(t), 1.0);
}

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

vec4 renderWorldProbeBlendWeightsDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    // indirectHalf alpha encodes the final (screen+world) confidence.
    float sumW = clamp(texture(indirectHalf, uv).a, 0.0, 1.0);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    float worldConf = clamp(wp.confidence, 0.0, 1.0);

    float screenW = (worldConf >= 0.999)
        ? 0.0
        : clamp((sumW - worldConf) / max(1.0 - worldConf, 1e-6), 0.0, 1.0);

    float worldW = worldConf * (1.0 - screenW);

    // R=screen weight, G=world weight.
    return vec4(screenW, worldW, 0.0, 1.0);
#endif
}

vec4 renderWorldProbeRawConfidencesDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    // Final confidence from gather (screen-first blend result).
    float sumW = clamp(texture(indirectHalf, uv).a, 0.0, 1.0);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    float worldConf = clamp(wp.confidence, 0.0, 1.0);

    // Reconstruct the screen confidence (screenW) from sumW and worldConf.
    // This matches the gather blend:
    //   sumW = screenW + worldConf * (1 - screenW)
    float screenConf = (worldConf >= 0.999)
        ? 0.0
        : clamp((sumW - worldConf) / max(1.0 - worldConf, 1e-6), 0.0, 1.0);

    return vec4(screenConf, worldConf, sumW, 1.0);
#endif
}

vec4 renderWorldProbeCrossLevelBlendDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;

    int levels = VGE_LUMON_WORLDPROBE_LEVELS;
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;
    if (levels <= 0 || resolution <= 0) return vec4(0.0, 0.0, 0.0, 1.0);

    vec3 posRel = posWS - lumonWorldProbeGetCameraPosWS();
    int level = lumonWorldProbeSelectLevelByExtents(posRel, baseSpacing, levels, resolution);
    float spacingL = lumonWorldProbeSpacing(baseSpacing, level);

    vec3 originL = lumonWorldProbeGetOriginMinCorner(level);
    vec3 localL = (posRel - originL) / max(spacingL, 1e-6);
    float edgeDist = lumonWorldProbeDistanceToBoundaryProbeUnits(localL, resolution);
    float wL = lumonWorldProbeCrossLevelBlendWeight(edgeDist, 2.0, 2.0);

    float levelN = (levels > 1) ? float(level) / float(max(levels - 1, 1)) : 0.0;
    // R = selected level (normalized), G = weight for L, B = weight for L+1.
    return vec4(levelN, wL, 1.0 - wL, 1.0);
#endif
}

// Program entry: WorldProbe
vec4 RenderDebug_WorldProbe(vec2 screenPos)
{
    switch (debugMode)
    {
        case 31: return renderWorldProbeIrradianceCombinedDebug();
        case 32: return renderWorldProbeIrradianceLevelDebug();
        case 33: return renderWorldProbeConfidenceDebug();
        case 34: return renderWorldProbeAoDirectionDebug();
        case 35: return renderWorldProbeAoConfidenceDebug();
        case 36: return renderWorldProbeDistanceDebug();
        case 37: return renderWorldProbeFlagsHeatmapDebug();
        case 38: return renderWorldProbeBlendWeightsDebug();
        case 39: return renderWorldProbeCrossLevelBlendDebug();
        case 42: return renderWorldProbeRawConfidencesDebug();
        case 43: return renderWorldProbeContributionOnlyDebug();
        case 44: return renderScreenSpaceContributionOnlyDebug();
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
