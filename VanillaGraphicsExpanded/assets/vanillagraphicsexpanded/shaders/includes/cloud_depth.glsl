#ifndef VGE_CLOUD_DEPTH
#define VGE_CLOUD_DEPTH
uniform sampler2D sceneDepth;
uniform sampler2D visibilityDepth;
uniform sampler2D liquidDepth;

/** Combines shared material depth with particle visibility, without sampling reduced HZB levels. */
float CloudOpaqueDepth(vec2 uv)
{
    // Receiver correction removes opaque-pass particles for lighting. Their visibility must still clip clouds.
    return min(textureLod(sceneDepth,uv,0.0).r,textureLod(visibilityDepth,uv,0.0).r);
}
#endif
