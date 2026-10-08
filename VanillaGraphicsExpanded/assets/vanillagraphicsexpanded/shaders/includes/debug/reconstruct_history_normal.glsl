#ifndef LUMON_DEBUG_RECONSTRUCT_HISTORY_NORMAL_GLSL
#define LUMON_DEBUG_RECONSTRUCT_HISTORY_NORMAL_GLSL


/** Implements reconstruct history normal for its explicit view entrypoint. */
vec3 reconstructHistoryNormal(vec2 historyNormal2D, vec3 currentNormal)
{
    float z2 = max(1.0 - dot(historyNormal2D, historyNormal2D), 0.0);
    float z = sqrt(z2);
    float zSign = (currentNormal.z >= 0.0) ? 1.0 : -1.0;
    return normalize(vec3(historyNormal2D, z * zSign));
}
#endif
