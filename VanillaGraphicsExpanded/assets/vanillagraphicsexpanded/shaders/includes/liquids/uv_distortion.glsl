#ifndef VGE_WATER_UV_DISTORTION_GLSL
#define VGE_WATER_UV_DISTORTION_GLSL
@import "./transport.glsl"
@import "./receiver.glsl"
#ifndef VGE_REFRACTION_UV_SAMPLE
#define VGE_REFRACTION_UV_SAMPLE(uv)
#endif

/** Projects source geometry exactly, or bounds an approximate receiver lookup to safe image coverage. */
bool VgeWaterUvProject(vec3 positionVS, bool clampToEdge, out vec2 sampleUv)
{
    vec4 clip = projectionMatrix * vec4(positionVS, 1.0);
    sampleUv = vec2(0);
    if (any(isnan(clip)) || any(isinf(clip)) || clip.w <= .0001) return false;
    sampleUv = clip.xy / clip.w * .5 + .5;
    if (any(isnan(sampleUv)) || any(isinf(sampleUv))) return false;
    if (clampToEdge)
    {
        // Keep the complete filtering footprint inside the captured image. This is
        // approximate edge continuation, never evidence of an offscreen ray hit.
        // A one-texel dimension collapses to its center instead of reversing bounds.
        vec2 inset = min(vec2(.5), vec2(.55) / vec2(textureSize(vge_refractionDepth, 0)));
        sampleUv = clamp(sampleUv, inset, vec2(1) - inset);
        return true;
    }
    return all(greaterThanEqual(sampleUv, vec2(0))) && all(lessThanEqual(sampleUv, vec2(1)));
}

/** Intersects one represented source triangle with a camera ray for approximate UV reconstruction. */
bool VgeWaterUvTriangle(vec3 cameraRay, VgeRefractionSupport support, ivec3 taps,
    out vec3 positionVS, out vec3 radiance)
{
    positionVS = support.positionVS;
    radiance = vec3(0);
    vec3 triangleOrigin = support.tapPositions[taps.x];
    vec3 triangleNormal = cross(support.tapPositions[taps.y] - triangleOrigin,
        support.tapPositions[taps.z] - triangleOrigin);
    if (dot(triangleNormal, triangleNormal) <= 1e-16) return false;
    triangleNormal = normalize(triangleNormal);
    float denominator = dot(cameraRay, triangleNormal);
    if (abs(denominator) < .00001) return false;
    float distance = dot(triangleOrigin, triangleNormal) / denominator;
    if (distance <= 0.0) return false;
    vec3 point = cameraRay * distance;
    if (!VgeRefractionTriangle(point, support, taps, radiance)) return false;
    positionVS = point;
    return true;
}

/** Reweights a camera sample on cached piecewise geometry without proving a Snell-ray hit. */
bool VgeWaterUvPatch(vec2 sampleUv, mat4 inverseProjection, VgeRefractionSupport support,
    out vec3 positionVS, out vec3 radiance)
{
    positionVS = support.positionVS;
    radiance = vec3(0);
    if (!support.triangular) return false;
    vec3 cameraRay = (inverseProjection * vec4(sampleUv * 2.0 - 1.0, -1.0, 1.0)).xyz;
    // Original half-resolution source UVs need not sit at reduced cell centers.
    // Piecewise triangles also retain curved receivers; requiring an entirely
    // planar quad would switch back to biased sampling as curvature changes.
    return VgeWaterUvTriangle(cameraRay, support, support.primaryTriangle, positionVS, radiance)
        || (support.secondaryTriangle && VgeWaterUvTriangle(cameraRay, support, ivec3(1,3,2), positionVS, radiance));
}

