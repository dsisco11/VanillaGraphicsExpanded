#ifndef VGE_PMJ_SAMPLING
#define VGE_PMJ_SAMPLING
/** Produces stable screen-pixel scramble bits without permuting or skipping sequence indices. */
uint PmjPixelScramble(ivec2 pixel)
{
    uint value=uint(pixel.x)*0x9e3779b9u^uint(pixel.y)*0x85ebca6bu^0xa5b35705u;
    value^=value>>16;value*=0x7feb352du;value^=value>>15;value*=0x846ca68bu;
    return value^(value>>16);
}
/** Digitally shifts quantized points and returns offsets strictly inside a pixel footprint. */
vec2 PmjPixelOffset(sampler2D sequence,ivec2 pixel,uint observation)
{
    uint count=uint(textureSize(sequence,0).x);
    uvec2 bits=uvec2(round(texelFetch(sequence,ivec2(int(observation%count),0),0).rg*65535.0));
    uint scramble=PmjPixelScramble(pixel);
    // XOR is a bijection of the stored 16-bit digits. It preserves the actual quantized
    // sequence's dyadic bin counts, without claiming properties of a different PMJ algorithm.
    bits^=uvec2(scramble,scramble>>16)&uvec2(65535u);
    return (vec2(bits)+.5)/65536.0-.5;
}
#endif
