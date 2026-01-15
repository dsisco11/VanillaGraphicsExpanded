#ifndef VGE_NORMALDEPTH_GLSL
#define VGE_NORMALDEPTH_GLSL

// Requires: `uniform sampler2D vge_normalDepthTex;`
// Provisional encoding (plumbing stage): RGBA16F = (normalXYZ_packed01, depth01)

vec4 ReadNormalDepth(vec2 uv)
{
    // Same UVs as terrainTex sampling.
    return texture(vge_normalDepthTex, uv);
}

#endif // VGE_NORMALDEPTH_GLSL
