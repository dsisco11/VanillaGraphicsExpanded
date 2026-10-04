#ifndef VGE_WATER_RECEIVER_GLSL
#define VGE_WATER_RECEIVER_GLSL
#ifndef VGE_REFRACTION_EVENT
#define VGE_REFRACTION_EVENT(reason)
#endif
layout(binding = 9) uniform sampler2D vge_refractionColor;
layout(binding = 10) uniform sampler2D vge_refractionDepth;

/** Carries one filtered depth layer and the bounded local patch supported by its existing taps. */
struct VgeRefractionSupport
{
    vec3 positionVS;
    vec3 radiance;
    vec3 normalVS;
    vec3 tapPositions[4];
    vec3 tapRadiances[4];
    ivec3 primaryTriangle;
    bool secondaryTriangle;
    float precisionMetres;
    bool planar;
};

/** Reconstructs one physical receiver at its original source pixel, including reduced backgrounds. */
bool VgeRefractionTap(ivec2 pixel, vec3 surface, vec3 normalVS, mat4 inverseProjection,
    out vec3 positionVS, out vec3 radiance, out float precisionMetres, out int rejection)
{
    positionVS = vec3(0);
    radiance = vec3(0);
    precisionMetres = .0005;
    rejection = 2;
    ivec2 size = textureSize(vge_refractionDepth, 0);
    if (any(lessThan(pixel, ivec2(0))) || any(greaterThanEqual(pixel, size))) return false;
    vec4 depth = texelFetch(vge_refractionDepth, pixel, 0);
    vec4 color = texelFetch(vge_refractionColor, pixel, 0);
    if (isnan(depth.r) || isinf(depth.r) || depth.r <= 0.0 || depth.r >= .999999 || color.a < .5) return false;
    if (any(isnan(color)) || any(isinf(color))) { rejection = 8; return false; }
    // Reduced texels retain the selected full-resolution UV; their cell center
    // is not the original ray and can move a silhouette through the water plane.
    // Production variants specialize the receiver convention together with the
    // selected storage. Standalone diagnostics retain dimension-driven inputs.
#ifdef VGE_WATER_BACKGROUND_RESOLUTION
    const bool reduced = VGE_WATER_BACKGROUND_RESOLUTION == 1;
#else
    bool reduced = any(notEqual(size, ivec2(frameSize)));
#endif
    vec2 uv = reduced ? depth.gb : (vec2(pixel) + .5) / vec2(size);
    if (reduced && (depth.a < .5 || any(isnan(uv)) || any(isinf(uv))
        || any(lessThanEqual(uv, vec2(0))) || any(greaterThanEqual(uv, vec2(1))))) return false;
    vec4 position = inverseProjection * vec4(uv * 2.0 - 1.0, depth.r * 2.0 - 1.0, 1.0);
    if (abs(position.w) < .00001 || any(isnan(position)) || any(isinf(position))) { rejection = 3; return false; }
    positionVS = position.xyz / position.w;
    // Propagate a conservative float device-depth perturbation through homogeneous
    // reconstruction. Eight ulps allow arithmetic error without using layer width
    // as a crossing tolerance; cap distant sensitivity at two centimetres.
    float depthSensitivity = abs(2.0 * (inverseProjection[2][2]
        - positionVS.z * inverseProjection[2][3]) / position.w);
    precisionMetres = max(.0005, min(.02, 8.0 * 1.1920929e-7 * depthSensitivity));
    // A numerical separation guard must not discard centimetre-deep physical water.
    if (dot(positionVS - surface, normalVS) >= -.0005) { rejection = 4; return false; }
    radiance = color.rgb;
    return true;
}

