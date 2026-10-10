#version 330 core

layout(location = 0) out float outDepth;

// Copies primary depth into HZB mip 0 (R32F)
uniform sampler2D primaryDepth;

void main(void)
{
    ivec2 coord = ivec2(gl_FragCoord.xy);
    float depth = texelFetch(primaryDepth, coord, 0).r;
    outDepth = depth;
}
