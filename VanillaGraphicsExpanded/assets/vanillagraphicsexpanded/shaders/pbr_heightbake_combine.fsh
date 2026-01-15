#version 330 core

// Multi-scale band-pass combine.
// Inputs are blurred versions of D0: G1..G4.
// Output: R32F (detail D)

out vec4 outColor;

uniform sampler2D u_g1;
uniform sampler2D u_g2;
uniform sampler2D u_g3;
uniform sampler2D u_g4;
uniform vec3 u_w;   // (w1,w2,w3)
uniform ivec2 u_size;

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_size - ivec2(1));

    float g1 = texelFetch(u_g1, p, 0).r;
    float g2 = texelFetch(u_g2, p, 0).r;
    float g3 = texelFetch(u_g3, p, 0).r;
    float g4 = texelFetch(u_g4, p, 0).r;

    float b1 = g1 - g2;
    float b2 = g2 - g3;
    float b3 = g3 - g4;

    float d = u_w.x * b1 + u_w.y * b2 + u_w.z * b3;
    outColor = vec4(d, 0.0, 0.0, 1.0);
}
