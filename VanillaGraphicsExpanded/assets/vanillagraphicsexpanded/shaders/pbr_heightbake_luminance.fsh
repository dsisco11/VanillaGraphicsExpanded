#version 330 core

// Luminance extraction from an atlas sub-rect.
// Output: R32F (written as .r)

out vec4 outColor;

uniform sampler2D u_atlas;
uniform ivec4 u_atlasRectPx; // (x, y, w, h) in atlas pixels
uniform ivec2 u_outSize;      // output size in pixels (w, h)

ivec2 Wrap(ivec2 p, ivec2 size)
{
    int x = p.x % size.x; if (x < 0) x += size.x;
    int y = p.y % size.y; if (y < 0) y += size.y;
    return ivec2(x, y);
}

float SrgbToLinear1(float c)
{
    return (c <= 0.04045) ? (c / 12.92) : pow((c + 0.055) / 1.055, 2.4);
}

vec3 SrgbToLinear(vec3 c)
{
    return vec3(SrgbToLinear1(c.r), SrgbToLinear1(c.g), SrgbToLinear1(c.b));
}

void main()
{
    // Pixel coordinate in output space.
    ivec2 p = ivec2(gl_FragCoord.xy);
    p = clamp(p, ivec2(0), u_outSize - ivec2(1));

    ivec2 tileSize = u_atlasRectPx.zw;
    ivec2 tp = Wrap(p, tileSize);

    ivec2 atlasCoord = u_atlasRectPx.xy + tp;
    vec4 rgba = texelFetch(u_atlas, atlasCoord, 0);
    vec3 lin = SrgbToLinear(rgba.rgb);

    // Alpha handling:
    // Many block-atlas tiles have cutouts/overlays with fully transparent texels whose RGB is not meaningful.
    // Multiplying luminance by alpha can also wipe out useful edge detail on anti-aliased cutouts.
    // Treat fully transparent texels as a neutral baseline (0.5) so they don't dominate band-pass steps.
    float lum = dot(lin, vec3(0.2126, 0.7152, 0.0722));
    float a = rgba.a;
    float w = smoothstep(0.05, 0.20, a);
    float L = mix(0.5, lum, w);
    outColor = vec4(L, 0.0, 0.0, 1.0);
}
