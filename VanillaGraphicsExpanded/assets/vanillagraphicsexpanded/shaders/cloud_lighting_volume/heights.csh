#version 430 core
layout(local_size_x=64) in;
layout(std140) uniform LightingHeightInputs { ivec4 heightExtent; };
uniform sampler2D heightMap;
layout(std430) buffer HeightScratch { uint heightScratch[]; };
shared float lower[64],upper[64];
/** Reduces finite occupied native support into two reserved scratch words, retaining an explicit empty interval. */
void main()
{
    uint lane=gl_LocalInvocationIndex;
    float lo=3.402823466e38,hi=-3.402823466e38;
    int width=heightExtent.x;
    for(int i=int(lane);i<width*width;i+=64)
    {
        vec4 data=texelFetch(heightMap,ivec2(i%width,i/width),0);vec2 h=50.0*data.ba;
        if(any(isnan(data))||any(isinf(data))||any(isinf(h))||data.r<=0.0||data.a<=data.b) continue;
        lo=min(lo,h.x);hi=max(hi,h.y);
    }
    lower[lane]=lo;upper[lane]=hi;barrier();
    for(uint stride=32u;stride>0u;stride>>=1u)
    {
        if(lane<stride) {lower[lane]=min(lower[lane],lower[lane+stride]);upper[lane]=max(upper[lane],upper[lane+stride]);}
        barrier();
    }
    if(lane==0u) {heightScratch[1536]=floatBitsToUint(lower[0]);heightScratch[1537]=floatBitsToUint(upper[0]);}
}
