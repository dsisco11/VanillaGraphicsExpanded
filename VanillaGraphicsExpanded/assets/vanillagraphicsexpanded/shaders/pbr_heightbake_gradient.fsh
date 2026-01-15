#version 330 core

// Desired gradient field g from band-passed detail D.
// Output: RG32F (gx, gy)

out vec4 outColor;

uniform sampler2D u_d;
uniform ivec2 u_size;
uniform float u_gain;
uniform float u_maxSlope;
uniform vec2 u_edgeT; // (t0, t1)

ivec2 Wrap(ivec2 p, ivec2 size)
{
    int x = p.x % size.x; if (x < 0) x += size.x;
    int y = p.y % size.y; if (y < 0) y += size.y;
    return ivec2(x, y);
}

float LoadR(ivec2 p)
{
    return texelFetch(u_d, Wrap(p, u_size), 0).r;
}

void main()
{
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_size - ivec2(1));

    float dx = 0.5 * (LoadR(p + ivec2(1, 0)) - LoadR(p + ivec2(-1, 0)));
    float dy = 0.5 * (LoadR(p + ivec2(0, 1)) - LoadR(p + ivec2(0, -1)));

    vec2 g = vec2(dx, dy);
    float mag = length(g);

    float edge = smoothstep(u_edgeT.x, u_edgeT.y, mag);
    g *= (u_gain * edge);

    float gmag = length(g);
    if (gmag > u_maxSlope && gmag > 1e-8)
    {
        g *= (u_maxSlope / gmag);
    }

    outColor = vec4(g, 0.0, 1.0);
}
