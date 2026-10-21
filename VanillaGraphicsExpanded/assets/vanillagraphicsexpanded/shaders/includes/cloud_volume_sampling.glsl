#ifndef VGE_CLOUD_VOLUME_SAMPLING
#define VGE_CLOUD_VOLUME_SAMPLING
#ifdef VGE_CLOUD_PRIMARY_NOISE
@import "./blue_noise.glsl"
uniform sampler2D cloudPrimaryNoise;
/** Uses the traced full-view position and an eight-frame spatial-mask index independently of reconstruction phase. */
float CloudPrimaryJitter(vec2 uv)
{
    return BlueNoiseRank(cloudPrimaryNoise,ivec2(floor(uv*vgeFrame.screenSize)),vgeFrame.frameIndex,8);
}
#else
/** Keeps deterministic full-view validation independent of spatial sampling resources. */
float CloudPrimaryJitter(vec2 uv) { return .5; }
#endif
#endif