/** Filters four spatial taps on one bounded depth layer, sharing weights between geometry and radiance. */
bool VgeRefractionFilterSupport(vec2 uv, vec3 surface, vec3 normalVS, mat4 inverseProjection,
    out VgeRefractionSupport support)
{
    support.positionVS = vec3(0);
    support.radiance = vec3(0);
    support.normalVS = vec3(0,0,1);
    support.precisionMetres = .001;
    support.planar = false;
    support.primaryTriangle = ivec3(0,1,2);
    support.secondaryTriangle = false;
    ivec2 size = textureSize(vge_refractionDepth, 0);
    if (any(notEqual(size, textureSize(vge_refractionColor, 0))) || any(isnan(uv)) || any(isinf(uv))
        || any(lessThan(uv, vec2(0))) || any(greaterThan(uv, vec2(1))))
    { VGE_REFRACTION_EVENT(2); return false; }
    vec2 footprint = uv * vec2(size) - .5;
    ivec2 footprintOrigin = ivec2(floor(footprint));
    vec2 fraction = fract(footprint);
    vec3 positions[4];
    vec3 colors[4];
    float precisions[4];
    float weights[4];
    bool eligible[4];
    float largestWeight = 0.0;
    int anchor = -1;
    int rejection = 2;
    for (int tap = 0; tap < 4; ++tap)
    {
        ivec2 offset = ivec2(tap & 1, tap >> 1);
        vec2 spatial = mix(1.0 - fraction, fraction, vec2(offset));
        weights[tap] = spatial.x * spatial.y;
        int reason;
        eligible[tap] = VgeRefractionTap(footprintOrigin + offset, surface, normalVS,
            inverseProjection, positions[tap], colors[tap], precisions[tap], reason);
        support.tapPositions[tap] = positions[tap];
        support.tapRadiances[tap] = colors[tap];
        if (!eligible[tap])
        {
            if (weights[tap] > 0.0) rejection = reason;
            weights[tap] = 0.0;
            continue;
        }
        if (weights[tap] > largestWeight)
        {
            largestWeight = weights[tap];
            anchor = tap;
        }
    }
    if (anchor < 0) { VGE_REFRACTION_EVENT(rejection); return false; }
    // Ordinary support is capped at two percent of axial depth. At a grazing
    // interface, a parallel submerged shore spans more axial depth per pixel;
    // account for that geometric slope rather than turning it into depth stairs.
    // The grazing allowance remains bounded at eight percent of axial depth.
    float depth = abs(positions[anchor].z);
    vec2 projectedTexel = 2.0 * vec2(abs(inverseProjection[0][0]), abs(inverseProjection[1][1])) / vec2(size);
    float footprintMetres = depth * max(projectedTexel.x, projectedTexel.y);
    vec3 anchorRay = positions[anchor] / max(depth, .0001);
    float interfaceSlope = depth * dot(abs(normalVS.xy), projectedTexel)
        / max(.1, abs(dot(normalVS, anchorRay)));
    float tolerance = max(.05, max(min(.02 * depth, footprintMetres), min(.08 * depth, 1.5 * interfaceSlope)));
    float total = 0.0;
    int retained[4];
    int count = 0;
    for (int tap = 0; tap < 4; ++tap)
    {
        if (!eligible[tap] || abs(positions[tap].z - positions[anchor].z) > tolerance) continue;
        support.positionVS += positions[tap] * weights[tap];
        support.radiance += colors[tap] * weights[tap];
        total += weights[tap];
        retained[count++] = tap;
    }
    support.positionVS /= total;
    support.radiance /= total;
    support.precisionMetres = precisions[anchor];
    if (count >= 3)
    {
        vec3 a = positions[retained[1]] - positions[retained[0]];
        vec3 b = positions[retained[2]] - positions[retained[0]];
        vec3 crossProduct = cross(a, b);
        if (dot(crossProduct, crossProduct) > 1e-16)
        {
            support.normalVS = normalize(crossProduct);
            // Three taps authorize only their actual triangle. A missing corner
            // must not become a filled rectangle, even at full resolution.
            support.primaryTriangle = ivec3(retained[0], retained[1], retained[2]);
            support.secondaryTriangle = count == 4;
            support.planar = true;
            // A fourth tap on another local slope cannot authorize extrapolation.
            for (int tap = 0; tap < count; ++tap)
                if (abs(dot(positions[retained[tap]] - support.positionVS, support.normalVS))
                    > support.precisionMetres) support.planar = false;
        }
    }
    return true;
}

/** Supplies compatible geometry/radiance to UV and transport consumers without exposing traversal policy. */
bool VgeRefractionFilter(vec2 uv, vec3 surface, vec3 normalVS, mat4 inverseProjection,
    out vec3 positionVS, out vec3 radiance)
{
    VgeRefractionSupport support;
    bool valid = VgeRefractionFilterSupport(uv, surface, normalVS, inverseProjection, support);
    positionVS = support.positionVS;
    radiance = support.radiance;
    return valid;
}
#endif
