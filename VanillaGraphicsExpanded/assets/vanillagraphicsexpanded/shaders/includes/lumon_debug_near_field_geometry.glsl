#ifndef LUMON_DEBUG_NEAR_FIELD_GEOMETRY_GLSL
#define LUMON_DEBUG_NEAR_FIELD_GEOMETRY_GLSL
@import "./lumon_near_field_scene.glsl"

/** Traces camera rays through the uploaded occupancy ring, exposing unavailable cells instead of skipping them. */
vec4 renderNearFieldGeometryDebug()
{
    int size = nearFieldOriginResolution.w;
    if (size <= 0) return vec4(0.1, 0.2, 0.8, 1.0);
    // Inverse-view translation is the camera position relative to the player origin.
    // Retain the absolute chunk component as integers; never convert the world origin to float.
    vec3 camera = (invViewMatrix * vec4(0.0, 0.0, 0.0, 1.0)).xyz + matrixSpaceWorldBlockOffsetRem;
    ivec3 cameraCell = ivec3(floor(camera)) + matrixSpaceWorldChunkCoordOffset * 32;
    vec3 origin = vec3(cameraCell - nearFieldOriginResolution.xyz) + fract(camera);
    vec3 viewRay = lumonReconstructViewPos(uv, 0.5, invProjectionMatrix);
    vec3 direction = normalize((invViewMatrix * vec4(normalize(viewRay), 0.0)).xyz);
    // Clip to the local volume so observers outside its bounds can still inspect it.
    float enter = 0.0;
    float leave = 1e30;
    for (int axis = 0; axis < 3; axis++)
    {
        if (abs(direction[axis]) < 1e-8)
        {
            if (origin[axis] < 0.0 || origin[axis] >= float(size)) return vec4(0.0, 0.2, 0.8, 1.0);
        }
        else
        {
            float a = -origin[axis] / direction[axis];
            float b = (float(size) - origin[axis]) / direction[axis];
            enter = max(enter, min(a, b));
            leave = min(leave, max(a, b));
        }
    }
    if (leave <= enter) return vec4(0.0, 0.2, 0.8, 1.0);
    vec3 start = clamp(origin + direction * (enter + 0.0001), vec3(0.0), vec3(float(size) - 0.0001));
    ivec3 cell = ivec3(floor(start));
    ivec3 stepCell = ivec3(sign(direction));
    vec3 delta = vec3(1e30);
    vec3 next = vec3(1e30);
    for (int axis = 0; axis < 3; axis++)
    {
        if (abs(direction[axis]) > 1e-8)
        {
            delta[axis] = 1.0 / abs(direction[axis]);
            next[axis] = (direction[axis] > 0.0 ? 1.0 - fract(start[axis]) : fract(start[axis])) * delta[axis];
        }
    }
    int face = abs(direction.x) >= abs(direction.y) && abs(direction.x) >= abs(direction.z) ? 0 : (abs(direction.y) >= abs(direction.z) ? 1 : 2);
    float distance = 0.0;
    // This viewer has a separate budget: it must expose the volume even if production rays terminate sooner.
    for (int i = 0; i < 2048; i++)
    {
        if (any(lessThan(cell, ivec3(0))) || any(greaterThanEqual(cell, ivec3(size)))) return vec4(0.0, 0.0, 0.0, 1.0);
        ivec3 worldCell = nearFieldOriginResolution.xyz + cell;
        int cellSize = nearFieldBudget.y;
        ivec3 region = lumonNearFieldWrap(cell / cellSize + nearFieldOriginResolution.xyz / cellSize, size / cellSize);
        if (texelFetch(nearFieldRegions, region, 0).r == 0u) return vec4(0.5, 0.0, 1.0, 1.0);
        uint geometry = texelFetch(nearFieldGeometry, lumonNearFieldWrap(worldCell, size), 0).r;
        uint state = geometry & 3u;
        if (state != 1u)
        {
            if (state != 2u) return vec4(1.0, 0.0, 1.0, 1.0);
            vec3 color = (geometry >> 2) == 0u ? vec3(1.0, 0.45, 0.0) : vec3(0.1, 0.85, 1.0);
            vec3 fraction = fract(start + direction * distance);
            vec2 surface = face == 0 ? fraction.yz : (face == 1 ? fraction.xz : fraction.xy);
            vec2 edge = min(surface, 1.0 - surface);
            float grid = min(edge.x, edge.y) < 0.025 ? 0.45 : 1.0;
            float shade = face == 1 ? 1.0 : (face == 0 ? 0.75 : 0.55);
            // White lines mark publication-cell boundaries on occupied surfaces; fine dark lines remain voxel edges.
            vec3 publication = vec3(lumonNearFieldWrap(worldCell, cellSize)) + fraction;
            vec2 publicationSurface = face == 0 ? publication.yz : (face == 1 ? publication.xz : publication.xy);
            vec2 publicationEdge = min(publicationSurface, float(cellSize) - publicationSurface);
            if (min(publicationEdge.x, publicationEdge.y) < 0.06) return vec4(vec3(shade), 1.0);
            return vec4(color * shade * grid, 1.0);
        }
        float boundary = min(next.x, min(next.y, next.z));
        // Match the production traversal's one-face-at-a-time tie handling.
        for (int axis = 0; axis < 3; axis++)
        {
            if (next[axis] <= boundary)
            {
                cell[axis] += stepCell[axis];
                next[axis] += delta[axis];
                face = axis;
                break;
            }
        }
        distance = boundary;
    }
    return vec4(1.0, 1.0, 0.0, 1.0);
}
#endif
