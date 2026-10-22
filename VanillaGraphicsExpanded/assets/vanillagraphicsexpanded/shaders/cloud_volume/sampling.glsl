#ifndef VGE_STORED_VOLUME_SAMPLING
#define VGE_STORED_VOLUME_SAMPLING
@import "../includes/blue_noise.glsl"
@import "../includes/interleaved_sampling.glsl"
/** Shares the exact compact-ray address between tracing and fresh-observation classification. */
ivec2 StoredRefreshLocation(sampler2D noise,ivec2 tile,uint frame,ivec2 size)
{
    uint sweep=(frame/4u)%8u;
    return InterleavedSampleLocation2x2(BlueNoiseRank(noise,tile,sweep,8),tile,frame,size);
}
#endif
