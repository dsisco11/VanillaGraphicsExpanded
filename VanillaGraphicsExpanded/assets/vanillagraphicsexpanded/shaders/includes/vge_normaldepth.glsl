#ifndef VGE_NORMALDEPTH_GLSL
#define VGE_NORMALDEPTH_GLSL

// Requires: `uniform sampler2D vge_normalDepthTex;`
// Encoding: RGBA16F = (normalXYZ_packed01, signedHeight)
// Height is generated from albedo during loading (tileable per texture rect).

vec4 ReadNormalDepth(vec2 uv)
{
    // Same UVs as terrainTex sampling.
    return texture(vge_normalDepthTex, uv);
}

#endif // VGE_NORMALDEPTH_GLSL
