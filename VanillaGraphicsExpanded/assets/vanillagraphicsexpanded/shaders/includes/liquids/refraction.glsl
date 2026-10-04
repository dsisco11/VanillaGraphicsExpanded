#ifndef VGE_WATER_REFRACTION_GLSL
#define VGE_WATER_REFRACTION_GLSL
@import "./transport.glsl"
// Opt-in diagnostics count every ray-position lookup, including the seed.
// Reasons: 1 projection, 2 unavailable data, 3 reconstruction, 4 interface,
// 6 TIR, 7 unsupported crossing, 8 nonfinite radiance, 9 range/budget exhaustion.
#ifndef VGE_REFRACTION_EVENT
#define VGE_REFRACTION_EVENT(reason)
#endif
#ifndef VGE_REFRACTION_SAMPLE
#define VGE_REFRACTION_SAMPLE(uv, depth)
#endif
@import "./receiver.glsl"

/** Projects visible ray positions; partial edge footprints are validated by the receiver filter. */
bool VgeRefractionProject(vec3 position, out vec2 sampleUv)
{
    vec4 clip = projectionMatrix * vec4(position, 1.0);
    sampleUv = vec2(0);
    if (any(isnan(clip)) || any(isinf(clip)) || clip.w <= .0001)
    { VGE_REFRACTION_EVENT(1); return false; }
    sampleUv = clip.xy / clip.w * .5 + .5;
    bool supported = all(greaterThanEqual(sampleUv, vec2(0))) && all(lessThanEqual(sampleUv, vec2(1)));
    if (!supported) { VGE_REFRACTION_EVENT(1); }
    return supported;
}

/** Clips the search extent analytically against screen coverage and the eye plane, without depth reads. */
float VgeRefractionVisibleExtent(vec3 surface, vec3 direction)
{
    vec4 clipOrigin = projectionMatrix * vec4(surface, 1.0);
    vec4 delta = projectionMatrix * vec4(direction, 0.0);
    float values[5] = float[5](clipOrigin.w + clipOrigin.x, clipOrigin.w - clipOrigin.x,
        clipOrigin.w + clipOrigin.y, clipOrigin.w - clipOrigin.y, clipOrigin.w - .0001);
    float slopes[5] = float[5](delta.w + delta.x, delta.w - delta.x,
        delta.w + delta.y, delta.w - delta.y, delta.w);
    float extent = 32.0;
    for (int plane = 0; plane < 5; ++plane)
        if (slopes[plane] < 0.0) extent = min(extent, max(0.0, -values[plane] / slopes[plane]));
    // Stay numerically inside coverage; the receiver filter itself handles edges.
    return max(0.0, extent - .0001);
}

/** Estimates a ray/represented-plane distance without assuming that view Z decreases. */
float VgeRefractionPlaneDistance(vec3 surface, vec3 direction, VgeRefractionSupport support)
{
    float denominator = dot(direction, support.normalVS);
    if (abs(denominator) < .00001) return -1.0;
    return dot(support.positionVS - surface, support.normalVS) / denominator;
}

/** Confirms a local intersection using cached coverage, never extrapolating across an unknown patch. */
bool VgeRefractionPatchHit(vec3 surface, vec3 direction,
    VgeRefractionSupport support, out float hitDistance, out vec3 radiance)
{
    hitDistance = 0.0;
    radiance = support.radiance;
    // Matching only filtered axial depth would associate a different ray position
    // with this color on a slope. Sparse/nonplanar geometry guides further probes
    // but cannot prove a geometric intersection; validated UV fallback owns it.
    if (!support.planar) return false;
    hitDistance = VgeRefractionPlaneDistance(surface, direction, support);
    if (hitDistance <= 0.0 || hitDistance > 32.0) return false;
    vec2 hitUv;
    if (!VgeRefractionProject(surface + direction * hitDistance, hitUv)) return false;
    // Original reduced-source positions can form an irregular quadrilateral. A
    // bounding box would fill unsupported corners; intersect the actual triangles
    // and reweight cached radiance at the corrected hit, with no extra depth read.
    vec3 hit = surface + direction * hitDistance;
    return VgeRefractionTriangle(hit, support, support.primaryTriangle, radiance)
        || (support.secondaryTriangle && VgeRefractionTriangle(hit, support, ivec3(1,3,2), radiance));
}

/** Searches the represented opaque layer with an exact total ceiling of two, four or eight lookups. */
VgeWaterReceiver VgeWaterRefraction(vec3 surface, vec3 normalVS, bool underwater, int budget)
{
    vec3 direction = refract(normalize(surface), normalVS, underwater ? 1.333 : 1.0 / 1.333);
    VgeWaterReceiver result = VgeWaterReceiver(false, VGE_WATER_RECEIVER_NONE,
        vec3(0), surface, direction, 0.0, 0.0);
    if (dot(direction, direction) < .0001) { VGE_REFRACTION_EVENT(6); return result; }
    mat4 inverseProjection = inverseProjectionMatrix;
#ifdef VGE_WATER_REFRACTION_QUALITY
    const int limit = 1 << VGE_WATER_REFRACTION_QUALITY;
#else
    int limit = budget == 2 ? 2 : budget == 4 ? 4 : 8;
#endif
    float extent = VgeRefractionVisibleExtent(surface, direction);
    float distance = 0.0;
    // The seed depth predicts local represented coverage; unrepresented gaps use
    // bounded recovery probes rather than the old fixed quadratic distribution.
    float recoveryDistance = .125;
    for (int step = 0; step < limit; ++step)
    {
        vec2 sampleUv;
        if (!VgeRefractionProject(surface + direction * distance, sampleUv)) return result;
        VgeRefractionSupport support;
        bool valid = VgeRefractionFilterSupport(sampleUv, surface, normalVS, inverseProjection, support);
        VGE_REFRACTION_SAMPLE(sampleUv, -support.positionVS.z);
        if (!valid)
        {
            // No crossing bracket spans a gap. A later lookup must independently
            // prove a locally supported receiver using its own compatible taps.
            distance = min(extent, max(distance + .125, recoveryDistance));
            recoveryDistance = min(extent, max(recoveryDistance * 4.0, distance * 2.0));
            continue;
        }
        float hitDistance;
        vec3 hitRadiance;
        if (VgeRefractionPatchHit(surface, direction, support, hitDistance, hitRadiance))
        {
            VGE_REFRACTION_EVENT(0);
            return VgeWaterReceiver(true, VGE_WATER_RECEIVER_RAY, hitRadiance,
                surface + direction * hitDistance, direction,
                underwater ? length(surface) : hitDistance, 1.0);
        }
        float estimate = VgeRefractionPlaneDistance(surface, direction, support);
        VGE_REFRACTION_EVENT(estimate > 32.0 ? 9 : 7);
        // Receiver-plane/Newton estimates are proposals, not assumed crossings.
        // Each change of represented patch consumes another counted lookup.
        if (estimate > 0.0 && abs(min(estimate, extent) - distance) > .0005)
            distance = min(estimate, extent);
        else
        {
            distance = min(extent, max(distance + .125, recoveryDistance));
            recoveryDistance = min(extent, max(recoveryDistance * 4.0, distance * 2.0));
        }
    }
    // Exhaustion is a search limit, never a license to accept the last receiver.
    return result;
}
#endif
