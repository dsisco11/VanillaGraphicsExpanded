#version 330 core

// Separable 1D Gaussian blur pass (horizontal or vertical).
// Output: R32F

out vec4 outColor;

uniform sampler2D u_src;
uniform ivec2 u_size;
uniform ivec2 u_dir;     // (1,0) for horizontal, (0,1) for vertical
uniform int u_radius;
uniform float u_weights[65];

ivec2 Wrap(ivec2 p, ivec2 size)
{
    int x = p.x % size.x; if (x < 0) x += size.x;
    int y = p.y % size.y; if (y < 0) y += size.y;
    return ivec2(x, y);
}

float LoadR(ivec2 p)
{
    return texelFetch(u_src, Wrap(p, u_size), 0).r;
}

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_size - ivec2(1));

    float sum = u_weights[0] * LoadR(p);

    int r = clamp(u_radius, 0, 64);
    for (int i = 1; i <= r; i++)
    {
        ivec2 o = u_dir * i;
        float w = u_weights[i];
        sum += w * (LoadR(p + o) + LoadR(p - o));
    }

    outColor = vec4(sum, 0.0, 0.0, 1.0);
}
