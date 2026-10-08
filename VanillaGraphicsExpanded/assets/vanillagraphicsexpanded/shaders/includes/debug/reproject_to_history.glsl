#ifndef LUMON_DEBUG_REPROJECT_TO_HISTORY_GLSL
#define LUMON_DEBUG_REPROJECT_TO_HISTORY_GLSL


/** Implements reproject to history for its explicit view entrypoint. */
vec2 reprojectToHistory(vec3 renderRelativePos)
{
    vec4 prevClip = prevViewProjMatrix * vec4(renderRelativePos, 1.0);
    vec3 prevNDC = prevClip.xyz / prevClip.w;
    return prevNDC.xy * 0.5 + 0.5;
}
#endif
