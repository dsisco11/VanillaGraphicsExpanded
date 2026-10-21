#ifndef VGE_STORED_VOLUME_SAMPLING
#define VGE_STORED_VOLUME_SAMPLING
@import "../includes/interleaved_sampling.glsl"
@import "../includes/pmj_sampling.glsl"
/** Reproduces the current fresh ray without per-pixel counters or persistent position textures. */
vec2 StoredVolumeSampleUv(sampler2D sequence,float rank,ivec2 pixel,ivec2 size,uint frame,uint epoch)
{
    uint observation=InterleavedObservationIndex2x2(rank,pixel,size,frame,epoch);
    // Adding a subpixel fraction to a large integer can round onto the outer boundary.
    // Keep finite half-open screen coverage without changing the selected pixel or ray count.
    return min((vec2(pixel)+.5+PmjPixelOffset(sequence,pixel,observation))/vec2(size),vec2(uintBitsToFloat(0x3f7fffffu)));
}
#endif
