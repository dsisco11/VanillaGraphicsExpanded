#ifndef VGE_VIEW_GLSL
#define VGE_VIEW_GLSL

// Engine terrain supplies a combined per-draw transform, including mini-dimension objects.
#ifndef VGE_VIEW_INPUTS
uniform mat4 modelViewMatrix;
#endif
#ifndef VGE_VIEW_MATRIX
#define VGE_VIEW_MATRIX modelViewMatrix
#endif

/// Returns the fragment-to-eye vector in world axes from a render-relative terrain position.
vec3 VgeFragmentToEyeWorld(vec3 renderRelativePos)
{
    // The terrain view is a rigid rotation and translation. Its inverse translation locates
    // the eye relative to CameraPos, including bob and camera offsets, without moving the surface.
    vec3 eyeRenderRelative = -transpose(mat3(VGE_VIEW_MATRIX)) * VGE_VIEW_MATRIX[3].xyz;
    return eyeRenderRelative - renderRelativePos;
}

#endif