/** Corrects a reduced footprint against actual source coverage using at most three missing neighbors. */
bool VgeWaterUvAdjacentPatch(vec2 sampleUv, vec3 surface, vec3 normalVS, mat4 inverseProjection,
    VgeRefractionSupport support, out vec3 positionVS, out vec3 radiance)
{
    positionVS = support.positionVS;
    radiance = vec3(0);
    if (!support.secondaryTriangle) return false;
    vec2 sourceUvs[4];
    for (int tap = 0; tap < 4; ++tap)
        if (!VgeWaterUvProject(support.tapPositions[tap], false, sourceUvs[tap])) return false;
    // The selected originals form a convex quad inside their four reduced cells.
    // Find violated edges in its actual camera projection, rather than guessing
    // a coordinate offset from a different receiver footprint.
    ivec2 shift = ivec2(0);
    ivec4 corners = ivec4(0,1,3,2);
    for (int edge = 0; edge < 4; ++edge)
    {
        vec2 a = sourceUvs[corners[edge]];
        vec2 b = sourceUvs[corners[(edge + 1) & 3]];
        vec2 side = b - a, query = sampleUv - a;
        if (side.x * query.y - side.y * query.x >= 0.0) continue;
        if (edge == 0) shift.y = -1;
        else if (edge == 1) shift.x = 1;
        else if (edge == 2) shift.y = 1;
        else shift.x = -1;
    }
    if (all(equal(shift, ivec2(0)))) return false;
    ivec2 size = textureSize(vge_refractionDepth, 0);
    ivec2 footprintOrigin = ivec2(floor(sampleUv * vec2(size) - .5));
    VgeRefractionSupport adjacent = support;
    for (int tap = 0; tap < 4; ++tap)
    {
        ivec2 offset = ivec2(tap & 1, tap >> 1);
        ivec2 previousOffset = shift + offset;
        if (all(greaterThanEqual(previousOffset, ivec2(0))) && all(lessThanEqual(previousOffset, ivec2(1))))
        {
            int previousTap = previousOffset.y * 2 + previousOffset.x;
            adjacent.tapPositions[tap] = support.tapPositions[previousTap];
            adjacent.tapPixels[tap] = support.tapPixels[previousTap];
            continue;
        }
        vec3 neighbor;
        float neighborPrecision;
        int rejection;
        if (!VgeRefractionTap(footprintOrigin + previousOffset, surface, normalVS, inverseProjection, true,
            neighbor, neighborPrecision, rejection)) return false;
        // A shifted footprint must remain on the represented layer. Steep planar
        // continuation uses geometric residual; unrelated depth steps stay rejected.
        bool depthCompatible = abs(neighbor.z - support.positionVS.z) <= support.depthToleranceMetres;
        bool planeCompatible = support.planar && abs(dot(neighbor - support.positionVS, support.normalVS))
            <= max(support.precisionMetres, neighborPrecision);
        if (!depthCompatible && !planeCompatible) return false;
        adjacent.tapPositions[tap] = neighbor;
        adjacent.tapPixels[tap] = footprintOrigin + previousOffset;
        adjacent.precisionMetres = max(adjacent.precisionMetres, neighborPrecision);
    }
    adjacent.primaryTriangle = ivec3(0,1,2);
    // New source triangles supply real coverage; neither the old quad's bounding
    // box nor extrapolated barycentric weights can authorize these samples.
    return VgeWaterUvPatch(sampleUv, inverseProjection, adjacent, positionVS, radiance);
}

