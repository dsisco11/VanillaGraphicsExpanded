#ifndef VGE_INTERLEAVED_SAMPLING
#define VGE_INTERLEAVED_SAMPLING
/** Visits each pixel of a 2x2 tile in four frames, using a rank in [0,1) to offset its phase and clamping partial edge tiles. */
ivec2 InterleavedSampleLocation2x2(float rank,ivec2 tile,uint frame,ivec2 size)
{
    int phase=(int(rank*4.0)+int(frame%4u))%4;
    return min(tile*2+ivec2(phase&1,phase>>1),size-1);
}
#endif
