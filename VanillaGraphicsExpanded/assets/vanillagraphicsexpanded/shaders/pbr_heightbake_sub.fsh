#version 330 core

// Subtract pass: dst = a - b
// Output: R32F
// Mode:
// - u_relContrast = 0: out = a - b
// - u_relContrast = 1: out = (a - b) / (b + u_eps)

out vec4 outColor;

uniform sampler2D u_a;
uniform sampler2D u_b;
uniform ivec2 u_size;
uniform int u_relContrast;
uniform float u_eps;

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_size - ivec2(1));

    float a = texelFetch(u_a, p, 0).r;
    float b = texelFetch(u_b, p, 0).r;

    float v = a - b;
    if (u_relContrast != 0)
    {
        v = v / (b + u_eps);
    }

    outColor = vec4(v, 0.0, 0.0, 1.0);
}
