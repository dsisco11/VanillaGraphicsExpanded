#ifndef LUMON_DEBUG_WORLD_TO_VIEW_POS_GLSL
#define LUMON_DEBUG_WORLD_TO_VIEW_POS_GLSL
@import "./get_view_matrix.glsl"

/** Implements world to view pos for its explicit view entrypoint. */
vec3 worldToViewPos(vec3 posWS)
{
    return (getViewMatrix() * vec4(posWS, 1.0)).xyz;
}
#endif
