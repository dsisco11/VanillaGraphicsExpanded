/** Wraps an atlas-local animated coordinate without sampling an adjacent tile. */
vec2 VgeAnimatedAtlasUv(vec2 baseUv, vec2 localUv, vec2 motion, vec2 tileSize, vec2 atlasSize)
{
    vec2 padding = 1.0 / atlasSize;
    return baseUv + clamp(mod(localUv + motion, tileSize), padding, tileSize - padding);
}