/** Selects an approximate receiver from an already validated interface seed and Snell direction. */
VgeWaterReceiver VgeWaterUvRefractionFromSeed(vec3 surface, vec3 normalVS, bool underwater,
    vec3 direction, VgeRefractionSupport seed)
{
    VgeWaterReceiver result = VgeWaterReceiver(false, VGE_WATER_RECEIVER_NONE,
        vec3(0), surface, direction, 0.0, 0.0);
    mat4 inverseProjection = inverseProjectionMatrix;

    // Use the represented receiver slope, just as the marcher's next probe does.
    // This remains a UV proposal, never proof of a geometric intersection. Sparse
    // support supplies its axial plane; nearly parallel/negative estimates retain
    // the bounded water-interface thickness estimate instead.
    float seedThickness = max(0.0, -dot(seed.positionVS - surface, normalVS));
    float normalCosine = max(.1, -dot(direction, normalVS));
    float estimate = min(32.0, seedThickness / normalCosine);
    float receiverCosine = dot(direction, seed.normalVS);
    if (abs(receiverCosine) > .00001)
    {
        float receiverEstimate = dot(seed.positionVS - surface, seed.normalVS) / receiverCosine;
        if (receiverEstimate > 0.0) estimate = min(32.0, receiverEstimate);
    }
    // Snell displacement already shrinks with physical separation. An additional
    // shallow-water ramp would abruptly erase it whenever x2 exhausts into UV.
    vec3 selectedPosition = seed.positionVS;
    vec3 selectedRadiance = vec3(0);
    bool corrected = false;
    vec2 projectedUv;
    if (VgeWaterUvProject(surface + direction * estimate, true, projectedUv))
    {
#ifdef VGE_WATER_BACKGROUND_RESOLUTION
        const bool reduced = VGE_WATER_BACKGROUND_RESOLUTION == 1;
#else
        bool reduced = any(notEqual(textureSize(vge_refractionDepth, 0), ivec2(frameSize)));
#endif
        VgeRefractionSupport candidate;
        VGE_REFRACTION_UV_SAMPLE(projectedUv);
        // Unsupported candidate taps cannot import foreground radiance. Preserve
        // the validated seed if the clamped edge footprint fails depth validation.
        if (VgeRefractionFilterSupport(projectedUv, surface, normalVS, inverseProjection, candidate))
        {
            selectedPosition = candidate.positionVS;
            // The seed is no longer needed once the candidate is eligible.
            seed = candidate;
            vec3 correctedPosition, correctedRadiance;
            if (VgeWaterUvPatch(projectedUv, inverseProjection, candidate, correctedPosition, correctedRadiance)
                || (reduced && VgeWaterUvAdjacentPatch(projectedUv, surface, normalVS, inverseProjection,
                    candidate, correctedPosition, correctedRadiance)))
            {
                selectedPosition = correctedPosition;
                selectedRadiance = correctedRadiance;
                corrected = true;
            }
        }
    }
    // Geometry-only proposals never interpolate discarded radiance. Reuse the
    // accepted triangle's color, or reconstruct only the selected spatial fallback.
    if (!corrected && !VgeRefractionRadiance(seed, selectedRadiance)) return result;
    // This is a one-interface planar approximation, not a confirmed intersection.
    // Underwater the camera-to-interface segment is water; the sampled exit is air.
    float pathLength = underwater ? length(surface)
        : min(32.0, max(0.0, -dot(selectedPosition - surface, normalVS)) / normalCosine);
    return VgeWaterReceiver(true, VGE_WATER_RECEIVER_UV, selectedRadiance,
        selectedPosition, direction, pathLength, 1.0);
}

/** Selects standalone UV quality with its own seed, at most two filtered lookups and no ray search. */
VgeWaterReceiver VgeWaterUvRefraction(vec3 surface, vec3 normalVS, bool underwater)
{
    vec3 direction = refract(normalize(surface), normalVS, underwater ? 1.333 : 1.0 / 1.333);
    VgeWaterReceiver result = VgeWaterReceiver(false, VGE_WATER_RECEIVER_NONE,
        vec3(0), surface, direction, 0.0, 0.0);
    // TIR has no transmitted receiver and performs no background reads.
    if (dot(direction, direction) < .0001) { VGE_REFRACTION_EVENT(6); return result; }
    vec2 seedUv;
    if (!VgeWaterUvProject(surface, false, seedUv)) return result;
    VgeRefractionSupport seed;
    VGE_REFRACTION_UV_SAMPLE(seedUv);
    if (!VgeRefractionFilterSupport(seedUv, surface, normalVS, inverseProjectionMatrix, seed)) return result;
    return VgeWaterUvRefractionFromSeed(surface, normalVS, underwater, direction, seed);
}
#endif
