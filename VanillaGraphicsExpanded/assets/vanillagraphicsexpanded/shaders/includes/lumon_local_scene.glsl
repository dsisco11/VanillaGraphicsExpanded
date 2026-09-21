#ifndef LUMON_LOCAL_SCENE_GLSL
#define LUMON_LOCAL_SCENE_GLSL
layout(std140) uniform LumOnLocalTraceUBO
{
    ivec4 localOriginResolution;
    ivec4 localBudget;
    vec4 localSupportedMin;
    vec4 localSupportedMax;
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
bool lumonLocalReadGeometry(ivec3 cell, out uint geometry)
{
    geometry = 0u;
    int size = localOriginResolution.w;
    if (size <= 0) return false;
    ivec3 relative = cell - localOriginResolution.xyz;
    if (any(lessThan(relative, ivec3(0))) || any(greaterThanEqual(relative, ivec3(size)))) return false;
    // The anchor determines the ring offset, avoiding another independently updated uniform.
    // CPU publication clears evicted slot readiness before this anchor becomes visible.
    int cellSize = localBudget.y;
    if (cellSize <= 0) return false;
    ivec3 ringOffset = lumonLocalWrap(localOriginResolution.xyz / cellSize, size / cellSize);
    ivec3 regionTexel = lumonLocalWrap((relative / cellSize) + ringOffset, size / cellSize);
    if (texelFetch(localTraceRegions, regionTexel, 0).r == 0u) return false;
    ivec3 texel = lumonLocalWrap(relative + ringOffset * cellSize, size);
    geometry = texelFetch(localTraceGeometry, texel, 0).r;
    return (geometry & 3u) == 1u || (geometry & 3u) == 2u;
}
/** Rejects screen-probe origins outside the configured domain before claiming local coverage. */
bool lumonLocalOriginSupported(ivec3 cell, vec3 fraction, float traceDistance)
{
    if (localBudget.z == 0) return true;
    vec3 relative = vec3(cell - localOriginResolution.xyz) + fraction;
    return all(greaterThanEqual(relative, localSupportedMin.xyz)) && all(lessThan(relative, localSupportedMax.xyz)) &&
        traceDistance <= localSupportedMin.w;
}
/** Reads normalized hit lighting only after geometry publication is established. */
bool lumonLocalRead(ivec3 cell, out uint geometry, out vec4 light)
{
    light = vec4(0.0);
    if (!lumonLocalReadGeometry(cell, geometry)) return false;
    light = texelFetch(localTraceLight, lumonLocalWrap(cell, localOriginResolution.w), 0);
    return true;
}
#endif
