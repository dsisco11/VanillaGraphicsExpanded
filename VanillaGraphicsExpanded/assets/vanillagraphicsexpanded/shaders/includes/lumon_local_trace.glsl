#ifndef LUMON_LOCAL_TRACE_GLSL
#define LUMON_LOCAL_TRACE_GLSL
@import "./lumon_local_scene.glsl"
const int LUMON_LOCAL_UNAVAILABLE = 0;
const int LUMON_LOCAL_HIT = 1;
const int LUMON_LOCAL_CLEAR = 2;
const int LUMON_LOCAL_BUDGET = 3;

/** Local ray result; geometry hits remain hits even when their lighting is unavailable. */
struct LumonLocalHit
{
    int outcome;
    ivec3 cell;
    ivec3 normal;
    vec3 fraction;
    float distance;
    uint material;
};

/** Traverses integer world cells while retaining small fractional arithmetic for large-world precision. */
LumonLocalHit lumonTraceLocal(ivec3 startCell, vec3 fraction, vec3 direction, float maxDistance)
{
    LumonLocalHit result;
    result.outcome = LUMON_LOCAL_UNAVAILABLE;
    result.cell = startCell;
    result.normal = ivec3(0);
    result.fraction = fraction;
    result.distance = 0.0;
    result.material = 0u;
    if (maxDistance <= 0.0 || dot(direction, direction) < 1e-12) return result;
    vec3 dir = normalize(direction);
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
    for (int i = 0; i < 512; i++)
    {
        if (i >= localBudget.x) { result.outcome = LUMON_LOCAL_BUDGET; return result; }
        uint geometry;
        if (!lumonLocalReadGeometry(result.cell, geometry)) return result;
        if ((geometry & 3u) == 2u)
        {
            result.outcome = LUMON_LOCAL_HIT;
            result.material = geometry >> 2;
            result.fraction = fraction + dir * result.distance - vec3(result.cell - startCell);
            return result;
        }
        float boundary = min(next.x, min(next.y, next.z));
        if (boundary >= maxDistance)
        {
            result.outcome = LUMON_LOCAL_CLEAR;
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
    }
    result.outcome = LUMON_LOCAL_BUDGET;
    return result;
}
#endif
