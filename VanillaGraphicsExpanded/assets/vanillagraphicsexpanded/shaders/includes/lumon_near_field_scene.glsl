#ifndef LUMON_NEAR_FIELD_SCENE_GLSL
#define LUMON_NEAR_FIELD_SCENE_GLSL
layout(std140) uniform LumOnNearFieldUBO
{
    ivec4 nearFieldOriginResolution;
    ivec4 nearFieldBudget;
    vec4 localSupportedMin;
    vec4 localSupportedMax;
};
uniform usampler3D nearFieldGeometry;
uniform sampler3D nearFieldLight;
uniform usampler3D nearFieldRegions;
uniform sampler2D nearFieldMaterials;

/** Maps signed cell or region coordinates into their bounded physical texture. */
ivec3 lumonNearFieldWrap(ivec3 cell, int size)
{
    return ((cell % size) + size) % size;
}

/** Distinguishes published empty cells from missing, stale or unsupported geometry. */
bool lumonNearFieldReadGeometry(ivec3 cell, out uint geometry)
{
    geometry = 0u;
    int size = nearFieldOriginResolution.w;
    if (size <= 0) return false;
    ivec3 relative = cell - nearFieldOriginResolution.xyz;
    if (any(lessThan(relative, ivec3(0))) || any(greaterThanEqual(relative, ivec3(size)))) return false;
    // The anchor determines the ring offset, avoiding another independently updated uniform.
    // CPU publication clears evicted slot readiness before this anchor becomes visible.
    int cellSize = nearFieldBudget.y;
    if (cellSize <= 0) return false;
    ivec3 ringOffset = lumonNearFieldWrap(nearFieldOriginResolution.xyz / cellSize, size / cellSize);
    ivec3 regionTexel = lumonNearFieldWrap((relative / cellSize) + ringOffset, size / cellSize);
    if (texelFetch(nearFieldRegions, regionTexel, 0).r == 0u) return false;
    ivec3 texel = lumonNearFieldWrap(relative + ringOffset * cellSize, size);
    geometry = texelFetch(nearFieldGeometry, texel, 0).r;
    return (geometry & 3u) == 1u || (geometry & 3u) == 2u;
}
/** Rejects screen-probe origins outside the configured domain before claiming local coverage. */
bool lumonNearFieldOriginSupported(ivec3 cell, vec3 fraction, float traceDistance)
{
    if (nearFieldBudget.z == 0) return true;
    vec3 relative = vec3(cell - nearFieldOriginResolution.xyz) + fraction;
    return all(greaterThanEqual(relative, localSupportedMin.xyz)) && all(lessThan(relative, localSupportedMax.xyz)) &&
        traceDistance <= localSupportedMin.w;
}
/** Reads normalized hit lighting only after geometry publication is established. */
bool lumonNearFieldRead(ivec3 cell, out uint geometry, out vec4 light)
{
    light = vec4(0.0);
    if (!lumonNearFieldReadGeometry(cell, geometry)) return false;
    light = texelFetch(nearFieldLight, lumonNearFieldWrap(cell, nearFieldOriginResolution.w), 0);
    return true;
}
#endif
