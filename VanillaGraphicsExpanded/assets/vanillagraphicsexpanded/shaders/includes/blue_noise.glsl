#ifndef VGE_BLUE_NOISE
#define VGE_BLUE_NOISE
/** Reads a tiled rank from vertically stacked masks; maskCount is positive and divides atlas height, pixel is nonnegative. */
float BlueNoiseRank(sampler2D noise,ivec2 pixel,uint frame,int maskCount)
{
    ivec2 size=textureSize(noise,0);int maskHeight=size.y/maskCount;
    return texelFetch(noise,ivec2(pixel.x%size.x,pixel.y%maskHeight+int(frame%uint(maskCount))*maskHeight),0).r;
}
#endif
