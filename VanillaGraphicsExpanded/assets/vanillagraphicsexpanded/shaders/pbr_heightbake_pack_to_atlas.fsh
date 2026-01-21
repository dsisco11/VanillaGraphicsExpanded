#version 330 core

// Pack height + normal into the atlas sidecar.
// Output target: RGBA16F atlas texture.
// RGB: normal (0..1). A: encoded signed height (0..1).

out vec4 outColor;

uniform sampler2D u_height; // R32F tile-sized height

// Base albedo atlas page (for alpha masking).
uniform sampler2D u_albedoAtlas;
uniform float u_alphaCutoff;

uniform ivec2 u_solverSize;      // size of height texture (tile)
uniform ivec2 u_tileSize;        // viewport/tile size within atlas
uniform ivec2 u_viewportOrigin;  // atlas viewport origin in pixels

uniform float u_normalStrength;
uniform float u_normalScale;
uniform float u_depthScale;

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

    // Base albedo alpha at this atlas pixel.
    // When albedo alpha is 0, treat it as "no surface": output A=0 and keep the normal flat.
    float aAlbedo = texelFetch(u_albedoAtlas, u_viewportOrigin + p, 0).a;
    if (aAlbedo <= u_alphaCutoff)
    {
        // Neutral height prevents parallax/normal artifacts if UVs ever sample into padding.
        outColor = vec4(0.5, 0.5, 1.0, 0.5);
        return;
    }

    float hC = LoadH(p);
    float dhdx = 0.5 * (LoadH(p + ivec2(1, 0)) - LoadH(p + ivec2(-1, 0)));
    float dhdy = 0.5 * (LoadH(p + ivec2(0, 1)) - LoadH(p + ivec2(0, -1)));

    float ns = u_normalStrength * u_normalScale;
    vec3 n = normalize(vec3(-dhdx * ns, -dhdy * ns, 1.0));
    vec3 n01 = n * 0.5 + 0.5;

    // Store signed height in a RenderDoc-friendly encoding.
    // Clamp first to avoid blowing out the visualization due to large solver magnitudes.
    float hSigned = clamp(hC * u_depthScale, -1.0, 1.0);
    float h01 = hSigned * 0.5 + 0.5;

    outColor = vec4(n01, h01);
}
