#version 330 core
layout(location = 0) out vec4 outColor;
layout(location = 1) out vec4 outDepth;
uniform sampler2D sourceColor;
uniform sampler2D sourceDepth;
@import "./includes/liquids/receiver_publication.glsl"

/** Selects the farthest supported opaque texel, preserving its exact radiance and reconstruction UV. */
void main()
{
    ivec2 size = textureSize(sourceDepth, 0);
    ivec2 origin = ivec2(gl_FragCoord.xy) * 2;
    outColor = vec4(0);
    outDepth = vec4(1, 0, 0, 0);
    float selected = 0.0;
    // A foreground silhouette cannot contaminate a represented background by averaging.
    // When only foreground survives here, interface eligibility still rejects it at lookup.
    for (int y = 0; y < 2; ++y)
    for (int x = 0; x < 2; ++x)
    {
        ivec2 pixel = origin + ivec2(x, y);
        if (any(greaterThanEqual(pixel, size))) continue;
        float depth = texelFetch(sourceDepth, pixel, 0).r;
        // Selected starts at zero and advances only after pair validation. Ordered
        // comparisons reject invalid depth and ties before reading unused color.
        if (!(depth > selected && depth < .999999)) continue;
        vec4 color = texelFetch(sourceColor, pixel, 0);
        if (!VgeWaterReceiverColorValid(color)) continue;
        selected = depth;
        outColor = color;
        outDepth = vec4(depth, (vec2(pixel) + .5) / vec2(size), 1.0);
    }
}
