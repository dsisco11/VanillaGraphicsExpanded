#ifndef VGE_NORMALDEPTH_GLSL
#define VGE_NORMALDEPTH_GLSL

// Requires: `uniform sampler2D vge_normalDepthTex;`
// Encoding: RGBA16F = (normalXYZ_packed01, signedHeight01)
// Height is generated from albedo during loading (tileable per texture rect).

vec4 ReadNormalDepth(vec2 uv)
{
    // Same UVs as terrainTex sampling.
    vec4 v = texture(vge_normalDepthTex, uv);
    // Decode height back to signed range for consumers.
    v.w = v.w * 2.0 - 1.0;
    return v;
}

#endif // VGE_NORMALDEPTH_GLSL
