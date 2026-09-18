#ifndef VGE_NORMALDEPTH_GLSL
#define VGE_NORMALDEPTH_GLSL

#ifndef VGE_PBR_ENABLE_NORMAL_MAPS
    #define VGE_PBR_ENABLE_NORMAL_MAPS 1
#endif

#ifndef VGE_PBR_NORMAL_MAP_SCALE
    #define VGE_PBR_NORMAL_MAP_SCALE 1.0
#endif

// Requires: `uniform sampler2D vge_normalDepthTex;`
// Encoding: RGBA16F = (normalXYZ_01, height01)
// Notes:
// - normal is stored in [0,1] (packed)
// - height is stored in [0,1] where 0.5 ~= 0 signed height
// - consumers can choose whether they want encoded (0..1) or signed (-1..1)
// Height is generated from albedo during loading (tileable per texture rect).

vec4 ReadNormalDepth01(vec2 uv)
{
    // Same UVs as terrainTex sampling.
    return texture(vge_normalDepthTex, uv);
}

float ReadHeight01(vec2 uv)
{
    return ReadNormalDepth01(uv).a;
}

float ReadHeightSigned(vec2 uv)
{
    return ReadHeight01(uv) * 2.0 - 1.0;
}

vec3 ReadNormal01(vec2 uv)
{
    return ReadNormalDepth01(uv).rgb;
}

vec3 ReadNormalSigned(vec2 uv)
{
    return ReadNormal01(uv) * 2.0 - 1.0;
}

vec4 ReadNormalDepth(vec2 uv)
{
    // Back-compat: return normal in 0..1 and height in signed -1..1.
    vec4 v = ReadNormalDepth01(uv);
    v.w = v.w * 2.0 - 1.0;
    return v;
}

bool VgeTryBuildTbnFromDerivatives(vec3 worldPosWs, vec2 uv, vec3 normalWs, out mat3 outTbn, out float outHandedness)
{
    vec3 n = normalize(normalWs);

    vec3 dpdx = dFdx(worldPosWs);
    vec3 dpdy = dFdy(worldPosWs);
    vec2 duvdx = dFdx(uv);
    vec2 duvdy = dFdy(uv);

    // Derivatives can be undefined at screen/primitive boundaries. Bail out on bad inputs.
    if (any(isnan(dpdx)) || any(isnan(dpdy)) || any(isnan(duvdx)) || any(isnan(duvdy)) ||
        any(isinf(dpdx)) || any(isinf(dpdy)) || any(isinf(duvdx)) || any(isinf(duvdy)))
    {
        outHandedness = 1.0;
        vec3 up = abs(n.y) < 0.999
            ? vec3(0.0, 1.0, 0.0)
            : vec3(1.0, 0.0, 0.0);

        vec3 tFallback = normalize(cross(up, n));
        vec3 bFallback = cross(n, tFallback);
        outTbn = mat3(tFallback, bFallback, n);
        return false;
    }

    // Stable cotangent-frame TBN (no determinant division).
    // This avoids quad-boundary flips that can happen when det ~ 0 or handedness tests change.
    vec3 dp2perp = cross(dpdy, n);
    vec3 dp1perp = cross(n, dpdx);

    vec3 t = dp2perp * duvdx.x + dp1perp * duvdy.x;
    vec3 b = dp2perp * duvdx.y + dp1perp * duvdy.y;

    float tLen2 = dot(t, t);
    float bLen2 = dot(b, b);
    float tbLen2 = max(tLen2, bLen2);

    vec3 up = abs(n.y) < 0.999
        ? vec3(0.0, 1.0, 0.0)
        : vec3(1.0, 0.0, 0.0);

    vec3 tFallback = normalize(cross(up, n));
    vec3 bFallback = cross(n, tFallback);

    // Blend out of degenerate derivative cases to avoid hard seams.
    float w = smoothstep(1e-16, 1e-10, tbLen2);
    t = mix(tFallback, t, w);
    b = mix(bFallback, b, w);

    // Orthonormalize.
    t = t - n * dot(n, t);
    float t2 = dot(t, t);
    if (t2 < 1e-16)
    {
        t = tFallback;
    }
    else
    {
        t *= inversesqrt(t2);
    }

    b = b - n * dot(n, b);
    b = b - t * dot(t, b);
    float b2 = dot(b, b);
    if (b2 < 1e-16)
    {
        b = bFallback;
    }
    else
    {
        b *= inversesqrt(b2);
    }

    outHandedness = 1.0;
    outTbn = mat3(t, b, n);
    return w > 0.0;
}

