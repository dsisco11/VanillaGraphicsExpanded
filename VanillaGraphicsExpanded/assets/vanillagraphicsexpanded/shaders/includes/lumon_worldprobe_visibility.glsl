#ifndef LUMON_WORLDPROBE_VISIBILITY_GLSL
#define LUMON_WORLDPROBE_VISIBILITY_GLSL

/** Checks the probe-to-sample segment using stored directional geometry, independently of lighting direction. */
bool lumonWorldProbeCanReachSample(
    sampler2D radianceAtlas, ivec3 storageIndex, int level, int resolution,
    vec3 probeCenter, vec3 samplePosition, float spacing)
{
    vec3 delta = samplePosition - probeCenter;
    float distanceToSample = length(delta);
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
}

#endif
