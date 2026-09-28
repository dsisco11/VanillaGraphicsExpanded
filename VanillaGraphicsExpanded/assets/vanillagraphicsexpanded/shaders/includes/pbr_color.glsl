#ifndef VGE_PBR_COLOR_GLSL
#define VGE_PBR_COLOR_GLSL

/** Matches the CPU representative-albedo sRGB transfer function. Alpha/data stay linear. */
vec3 VgeSrgbToLinear(vec3 color)
{
    color = max(color, vec3(0.0));
    return mix(pow((color + 0.055) / 1.055, vec3(2.4)), color / 12.92,
        lessThanEqual(color, vec3(0.04045)));
}

/** Encodes for the RGBA8 primary without automatic framebuffer sRGB conversion. */
vec3 VgeLinearToSrgb(vec3 color)
{
    color = max(color, vec3(0.0));
    return mix(1.055 * pow(color, vec3(1.0 / 2.4)) - 0.055, color * 12.92,
        lessThanEqual(color, vec3(0.0031308)));
}

/** Shared scene-to-display exposure for sky, solar disk and both surface lighting modes. */
const float VGE_DISPLAY_EXPOSURE = 1.0;

/** RGB-ratio-preserving shoulder: the brightest channel approaches one without clipping its neighbors. */
vec3 VgeToneMapRadiance(vec3 radiance)
{
    vec3 exposed = max(radiance, vec3(0.0)) * VGE_DISPLAY_EXPOSURE;
    float peak = max(exposed.r, max(exposed.g, exposed.b));
    return exposed / (1.0 + peak);
}

/** Apply exposure, highlight compression and sRGB exactly once at each current SDR draw boundary. */
vec3 VgeResolveDisplay(vec3 radiance)
{
    return VgeLinearToSrgb(VgeToneMapRadiance(radiance));
}

/** Stable, zero-mean ordered dither in encoded SDR units, bounded below half an RGBA8 code step. */
vec3 VgeDitherDisplay(vec3 encoded, vec2 pixel)
{
    uvec2 position = uvec2(floor(pixel)) & uvec2(7u);
    uint rank = 0u;
    // Interleave the Bayer quadrant ordering: each 8x8 tile contains every rank exactly once.
    for (uint bit = 0u; bit < 3u; ++bit)
    {
        uint x = (position.x >> bit) & 1u;
        uint y = (position.y >> bit) & 1u;
        rank = (rank << 2u) | ((x ^ y) << 1u) | y;
    }
    float offset = ((float(rank) + 0.5) / 64.0 - 0.5) / 255.0;
    // Share noise across RGB to keep neutral colors neutral. Alpha and lighting data never enter here.
    return clamp(encoded + vec3(offset), vec3(0.0), vec3(1.0));
}
#endif
