#ifndef LUMON_LOCAL_SCENE_GLSL
#define LUMON_LOCAL_SCENE_GLSL
layout(std140) uniform LumOnLocalTraceUBO
{
    ivec4 localOriginResolution;
    ivec4 localBudget;
};
uniform usampler3D localTraceGeometry;
uniform sampler3D localTraceLight;
uniform usampler3D localTraceRegions;
uniform sampler2D localTraceMaterials;

/** Maps signed cell or region coordinates into their bounded physical texture. */
ivec3 lumonLocalWrap(ivec3 cell, int size)
{
    return ((cell % size) + size) % size;
}

/** Distinguishes published empty cells from missing, stale or unsupported geometry. */
bool lumonLocalRead(ivec3 cell, out uint geometry, out vec4 light)
{
    geometry = 0u;
    light = vec4(0.0);
    int size = localOriginResolution.w;
    if (size <= 0) return false;
    ivec3 relative = cell - localOriginResolution.xyz;
    if (any(lessThan(relative, ivec3(0))) || any(greaterThanEqual(relative, ivec3(size)))) return false;
    // The anchor determines the ring offset, avoiding another independently updated uniform.
    // CPU publication clears evicted slot readiness before this anchor becomes visible.
    ivec3 ringOffset = lumonLocalWrap(localOriginResolution.xyz >> 5, size / 32);
    ivec3 regionTexel = lumonLocalWrap((relative >> 5) + ringOffset, size / 32);
    if (texelFetch(localTraceRegions, regionTexel, 0).r == 0u) return false;
    ivec3 texel = lumonLocalWrap(relative + ringOffset * 32, size);
    geometry = texelFetch(localTraceGeometry, texel, 0).r;
    light = texelFetch(localTraceLight, texel, 0);
    return (geometry & 3u) == 1u || (geometry & 3u) == 2u;
}
#endif
