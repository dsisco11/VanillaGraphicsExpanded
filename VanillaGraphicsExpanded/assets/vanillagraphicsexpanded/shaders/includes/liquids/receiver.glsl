#ifndef VGE_WATER_RECEIVER_GLSL
#define VGE_WATER_RECEIVER_GLSL
#ifndef VGE_REFRACTION_EVENT
#define VGE_REFRACTION_EVENT(reason)
#endif
layout(binding = 9) uniform sampler2D vge_refractionColor;
layout(binding = 10) uniform sampler2D vge_refractionDepth;

/** Reconstructs one physical receiver at its original source pixel, including reduced backgrounds. */
bool VgeRefractionTap(ivec2 pixel, vec3 surface, vec3 normalVS, mat4 inverseProjection,
    out vec3 positionVS, out vec3 radiance, out int rejection)
{
    positionVS = vec3(0);
    radiance = vec3(0);
    rejection = 2;
    ivec2 size = textureSize(vge_refractionDepth, 0);
    if (any(lessThan(pixel, ivec2(0))) || any(greaterThanEqual(pixel, size))) return false;
    vec4 depth = texelFetch(vge_refractionDepth, pixel, 0);
    vec4 color = texelFetch(vge_refractionColor, pixel, 0);
    if (isnan(depth.r) || isinf(depth.r) || depth.r <= 0.0 || depth.r >= .999999 || color.a < .5) return false;
    if (any(isnan(color)) || any(isinf(color))) { rejection = 8; return false; }
    // Reduced texels retain the selected full-resolution UV; their cell center
    // is not the original ray and can move a silhouette through the water plane.
    bool reduced = any(notEqual(size, ivec2(frameSize)));
    vec2 uv = reduced ? depth.gb : (vec2(pixel) + .5) / vec2(size);
    if (reduced && (depth.a < .5 || any(isnan(uv)) || any(isinf(uv))
        || any(lessThanEqual(uv, vec2(0))) || any(greaterThanEqual(uv, vec2(1))))) return false;
    vec4 position = inverseProjection * vec4(uv * 2.0 - 1.0, depth.r * 2.0 - 1.0, 1.0);
    if (abs(position.w) < .00001 || any(isnan(position)) || any(isinf(position))) { rejection = 3; return false; }
    positionVS = position.xyz / position.w;
    if (dot(positionVS - surface, normalVS) >= -.02) { rejection = 4; return false; }
    radiance = color.rgb;
    return true;
}

/** Filters four spatial taps on one bounded depth layer, sharing weights between geometry and radiance. */
bool VgeRefractionFilter(vec2 uv, vec3 surface, vec3 normalVS, mat4 inverseProjection,
    out vec3 positionVS, out vec3 radiance)
{
    positionVS = vec3(0);
    radiance = vec3(0);
    ivec2 size = textureSize(vge_refractionDepth, 0);
    if (any(notEqual(size, textureSize(vge_refractionColor, 0))) || any(isnan(uv)) || any(isinf(uv))
        || any(lessThan(uv, vec2(0))) || any(greaterThan(uv, vec2(1))))
    { VGE_REFRACTION_EVENT(2); return false; }
    vec2 footprint = uv * vec2(size) - .5;
    ivec2 footprintOrigin = ivec2(floor(footprint));
    vec2 fraction = fract(footprint);
    vec3 positions[4];
    vec3 colors[4];
    float weights[4];
    float largestWeight = 0.0;
    int anchor = -1;
    int rejection = 2;
    for (int tap = 0; tap < 4; ++tap)
    {
        ivec2 offset = ivec2(tap & 1, tap >> 1);
        vec2 spatial = mix(1.0 - fraction, fraction, vec2(offset));
        weights[tap] = spatial.x * spatial.y;
        int reason;
        if (weights[tap] <= 0.0 || !VgeRefractionTap(footprintOrigin + offset, surface, normalVS,
            inverseProjection, positions[tap], colors[tap], reason))
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
    for (int tap = 0; tap < 4; ++tap)
    {
        if (weights[tap] <= 0.0 || abs(positions[tap].z - positions[anchor].z) > tolerance) continue;
        positionVS += positions[tap] * weights[tap];
        radiance += colors[tap] * weights[tap];
        total += weights[tap];
    }
    positionVS /= total;
    radiance /= total;
    return true;
}
#endif
