#version 330 core

layout(location = 0) out float outDepth;

// Downsamples HZB by taking MIN depth over a 2x2 block.
// Assumes depth convention: near ~0, far/sky ~1.

uniform sampler2D hzbDepth;
uniform int srcMip;

void main(void)
{
    ivec2 dstCoord = ivec2(gl_FragCoord.xy);

    ivec2 srcCoord0 = dstCoord * 2;

    float d00 = texelFetch(hzbDepth, srcCoord0 + ivec2(0, 0), srcMip).r;
    float d10 = texelFetch(hzbDepth, srcCoord0 + ivec2(1, 0), srcMip).r;
    float d01 = texelFetch(hzbDepth, srcCoord0 + ivec2(0, 1), srcMip).r;
    float d11 = texelFetch(hzbDepth, srcCoord0 + ivec2(1, 1), srcMip).r;

    outDepth = min(min(d00, d10), min(d01, d11));
}
