#version 330 core

// Divergence of desired gradient field.
// Output: R32F

out vec4 outColor;

uniform sampler2D u_g; // RG32F
uniform ivec2 u_size;

ivec2 Wrap(ivec2 p, ivec2 size)
{
    int x = p.x % size.x; if (x < 0) x += size.x;
    int y = p.y % size.y; if (y < 0) y += size.y;
    return ivec2(x, y);
}

vec2 LoadG(ivec2 p)
{
    return texelFetch(u_g, Wrap(p, u_size), 0).rg;
}

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_size - ivec2(1));

    float dx = 0.5 * (LoadG(p + ivec2(1, 0)).x - LoadG(p + ivec2(-1, 0)).x);
    float dy = 0.5 * (LoadG(p + ivec2(0, 1)).y - LoadG(p + ivec2(0, -1)).y);

    float div = dx + dy;
    outColor = vec4(div, 0.0, 0.0, 1.0);
}
