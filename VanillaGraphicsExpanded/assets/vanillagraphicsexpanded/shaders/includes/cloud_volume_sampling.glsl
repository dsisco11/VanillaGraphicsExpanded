#ifndef VGE_CLOUD_VOLUME_SAMPLING
#define VGE_CLOUD_VOLUME_SAMPLING
#ifdef VGE_CLOUD_PRIMARY_NOISE
uniform sampler2D cloudPrimaryNoise;
/** Uses the traced full-view position and an eight-frame spatial-mask index independently of reconstruction phase. */
float CloudPrimaryJitter(vec2 uv)
{
    ivec2 size=textureSize(cloudPrimaryNoise,0);
    ivec2 pixel=ivec2(floor(uv*vgeFrame.screenSize));
    int side=size.y/8,frame=int(vgeFrame.frameIndex%8u);
    return texelFetch(cloudPrimaryNoise,ivec2(pixel.x%size.x,pixel.y%side+frame*side),0).r;
}
#else
/** Keeps deterministic full-view validation independent of spatial sampling resources. */
float CloudPrimaryJitter(vec2 uv) { return .5; }
#endif
#endif