bool VgeIsNeutralNormalSigned(vec3 normalSigned)
{
    // Neutral for our bake is approximately (0,0,1) in signed space.
    return abs(normalSigned.x) < 1e-3 && abs(normalSigned.y) < 1e-3 && normalSigned.z > 0.999;
}

// NOTE: Intentionally no per-texel "strength" gating.
// Strength heuristics can introduce visible bands when mip levels change.

// TBN-reuse helper: caller supplies tangent frame and handedness (usually computed once per-fragment).
// Optionally also supplies the already-sampled atlas normal/height to avoid resampling.
vec4 VgeComputePackedWorldNormal01Height01_WithTbn(
    vec2 uv,
    vec3 geometricNormalWs,
    vec3 worldPosWs,
    mat3 tbn,
    float handedness,
    vec3 nAtlasSigned,
    float height01)
{
    vec3 nGeom = normalize(geometricNormalWs);

    // Tangent space convention: X=tangent, Y=bitangent, Z=normal.
    // Do not apply a separate handedness flip here. If UVs are mirrored, the TBN already encodes it.
    // Enforce a forward-facing tangent-space normal to avoid discontinuous hemisphere flips.
    nAtlasSigned.z = max(nAtlasSigned.z, 1e-3);
    nAtlasSigned = normalize(nAtlasSigned);
    vec3 nWsMap = normalize(tbn * nAtlasSigned);

    // VGE: Distance attenuation for normal-map contribution.
    // Assumption: `worldPosWs` is camera-relative world-space (common in VS terrain shaders),
    // so distance-to-camera is simply length(worldPosWs).
    const float VGE_NORMALMAP_FADE_START = 8.0;
    const float VGE_NORMALMAP_FADE_END = 24.0;
    float vge_dist = length(worldPosWs);
    float vge_normalMapWeight = 1.0 - smoothstep(VGE_NORMALMAP_FADE_START, VGE_NORMALMAP_FADE_END, vge_dist);
    float normalMapStrength = clamp(float(VGE_PBR_NORMAL_MAP_SCALE), 0.0, 4.0);
    float normalMapBlend = clamp(vge_normalMapWeight * normalMapStrength, 0.0, 1.0);
#if !VGE_PBR_ENABLE_NORMAL_MAPS
    normalMapBlend = 0.0;
#endif
    vec3 nWs = normalize(mix(nGeom, nWsMap, normalMapBlend));

    // Keep the result in the same hemisphere as the geometric normal, but do it continuously
    // (hard flips can produce visible slice lines when dot() crosses 0 due to tiny sampling changes).
    float hemi = dot(nWs, nGeom);
    if (hemi < 0.0)
    {
        nWs = normalize(mix(nWs, nGeom, clamp(-hemi, 0.0, 1.0)));
    }

    return vec4(nWs * 0.5 + 0.5, height01);
}

// Convenience overload: caller supplies TBN/handedness only; this helper will sample the atlas.
vec4 VgeComputePackedWorldNormal01Height01_WithTbn(vec2 uv, vec3 geometricNormalWs, vec3 worldPosWs, mat3 tbn, float handedness)
{
    vec3 nAtlasSigned = ReadNormalSigned(uv);
    float height01 = ReadHeight01(uv);
    return VgeComputePackedWorldNormal01Height01_WithTbn(uv, geometricNormalWs, worldPosWs, tbn, handedness, nAtlasSigned, height01);
}

// Returns a packed world-space normal (xyz in 0..1) + height01 (w).
// NOTE: The baked normal atlas encodes a UV-aligned normal (texture/heightmap-space).
// We derive a tangent frame from screen-space derivatives of world position and UV.
vec4 VgeComputePackedWorldNormal01Height01(vec2 uv, vec3 geometricNormalWs, vec3 worldPosWs)
{
    vec3 nGeom = normalize(geometricNormalWs);

    vec3 nAtlasSigned = ReadNormalSigned(uv);
    float height01 = ReadHeight01(uv);

    mat3 tbn;
    float handedness;
    VgeTryBuildTbnFromDerivatives(worldPosWs, uv, nGeom, tbn, handedness);

    // Delegate to the TBN-reuse helper.
    return VgeComputePackedWorldNormal01Height01_WithTbn(uv, nGeom, worldPosWs, tbn, handedness, nAtlasSigned, height01);
}

#endif // VGE_NORMALDEPTH_GLSL
