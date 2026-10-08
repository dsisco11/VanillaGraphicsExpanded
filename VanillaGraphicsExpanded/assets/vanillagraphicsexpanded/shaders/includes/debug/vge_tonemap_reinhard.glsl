#ifndef LUMON_DEBUG_VGE_TONEMAP_REINHARD_GLSL
#define LUMON_DEBUG_VGE_TONEMAP_REINHARD_GLSL


/** Implements vge tonemap reinhard for its explicit view entrypoint. */
vec3 vgeTonemapReinhard(vec3 c)
{
    c = max(c, vec3(0.0));
    return c / (c + vec3(1.0));
}
#endif
