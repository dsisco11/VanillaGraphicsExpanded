#ifndef VGE_WATER_PIXEL_NORMAL_REFRACTION_GLSL
#define VGE_WATER_PIXEL_NORMAL_REFRACTION_GLSL
@import "./uv_distortion.glsl"

/** Finds the nearest projected point inside one represented source triangle. */
bool VgeWaterPixelNormalClosestUv(vec2 query, vec2 a, vec2 b, vec2 c, out vec2 closest)
{
    closest = query;
    vec2 ab = b - a, ac = c - a;
    float area = ab.x * ac.y - ab.y * ac.x;
    if (abs(area) <= 1e-12) return false;
    vec2 vertices[3] = vec2[3](a, b, c);
    bool inside = true;
    float bestDistance = 3.402823e38;
    // A point outside a convex triangle has its nearest point on one of its three segments.
    // Projection uses validated source vertices only; this never extends their coverage.
    for (int edge = 0; edge < 3; ++edge)
    {
        vec2 edgeOrigin = vertices[edge], side = vertices[(edge + 1) % 3] - edgeOrigin;
        vec2 delta = query - edgeOrigin;
        inside = inside && (side.x * delta.y - side.y * delta.x) * sign(area) >= 0.0;
        float lengthSquared = dot(side, side);
        if (lengthSquared <= 1e-16) continue;
        vec2 candidate = edgeOrigin + side * clamp(dot(delta, side) / lengthSquared, 0.0, 1.0);
        float distanceSquared = dot(query - candidate, query - candidate);
        if (distanceSquared < bestDistance) { bestDistance = distanceSquared; closest = candidate; }
    }
    if (inside) closest = query;
    return true;
}

/** Continues a reduced quality-zero sample only to the closest validated submerged coverage. */
bool VgeWaterPixelNormalContinuePatch(vec2 sampleUv, mat4 inverseProjection, VgeRefractionSupport support,
    out vec3 positionVS, out vec3 radiance)
{
    positionVS = support.positionVS;
    radiance = vec3(0);
    if (!support.triangular) return false;
    float bestDistance = 3.402823e38;
    vec2 closestUv = sampleUv;
    ivec3 closestTriangle = support.primaryTriangle;
    bool found = false;
    // No receiver reads are needed: filter support already established these source triangles'
    // submerged eligibility and layer compatibility. Foreground verification taps are excluded.
    for (int triangle = 0; triangle < (support.secondaryTriangle ? 2 : 1); ++triangle)
    {
        ivec3 taps = triangle == 0 ? support.primaryTriangle : ivec3(1,3,2);
        vec2 a, b, c, candidate;
        if (!VgeWaterUvProject(support.tapPositions[taps.x], false, a)
            || !VgeWaterUvProject(support.tapPositions[taps.y], false, b)
            || !VgeWaterUvProject(support.tapPositions[taps.z], false, c)
            || !VgeWaterPixelNormalClosestUv(sampleUv, a, b, c, candidate)) continue;
        float distanceSquared = dot(candidate - sampleUv, candidate - sampleUv);
        if (distanceSquared < bestDistance)
        {
            found = true; bestDistance = distanceSquared; closestUv = candidate; closestTriangle = taps;
        }
    }
    if (!found) return false;
    vec3 cameraRay = (inverseProjection * vec4(closestUv * 2.0 - 1.0, -1.0, 1.0)).xyz;
    // Use the existing associated geometry/radiance interpolation and its precision/color guards.
    return VgeWaterUvTriangle(cameraRay, support, closestTriangle, positionVS, radiance);
}

/** Samples bounded normal-detail distortion without projecting a Snell receiver endpoint. */
VgeWaterReceiver VgeWaterPixelNormalRefraction(vec3 surface, vec3 normalVS, vec3 baseNormalVS, bool underwater)
{
    vec3 direction = refract(normalize(surface), normalVS, underwater ? 1.333 : 1.0 / 1.333);
    VgeWaterReceiver result = VgeWaterReceiver(false, VGE_WATER_RECEIVER_NONE,
        vec3(0), surface, direction, 0.0, 0.0);
    // Keep the optical direction for scattering and TIR, not for the screen offset.
    if (dot(direction, direction) < .0001) { VGE_REFRACTION_EVENT(6); return result; }
    vec2 seedUv;
    if (!VgeWaterUvProject(surface, false, seedUv)) return result;
    VgeRefractionSupport support;
    VGE_REFRACTION_UV_SAMPLE(seedUv);
    if (!VgeRefractionFilterSupport(seedUv, surface, normalVS, inverseProjectionMatrix, support)) return result;

    // Pixel-normal offset: flat surfaces have no displacement. Use a
    // resolution-independent strength with projection scaling in our metre-based
    // renderer, and fade distortion over the first 30 cm of axial separation.
    const float strength = .02;
    float shallow = clamp((surface.z - support.positionVS.z) / .3, 0.0, 1.0);
    vec2 offset = (baseNormalVS.xy - normalVS.xy)
        * vec2(projectionMatrix[0][0], projectionMatrix[1][1]) * strength * shallow;
    vec2 inset = min(vec2(.5), vec2(.55) / vec2(textureSize(vge_refractionDepth, 0)));
    vec2 sampleUv = seedUv;
    if (dot(offset, offset) > 0.0)
    {
        vec2 candidateUv = clamp(seedUv + offset, inset, vec2(1) - inset);
        VgeRefractionSupport candidate;
        VGE_REFRACTION_UV_SAMPLE(candidateUv);
        // Edge continuation cannot authorize foreground or unavailable receivers.
        if (VgeRefractionFilterSupport(candidateUv, surface, normalVS, inverseProjectionMatrix, candidate))
        {
            support = candidate;
            sampleUv = candidateUv;
        }
    }
    vec3 position = support.positionVS, radiance;
    vec3 correctedPosition, correctedRadiance;
#ifdef VGE_WATER_BACKGROUND_RESOLUTION
    const bool reduced = VGE_WATER_BACKGROUND_RESOLUTION == 1;
#else
    bool reduced = any(notEqual(textureSize(vge_refractionDepth, 0), ivec2(frameSize)));
#endif
    // Reduced texels retain original source UVs, which can leave gaps inside their nominal quad.
    // Try actual neighboring coverage first, then clamp to represented submerged triangles before
    // weighted fallback. The continuation adds geometry arithmetic but no depth/neighbor reads.
    if (VgeWaterUvPatch(sampleUv, inverseProjectionMatrix, support, correctedPosition, correctedRadiance)
        || (reduced && VgeWaterUvAdjacentPatch(sampleUv, surface, normalVS, inverseProjectionMatrix,
            support, correctedPosition, correctedRadiance))
        || (reduced && VgeWaterPixelNormalContinuePatch(sampleUv, inverseProjectionMatrix,
            support, correctedPosition, correctedRadiance)))
    {
        position = correctedPosition;
        radiance = correctedRadiance;
    }
    else if (!VgeRefractionRadiance(support, radiance)) return result;
    float pathLength = underwater ? length(surface)
        : min(32.0, max(0.0, -dot(position - surface, normalVS)) / max(.1, -dot(direction, normalVS)));
    return VgeWaterReceiver(true, VGE_WATER_RECEIVER_UV, radiance, position, direction, pathLength, 1.0);
}
#endif
