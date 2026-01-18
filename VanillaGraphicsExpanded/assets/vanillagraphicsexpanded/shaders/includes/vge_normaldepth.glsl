#ifndef VGE_NORMALDEPTH_GLSL
#define VGE_NORMALDEPTH_GLSL

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

    float det = duvdx.x * duvdy.y - duvdx.y * duvdy.x;
    if (abs(det) < 1e-10)
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

    // UV determinant sign indicates whether the UV mapping is mirrored.
    // We keep TBN right-handed and apply this sign to the tangent-space normal (Y) at use-site.
    outHandedness = det < 0.0 ? -1.0 : 1.0;

    float invDet = 1.0 / det;

    vec3 t = (dpdx * duvdy.y - dpdy * duvdx.y) * invDet;
    vec3 b = (dpdy * duvdx.x - dpdx * duvdy.x) * invDet;

    // Orthonormalize against the geometric normal for stability.
    t = t - n * dot(n, t);
    float tLen2 = dot(t, t);
    if (tLen2 < 1e-12)
    {
        vec3 up = abs(n.y) < 0.999
            ? vec3(0.0, 1.0, 0.0)
            : vec3(1.0, 0.0, 0.0);

        vec3 tFallback = normalize(cross(up, n));
        vec3 bFallback = cross(n, tFallback);
        outTbn = mat3(tFallback, bFallback, n);
        return false;
    }
    t *= inversesqrt(tLen2);

    // Right-handed TBN.
    vec3 bOrtho = normalize(cross(n, t));
    outTbn = mat3(t, bOrtho, n);
    return true;
}

bool VgeIsNeutralNormalSigned(vec3 normalSigned)
{
    // Neutral for our bake is approximately (0,0,1) in signed space.
    return abs(normalSigned.x) < 1e-3 && abs(normalSigned.y) < 1e-3 && normalSigned.z > 0.999;
}

// Returns a packed world-space normal (xyz in 0..1) + height01 (w).
// NOTE: The baked normal atlas encodes a UV-aligned normal (texture/heightmap-space).
// We derive a tangent frame from screen-space derivatives of world position and UV.
vec4 VgeComputePackedWorldNormal01Height01(vec2 uv, vec3 geometricNormalWs, vec3 worldPosWs)
{
    vec3 nGeom = normalize(geometricNormalWs);

    vec3 nAtlasSigned = ReadNormalSigned(uv);
    float height01 = ReadHeight01(uv);

    if (VgeIsNeutralNormalSigned(nAtlasSigned))
    {
        return vec4(nGeom * 0.5 + 0.5, height01);
    }

    mat3 tbn;
    float handedness;
    VgeTryBuildTbnFromDerivatives(worldPosWs, uv, nGeom, tbn, handedness);

    // Apply UV-handedness to the tangent-space normal to avoid mirrored UVs producing inverted bumps.
    // Tangent space convention: X=tangent, Y=bitangent, Z=normal.
    nAtlasSigned.y *= handedness;
    vec3 nWs = normalize(tbn * nAtlasSigned);

    // VGE: Distance attenuation for normal-map contribution.
    // Assumption: `worldPosWs` is camera-relative world-space (common in VS terrain shaders),
    // so distance-to-camera is simply length(worldPosWs).
    const float VGE_NORMALMAP_FADE_START = 8.0;
    const float VGE_NORMALMAP_FADE_END = 24.0;
    float vge_dist = length(worldPosWs);
    float vge_normalMapWeight = 1.0 - smoothstep(VGE_NORMALMAP_FADE_START, VGE_NORMALMAP_FADE_END, vge_dist);
    nWs = normalize(mix(nGeom, nWs, vge_normalMapWeight));

    // Keep the result in the same hemisphere as the geometric normal (stability).
    if (dot(nWs, nGeom) < 0.0)
    {
        nWs = -nWs;
    }

    return vec4(nWs * 0.5 + 0.5, height01);
}

#endif // VGE_NORMALDEPTH_GLSL
