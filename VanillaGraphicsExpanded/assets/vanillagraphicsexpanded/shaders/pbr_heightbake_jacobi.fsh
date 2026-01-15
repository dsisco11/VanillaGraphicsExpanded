#version 330 core

// Jacobi smoother for periodic Poisson solve: (sumN - 4h) = b
// Update: hNew = (sumN - b) / 4
// Output: R32F

out vec4 outColor;

uniform sampler2D u_h;
uniform sampler2D u_b;
uniform ivec2 u_size;

ivec2 Wrap(ivec2 p, ivec2 size)
{
    int x = p.x % size.x; if (x < 0) x += size.x;
    int y = p.y % size.y; if (y < 0) y += size.y;
    return ivec2(x, y);
}

float LoadR(sampler2D t, ivec2 p)
{
    return texelFetch(t, Wrap(p, u_size), 0).r;
}

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_size - ivec2(1));

    float hL = LoadR(u_h, p + ivec2(-1, 0));
    float hR = LoadR(u_h, p + ivec2( 1, 0));
    float hD = LoadR(u_h, p + ivec2( 0, -1));
    float hU = LoadR(u_h, p + ivec2( 0, 1));

    float b = LoadR(u_b, p);

    float hNew = (hL + hR + hD + hU - b) * 0.25;
    outColor = vec4(hNew, 0.0, 0.0, 1.0);
}
