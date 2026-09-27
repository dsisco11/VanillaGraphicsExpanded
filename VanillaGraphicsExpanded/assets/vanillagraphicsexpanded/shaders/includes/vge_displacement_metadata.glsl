#ifndef VGE_DISPLACEMENT_METADATA
#define VGE_DISPLACEMENT_METADATA
// Zero indices and invalid records disable only the current material.
bool VgeResolveDisplacement(sampler2D indices, sampler2D records, vec2 uv, out vec4 rect, out float amplitude) {
    rect = vec4(0);
    amplitude = 0.0;
    if (any(isnan(uv)) || any(isinf(uv)) || any(lessThan(uv, vec2(0))) || any(greaterThanEqual(uv, vec2(1)))) return false;
    float encoded = textureLod(indices, uv, 0.0).r;
    ivec2 size = textureSize(records, 0);
    int count = (size.x * size.y) >> 1;
    if (isnan(encoded) || isinf(encoded) || encoded < 1.0 || encoded > float(count) || encoded != floor(encoded)) return false;
    int entry = (int(encoded) - 1) << 1;
    rect = texelFetch(records, ivec2(entry % size.x, entry / size.x), 0);
    entry++;
    amplitude = texelFetch(records, ivec2(entry % size.x, entry / size.x), 0).r;
    return !any(isnan(rect)) && !any(isinf(rect)) && all(greaterThanEqual(rect.xy, vec2(0)))
        && all(greaterThan(rect.zw, vec2(0))) && all(lessThanEqual(rect.xy + rect.zw, vec2(1)))
        && !isnan(amplitude) && !isinf(amplitude) && amplitude > 0.0 && amplitude <= 0.05;
}
#endif
