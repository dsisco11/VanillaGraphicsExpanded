#version 330 core

// Prolongation + correction: dst = fineH + prolongate(coarseE)
// Output: R32F

out vec4 outColor;

uniform sampler2D u_fineH;
uniform sampler2D u_coarseE;
uniform ivec2 u_fineSize;
uniform ivec2 u_coarseSize;

ivec2 Wrap(ivec2 p, ivec2 size)
{
    int x = p.x % size.x; if (x < 0) x += size.x;
    int y = p.y % size.y; if (y < 0) y += size.y;
    return ivec2(x, y);
}

float LoadFineH(ivec2 p)
{
    return texelFetch(u_fineH, Wrap(p, u_fineSize), 0).r;
}

float LoadCoarseE(ivec2 p)
{
    return texelFetch(u_coarseE, Wrap(p, u_coarseSize), 0).r;
}

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_fineSize - ivec2(1));

    ivec2 c0 = p / 2;
    ivec2 c1x = c0 + ivec2(1, 0);
    ivec2 c1y = c0 + ivec2(0, 1);
    ivec2 c11 = c0 + ivec2(1, 1);

    float fx = float(p.x & 1) * 0.5;
    float fy = float(p.y & 1) * 0.5;

    float e00 = LoadCoarseE(c0);
    float e10 = LoadCoarseE(c1x);
    float e01 = LoadCoarseE(c1y);
    float e11 = LoadCoarseE(c11);

    float e0 = mix(e00, e10, fx);
    float e1 = mix(e01, e11, fx);
    float e = mix(e0, e1, fy);

    float h = LoadFineH(p);
    outColor = vec4(h + e, 0.0, 0.0, 1.0);
}
