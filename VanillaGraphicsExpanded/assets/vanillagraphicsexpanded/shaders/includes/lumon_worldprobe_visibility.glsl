#ifndef LUMON_WORLDPROBE_VISIBILITY_GLSL
#define LUMON_WORLDPROBE_VISIBILITY_GLSL

#ifndef VGE_LUMON_DIRECT_LOCAL_VISIBILITY
#define VGE_LUMON_DIRECT_LOCAL_VISIBILITY 0
#endif
#if VGE_LUMON_DIRECT_LOCAL_VISIBILITY
@import "./lumon_local_trace.glsl"
#endif

/** Checks the probe-to-sample segment independently of lighting direction; direct consumers use published local geometry. */
bool lumonWorldProbeCanReachSample(
    sampler2D radianceAtlas, ivec3 storageIndex, int level, int resolution,
    vec3 probeCenter, vec3 samplePosition, float spacing)
{
    vec3 delta = samplePosition - probeCenter;
    float distanceToSample = length(delta);
#if VGE_LUMON_DIRECT_LOCAL_VISIBILITY
    // The cache and reconstructed receiver share matrix-relative coordinates.
    // Keep the absolute world origin integer to preserve large-world precision.
    vec3 origin = probeCenter + matrixSpaceWorldBlockOffsetRem;
    ivec3 cell = ivec3(floor(origin)) + matrixSpaceWorldChunkCoordOffset * 32;
    if (distanceToSample <= 1e-6)
    {
        uint geometry;
        return lumonLocalReadGeometry(cell, geometry) && (geometry & 3u) == 1u;
    }
    // Exclude only the receiver endpoint. Do not apply a spacing-scaled depth bias:
    // it could permit a nearby wall between the receiver and the probe.
    float segmentLength = max(distanceToSample - 0.0001, 0.000001);
    return lumonTraceLocal(cell, fract(origin), delta / distanceToSample, segmentLength).outcome == LUMON_LOCAL_CLEAR;
#else
    if (distanceToSample <= 1e-6) return true;

    float encoded = lumonWorldProbeFetchRadianceAtlasTexel(
        radianceAtlas, storageIndex, level, resolution,
        lumonWorldProbeDirectionToOctahedralTexel(delta)).a;
    // Zero is the cleared/unpublished texel sentinel. Misses prove visibility only
    // through their traced range; a missing direction never proves open space.
    if (encoded == 0.0 || isnan(encoded) || isinf(encoded)) return false;
    float recordedDistance = lumonWorldProbeDecodeHitDistanceSigned(encoded);
    // Bound the bias in world units so coarse levels cannot see through voxel walls.
    float tolerance = clamp(spacing * 0.02, 0.001, 0.05);
    return distanceToSample <= recordedDistance + tolerance;
#endif
}

#endif
