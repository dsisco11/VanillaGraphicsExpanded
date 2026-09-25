#ifndef VGE_VIEW_GLSL
#define VGE_VIEW_GLSL

// Shared with the vanilla terrain vertex stage and populated by the engine for this draw.
uniform mat4 modelViewMatrix;

/// Returns the fragment-to-eye vector in world axes from a render-relative terrain position.
vec3 VgeFragmentToEyeWorld(vec3 renderRelativePos)
{
    // The terrain view is a rigid rotation and translation. Its inverse translation locates
    // the eye relative to CameraPos, including bob and camera offsets, without moving the surface.
    vec3 eyeRenderRelative = -transpose(mat3(modelViewMatrix)) * modelViewMatrix[3].xyz;
    return eyeRenderRelative - renderRelativePos;
}

#endif
