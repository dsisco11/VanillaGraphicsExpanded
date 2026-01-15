#version 330 core

// Restriction: coarseB = restrict(fineResidual)
// Output: R32F

out vec4 outColor;

uniform sampler2D u_fine;
uniform ivec2 u_fineSize;
uniform ivec2 u_coarseSize;

ivec2 Wrap(ivec2 p, ivec2 size)
{
    int x = p.x % size.x; if (x < 0) x += size.x;
    int y = p.y % size.y; if (y < 0) y += size.y;
    return ivec2(x, y);
}

float LoadFine(ivec2 p)
{
    return texelFetch(u_fine, Wrap(p, u_fineSize), 0).r;
}

void main()
{
    ivec2 c = ivec2(gl_FragCoord.xy);
    c = clamp(c, ivec2(0), u_coarseSize - ivec2(1));

    ivec2 f0 = c * 2;

    float r00 = LoadFine(f0 + ivec2(0, 0));
    float r10 = LoadFine(f0 + ivec2(1, 0));
    float r01 = LoadFine(f0 + ivec2(0, 1));
    float r11 = LoadFine(f0 + ivec2(1, 1));

    float coarse = 0.25 * (r00 + r10 + r01 + r11);
    outColor = vec4(coarse, 0.0, 0.0, 1.0);
}
