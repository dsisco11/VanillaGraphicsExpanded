#ifndef LUMON_DEBUG_GET_VIEW_MATRIX_GLSL
#define LUMON_DEBUG_GET_VIEW_MATRIX_GLSL


/** Implements get view matrix for its explicit view entrypoint. */
mat4 getViewMatrix()
{
    return inverse(invViewMatrix);
}
#endif
