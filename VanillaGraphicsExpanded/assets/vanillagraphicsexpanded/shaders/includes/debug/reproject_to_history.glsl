#ifndef LUMON_DEBUG_REPROJECT_TO_HISTORY_GLSL
#define LUMON_DEBUG_REPROJECT_TO_HISTORY_GLSL
@import "../camera_reprojection.glsl"

/** Implements reproject to history for its explicit view entrypoint. */
vec2 reprojectToHistory(vec3 renderRelativePos)
{
    vec4 prevClip = CameraProjectPosition(vgeFrame.prevViewProjMatrix,renderRelativePos);
    return CameraClipUv(prevClip);
}
#endif
