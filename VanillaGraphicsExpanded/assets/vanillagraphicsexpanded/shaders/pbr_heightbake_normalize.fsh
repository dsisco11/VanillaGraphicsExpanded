#version 330 core

// Normalize + shape height field.
// Output: R32F

out vec4 outColor;

uniform sampler2D u_h;
uniform ivec2 u_size;
uniform float u_mean;
uniform float u_invNeg;
uniform float u_invPos;
uniform float u_heightStrength;
uniform float u_gamma;

float Shape(float x, float gamma)
{
    if (gamma == 1.0) return x;
    float ax = abs(x);
    return sign(x) * pow(ax, gamma);
}

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_size - ivec2(1));

    float h = texelFetch(u_h, p, 0).r;
    // Per-tile asymmetric normalization.
    // Many tiles have skewed height distributions (e.g. strong negatives but weak positives);
    // using one symmetric scale makes them appear mostly black/white after packing.
    float d = h - u_mean;
    float inv = (d < 0.0) ? u_invNeg : u_invPos;
    float v = d * inv * u_heightStrength;
    v = Shape(v, u_gamma);

    outColor = vec4(v, 0.0, 0.0, 1.0);
}
