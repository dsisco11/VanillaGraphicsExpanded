#ifndef VGE_INTERLEAVED_SAMPLING
#define VGE_INTERLEAVED_SAMPLING
/** Visits each pixel of a 2x2 tile in four frames, using a rank in [0,1) to offset its phase and clamping partial edge tiles. */
ivec2 InterleavedSampleLocation2x2(float rank,ivec2 tile,uint frame,ivec2 size)
{
    int phase=(int(rank*4.0)+int(frame%4u))%4;
    return min(tile*2+ivec2(phase&1,phase>>1),size-1);
}
/** Counts fresh opportunities after a cold full-grid observation, including repeated clamped edge phases. */
uint InterleavedObservationIndex2x2(float rank,ivec2 pixel,ivec2 size,uint frame,uint epoch)
{
    uint elapsed=frame-epoch,perCycle=0u,prefix=0u;
    if(elapsed==0u)return 0u;
    // Enumerate one period, beginning immediately after the cold frame. A partial tile can
    // select the same edge pixel twice or four times; every actual opportunity consumes a point.
    for(uint step=1u;step<=4u;step++)
    {
        bool selected=all(equal(pixel,InterleavedSampleLocation2x2(rank,pixel/2,epoch+step,size)));
        if(selected){perCycle++;if(step<=elapsed%4u)prefix++;}
    }
    return elapsed/4u*perCycle+prefix;
}
#endif
