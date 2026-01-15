#version 330 core

// Subtract pass: dst = a - b
// Output: R32F

out vec4 outColor;

uniform sampler2D u_a;
uniform sampler2D u_b;
uniform ivec2 u_size;

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_size - ivec2(1));

    float a = texelFetch(u_a, p, 0).r;
    float b = texelFetch(u_b, p, 0).r;
    outColor = vec4(a - b, 0.0, 0.0, 1.0);
}
