#ifndef LUMON_TRACE_SCENE_TRACE_GLSL
#define LUMON_TRACE_SCENE_TRACE_GLSL
@import "./lumon_trace_scene.glsl"
const int LUMON_NEAR_FIELD_UNAVAILABLE = 0;
const int LUMON_NEAR_FIELD_HIT = 1;
// CLEAR proves only the requested finite segment. A consumer needs an independent
// distant-light result; neither CLEAR, BUDGET nor UNAVAILABLE establishes sky visibility.
const int LUMON_NEAR_FIELD_CLEAR = 2;
const int LUMON_NEAR_FIELD_BUDGET = 3;
const int LUMON_NEAR_FIELD_SKY = 4;

/** Local ray result; geometry hits remain hits even when their lighting is unavailable. */
struct LumonTraceSceneHit
{
    int outcome;
    int reason;
    ivec3 cell;
    ivec3 normal;
    vec3 fraction;
    float distance;
    uint material;
};

/** Traverses integer cells; a positive worldHeight optionally enables proven upper-boundary sky completion. */
LumonTraceSceneHit lumonTraceScene(ivec3 startCell, vec3 fraction, vec3 direction, float maxDistance, int maxSteps, int domain, int worldHeight)
{
    LumonTraceSceneHit result;
    result.outcome = LUMON_NEAR_FIELD_UNAVAILABLE;
    result.reason = TRACE_SCENE_UNPUBLISHED;
    result.cell = startCell;
    result.normal = ivec3(0);
    result.fraction = fraction;
    result.distance = 0.0;
    result.material = 0u;
    if (maxDistance <= 0.0 || dot(direction, direction) < 1e-12) return result;
    vec3 dir = normalize(direction);
    bool hasSkyBoundary = worldHeight > 0 && startCell.y >= 0 && dir.y > 0.0;
    if (hasSkyBoundary && startCell.y >= worldHeight)
    {
        result.outcome = LUMON_NEAR_FIELD_SKY;
        return result;
    }
    ivec3 stepCell = ivec3(sign(dir));
    vec3 delta = vec3(1e30);
    vec3 next = vec3(1e30);
    for (int axis = 0; axis < 3; axis++)
    {
        if (abs(dir[axis]) > 1e-8)
        {
            delta[axis] = 1.0 / abs(dir[axis]);
            next[axis] = (dir[axis] > 0.0 ? 1.0 - fraction[axis] : fraction[axis]) * delta[axis];
        }
    }
    // A start inside a solid is an immediate opaque hit, never an ignored first cell.
    int dominant = abs(dir.x) >= abs(dir.y) && abs(dir.x) >= abs(dir.z) ? 0 : (abs(dir.y) >= abs(dir.z) ? 1 : 2);
    result.normal[dominant] = -stepCell[dominant];
    // Keep the caller's runtime budget in the loop condition. A constant 512-iteration
    // loop can be unrolled into every visibility query and exceed driver instruction limits.
    int stepLimit = clamp(maxSteps, 0, 512);
    for (int i = 0; i < stepLimit; i++)
    {
        uint geometry;
        int status = lumonTraceSceneReadGeometry(result.cell, domain, geometry);
        if (status != TRACE_SCENE_READY) { result.reason = status; return result; }
        if ((geometry & 3u) == 2u)
        {
            result.outcome = LUMON_NEAR_FIELD_HIT;
            result.material = geometry >> 2;
            result.fraction = fraction + dir * result.distance - vec3(result.cell - startCell);
            return result;
        }
        float boundary = min(next.x, min(next.y, next.z));
        if (boundary >= maxDistance)
        {
            result.outcome = LUMON_NEAR_FIELD_CLEAR;
            result.distance = maxDistance;
            return result;
        }
        result.distance = boundary;
        result.normal = ivec3(0);
        // Resolve tied boundaries one face at a time. This retains the adjacent outside cell
        // for lighting at voxel edges instead of selecting another wall as the light source.
        for (int axis = 0; axis < 3; axis++)
        {
            if (next[axis] <= boundary)
            {
                result.cell[axis] += stepCell[axis];
                result.normal[axis] = -stepCell[axis];
                next[axis] += delta[axis];
                break;
            }
        }
        // Check the integer boundary after the last verified air cell, before any next
        // geometry read (even on the final budgeted step). A float distance-to-plane
        // comparison can round differently from DDA and sample geometry above the world.
        if (hasSkyBoundary && result.cell.y >= worldHeight)
        {
            result.outcome = LUMON_NEAR_FIELD_SKY;
            return result;
        }
    }
    result.outcome = LUMON_NEAR_FIELD_BUDGET;
    return result;
}
#endif
