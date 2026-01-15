#version 330 core

// Pack height + normal into the atlas sidecar.
// Output target: RGBA16F atlas texture.
// RGB: normal (0..1). A: signed height.

out vec4 outColor;

uniform sampler2D u_height; // R32F tile-sized height

uniform ivec2 u_solverSize;      // size of height texture (tile)
uniform ivec2 u_tileSize;        // viewport/tile size within atlas
uniform ivec2 u_viewportOrigin;  // atlas viewport origin in pixels

uniform float u_normalStrength;

ivec2 Wrap(ivec2 p, ivec2 size)
{
    int x = p.x % size.x; if (x < 0) x += size.x;
    int y = p.y % size.y; if (y < 0) y += size.y;
    return ivec2(x, y);
}

float LoadH(ivec2 p)
{
    return texelFetch(u_height, Wrap(p, u_solverSize), 0).r;
}

void main()
{
    // gl_FragCoord is in window (atlas) coordinates; make it local to the tile.
    ivec2 p = ivec2(gl_FragCoord.xy) - u_viewportOrigin;
    p = clamp(p, ivec2(0), u_tileSize - ivec2(1));

    float hC = LoadH(p);
    float dhdx = 0.5 * (LoadH(p + ivec2(1, 0)) - LoadH(p + ivec2(-1, 0)));
    float dhdy = 0.5 * (LoadH(p + ivec2(0, 1)) - LoadH(p + ivec2(0, -1)));

    vec3 n = normalize(vec3(-dhdx * u_normalStrength, -dhdy * u_normalStrength, 1.0));
    vec3 n01 = n * 0.5 + 0.5;

    outColor = vec4(n01, hC);
}
