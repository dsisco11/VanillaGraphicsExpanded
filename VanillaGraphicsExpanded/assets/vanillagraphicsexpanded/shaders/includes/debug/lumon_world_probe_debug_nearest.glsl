#ifndef LUMON_DEBUG_LUMON_WORLD_PROBE_DEBUG_NEAREST_GLSL
#define LUMON_DEBUG_LUMON_WORLD_PROBE_DEBUG_NEAREST_GLSL


/** Implements lumon world probe debug nearest for its explicit view entrypoint. */
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

    // Depth reconstruction and the rebased clipmap origins share the current terrain render origin.
    vec3 worldPosRel = worldPosWS;
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
#endif
