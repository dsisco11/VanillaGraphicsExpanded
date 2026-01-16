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

#endif // VGE_NORMALDEPTH_GLSL
