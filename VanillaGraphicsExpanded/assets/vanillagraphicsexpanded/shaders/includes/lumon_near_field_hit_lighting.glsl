#ifndef LUMON_NEAR_FIELD_HIT_LIGHTING_GLSL
#define LUMON_NEAR_FIELD_HIT_LIGHTING_GLSL
@import "./lumon_near_field_trace.glsl"
@import "./lumon_surface_lighting.glsl"

/** Consumes outgoing radiance once; unavailable lighting does not erase the opaque geometric hit. */
bool lumonShadeNearFieldHit(LumonNearFieldHit hit, float traceDistance, float emissionBoost, out vec3 radiance)
{
    radiance = vec3(0);
    if (any(lessThanEqual(lighting.slotDimensions.xyz, ivec3(0)))) return false;
    uint face = hit.normal.x > 0 ? 1u : hit.normal.x < 0 ? 3u :
        hit.normal.y > 0 ? 4u : hit.normal.y < 0 ? 5u : hit.normal.z > 0 ? 2u : 0u;
    uint surface;
    return lumonTraceSceneReadSurface(hit.cell, face, surface) &&
        sampleSurfaceLighting(hit.cell, hit.normal, hit.fraction, surface, radiance);
}
#endif
