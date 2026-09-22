#ifndef LUMON_TRACE_SCENE_GLSL
#define LUMON_TRACE_SCENE_GLSL
#ifdef LUMON_TRACE_SCENE_COMPUTE
layout(std140, binding = 15) uniform LumOnNearFieldUBO
#else
layout(std140) uniform LumOnNearFieldUBO
#endif
{
    ivec4 nearFieldOriginResolution;
    ivec4 nearFieldBudget;
    vec4 localSupportedMin;
    vec4 localSupportedMax;
    ivec4 traceNearMin;
    ivec4 traceNearMax;
    ivec4 traceSurfaceMin;
    ivec4 traceSurfaceMax;
};
#ifdef LUMON_TRACE_SCENE_COMPUTE
layout(binding = 8) uniform usampler3D nearFieldGeometry;
#else
uniform usampler3D nearFieldGeometry;
#endif
#ifdef LUMON_TRACE_SCENE_COMPUTE
layout(binding = 12) uniform sampler3D nearFieldLight;
#else
uniform sampler3D nearFieldLight;
#endif
#ifdef LUMON_TRACE_SCENE_COMPUTE
layout(binding = 9) uniform usampler3D nearFieldRegions;
#else
uniform usampler3D nearFieldRegions;
#endif
#ifdef LUMON_TRACE_SCENE_COMPUTE
layout(binding = 13) uniform sampler2D nearFieldMaterials;
#else
uniform sampler2D nearFieldMaterials;
#endif

/** Maps signed cell or region coordinates into their bounded physical texture. */
ivec3 lumonNearFieldWrap(ivec3 cell, int size)
{
    // GLSL leaves integer remainder undefined for negative operands. Using only
    // nonnegative operands matters for our 48/80/144 rings, unlike power-of-two masks.
    // Supported world coordinates exclude INT_MIN, so abs is representable.
    ivec3 remainder = abs(cell) % size;
    return ivec3(
        cell.x < 0 && remainder.x != 0 ? size - remainder.x : remainder.x,
        cell.y < 0 && remainder.y != 0 ? size - remainder.y : remainder.y,
        cell.z < 0 && remainder.z != 0 ? size - remainder.z : remainder.z);
}

const int TRACE_SCENE_NEAR = 0;
const int TRACE_SCENE_SURFACE = 1;
const int TRACE_SCENE_READY = 0;
const int TRACE_SCENE_OUTSIDE = 1;
const int TRACE_SCENE_UNPUBLISHED = 2;
const int TRACE_SCENE_UNSUPPORTED = 3;
#ifdef LUMON_TRACE_SCENE_COMPUTE
layout(binding = 10) uniform usampler3D traceSceneLegacy;
#else
uniform usampler3D traceSceneLegacy;
#endif
#ifdef LUMON_TRACE_SCENE_COMPUTE
layout(binding = 11) uniform usampler2D traceSceneFaces;
#else
uniform usampler2D traceSceneFaces;
#endif

/** Checks logical coverage before physical mapping and publication readiness. */
int lumonTraceSceneReadGeometry(ivec3 cell, int domain, out uint geometry)
{
    geometry = 0u;
    ivec4 lo = domain == TRACE_SCENE_NEAR ? traceNearMin : traceSurfaceMin;
    ivec3 hi = domain == TRACE_SCENE_NEAR ? traceNearMax.xyz : traceSurfaceMax.xyz;
    if (lo.w == 0 || any(lessThan(cell, lo.xyz)) || any(greaterThanEqual(cell, hi))) return TRACE_SCENE_OUTSIDE;
    int size = nearFieldOriginResolution.w;
    ivec3 relative = cell - nearFieldOriginResolution.xyz;
    if (size <= 0 || any(lessThan(relative, ivec3(0))) || any(greaterThanEqual(relative, ivec3(size)))) return TRACE_SCENE_UNPUBLISHED;
    int cellSize = nearFieldBudget.y;
    if (cellSize <= 0) return TRACE_SCENE_UNPUBLISHED;
    ivec3 region = (cell - lumonNearFieldWrap(cell, cellSize)) / cellSize;
    if (texelFetch(nearFieldRegions, lumonNearFieldWrap(region, size / cellSize), 0).r == 0u) return TRACE_SCENE_UNPUBLISHED;
    geometry = texelFetch(nearFieldGeometry, lumonNearFieldWrap(cell, size), 0).r;
    uint kind = geometry & 3u;
    return kind == 0u ? TRACE_SCENE_UNPUBLISHED : kind == 3u ? TRACE_SCENE_UNSUPPORTED : TRACE_SCENE_READY;
}

/** Near-field geometry consumers reject unsupported shapes without interpreting them as air. */
bool lumonNearFieldReadGeometry(ivec3 cell, out uint geometry)
{
    return lumonTraceSceneReadGeometry(cell, TRACE_SCENE_NEAR, geometry) == TRACE_SCENE_READY;
}

/** Allows material capture of published unsupported shapes without making them traversable. */
bool lumonTraceSceneReadSurface(ivec3 cell, uint face, out uint surfaceId)
{
    surfaceId = 0u;
    uint geometry;
    int status = lumonTraceSceneReadGeometry(cell, TRACE_SCENE_SURFACE, geometry);
    if (status != TRACE_SCENE_READY && status != TRACE_SCENE_UNSUPPORTED) return false;
    if ((geometry & 3u) == 1u) return true;
    uint material = geometry >> 2;
    if (material == 0u) return false;
    uvec4 faces = texelFetch(traceSceneFaces, ivec2(int(material), 0), 0);
    if ((faces.w & 1u) == 0u) return false;
    surfaceId = (faces[int(face / 2u)] >> ((face % 2u) * 16u)) & 65535u;
    return surfaceId != 0u;
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
