#ifndef LUMON_NEAR_FIELD_CACHE_HANDOFF_GLSL
#define LUMON_NEAR_FIELD_CACHE_HANDOFF_GLSL

/** Selects a finite cache neighborhood and the segment that must be proven clear before interpolation. */
bool lumonNearFieldCacheCoverage(vec3 position, out int level, out float radius, out float traceDistance)
{
    level = 0; radius = 0.0; traceDistance = 0.0;
#if VGE_LUMON_WORLDPROBE_ENABLED
    if (VGE_LUMON_WORLDPROBE_LEVELS <= 0 || VGE_LUMON_WORLDPROBE_RESOLUTION <= 0) return false;
    level = lumonWorldProbeSelectLevelByExtents(position, VGE_LUMON_WORLDPROBE_BASE_SPACING,
        VGE_LUMON_WORLDPROBE_LEVELS, VGE_LUMON_WORLDPROBE_RESOLUTION);
    float spacing = lumonWorldProbeSpacing(VGE_LUMON_WORLDPROBE_BASE_SPACING, level);
    if (!lumonWorldProbeIsInsideLevel(position, lumonWorldProbeGetOriginMinCorner(level), spacing, VGE_LUMON_WORLDPROBE_RESOLUTION)) return false;
    // Every neighboring center lies within one cell diagonal. The near-field segment
    // reaches beyond the farthest point on each reprojection sphere.
    radius = spacing * sqrt(3.0);
    traceDistance = radius * 2.0;
    return true;
#else
    return false;
#endif
}

/** Samples only distant cache texels after near-field geometry has been resolved; no probe-to-surface rejection. */
LumOnWorldProbeRadianceSample lumonSampleLocalCache(vec3 position, vec3 direction, int level, float radius)
{
    LumOnWorldProbeRadianceSample result;
    result.radiance = vec3(0.0); result.confidence = 0.0; result.cacheAvailable = true;
#if VGE_LUMON_WORLDPROBE_ENABLED
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float spacing = lumonWorldProbeSpacing(VGE_LUMON_WORLDPROBE_BASE_SPACING, level);
    vec3 origin = lumonWorldProbeGetOriginMinCorner(level);
    vec3 local = clamp((position - origin) / spacing - 0.5, vec3(0.0), vec3(float(resolution - 1)));
    ivec3 base = ivec3(floor(local));
    vec3 f = fract(local);
    float totalWeight = 0.0;
    for (int c = 0; c < 8; c++)
    {
        ivec3 offset = ivec3(c & 1, (c >> 1) & 1, (c >> 2) & 1);
        ivec3 index = min(base + offset, ivec3(resolution - 1));
        vec3 weights = mix(vec3(1.0) - f, f, vec3(offset));
        ivec3 storage = lumonWorldProbeWrapIndex(index + ivec3(lumonWorldProbeGetRingOffset(level)), resolution);
        ivec2 scalar = lumonWorldProbeAtlasCoord(storage, level, resolution);
        float weight = weights.x * weights.y * weights.z * texelFetch(worldProbeMeta0, scalar, 0).x;
        if (weight <= 1e-6) continue;
        vec3 center = origin + (vec3(index) + 0.5) * spacing;
        vec3 relative = position - center;
        float b = dot(relative, direction);
        float t = -b + sqrt(max(0.0, b*b + radius*radius - dot(relative, relative)));
        vec3 reprojection = relative + direction * t;
        vec4 sampleValue = lumonWorldProbeFetchRadianceAtlasTexel(worldProbeRadianceAtlas, storage, level, resolution,
            lumonWorldProbeDirectionToOctahedralTexel(reprojection));
        if (sampleValue.a == 0.0 || isnan(sampleValue.a) || isinf(sampleValue.a)) continue;
        // Existing cache tiles include near hits. Those texels do not represent the
        // distant domain and must not be reused as if they came from beyond the sphere.
        if (lumonWorldProbeDecodeHitDistanceSigned(sampleValue.a) < radius) continue;
        vec3 lighting = sampleValue.a < 0.0 ?
            max(lumonWorldProbeGetSkyTint(), vec3(0.0)) * clamp(texelFetch(worldProbeVis0, scalar, 0).z, 0.0, 1.0) :
            max(sampleValue.rgb, vec3(0.0));
        // Reproject the lookup direction, not the radiance magnitude. A constant
        // incident field must remain constant as the sample moves between probes.
        result.radiance += lighting * weight;
        totalWeight += weight;
    }
    if (totalWeight > 1e-6) result.radiance /= totalWeight;
    result.confidence = clamp(totalWeight, 0.0, 1.0);
#endif
    return result;
}
#endif
