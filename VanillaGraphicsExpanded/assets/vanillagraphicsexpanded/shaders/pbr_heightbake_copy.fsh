#version 330 core

// Copy pass: dst = src
// Output: R32F

out vec4 outColor;

uniform sampler2D u_src;
uniform ivec2 u_size;

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_size - ivec2(1));

    float v = texelFetch(u_src, p, 0).r;
    outColor = vec4(v, 0.0, 0.0, 1.0);
}
