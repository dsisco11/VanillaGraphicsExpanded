#if !VGE_TESS_SHADOW
// Visible subdivision uses the shared camera even when a caller supplies effect inputs.
@import "../vge_frame_ubo.glsl"
#endif
uniform sampler2D vge_displacementTex;
uniform sampler2D vge_normalDepthTex;
#ifndef VGE_TESSELLATION_INPUTS
#if VGE_TESS_SHADOW
uniform mat4 mvpMatrix;
#else
uniform mat4 modelViewMatrix;
#endif
uniform float vge_tessellationFocalPixels;
uniform int vge_displacementEnabled;
// x = target pixels per segment, y = maximum subdivision.
uniform vec2 vge_tessellationPixels;
// x/y = displacement fade start/end in metres; zero/invalid values disable displacement.
uniform vec2 vge_tessellationDistance;
#endif

bool VgeFinite(vec3 v) { return !any(isnan(v)) && !any(isinf(v)); }
bool VgeFinite2(vec2 v) { return !any(isnan(v)) && !any(isinf(v)); }
bool VgeRectValid(vec2 lo, vec2 size) {
    return VgeFinite2(lo) && VgeFinite2(size) && all(greaterThan(size, vec2(0)))
        && all(greaterThanEqual(lo, vec2(0))) && all(lessThanEqual(lo + size, vec2(1)));
}
float VgeDistanceFade(vec3 position) {
    if (!VgeFinite2(vge_tessellationDistance) || vge_tessellationDistance.x < 0.0
        || vge_tessellationDistance.y <= vge_tessellationDistance.x) return 0.0;
    return 1.0 - smoothstep(vge_tessellationDistance.x, vge_tessellationDistance.y, length(position));
}
float VgeHeight(vec2 uvValue, vec2 lo, vec2 size, float amplitude, vec3 position) {
    if (isnan(amplitude) || isinf(amplitude) || amplitude <= 0.0 || !VgeRectValid(lo, size)
        || !VgeFinite2(uvValue) || !VgeFinite(position)) return 0.0;
    vec2 texel = 1.0 / vec2(textureSize(vge_normalDepthTex, 0));
    vec2 lower = lo + texel * 0.5;
    vec2 upper = lo + size - texel * 0.5;
    if (any(lessThan(upper, lower))) return 0.0;
    // LOD zero is deliberate: the current height atlas has only one initialized mip.
    float height = textureLod(vge_normalDepthTex, clamp(uvValue, lower, upper), 0.0).a;
    if (isnan(height) || isinf(height)) return 0.0;
    // Pin tile boundaries and corners to the original surface with a zero-slope ramp.
    // Both triangles evaluate the same function; the internal diagonal is not pinned.
    vec2 edge = min(uvValue - lo, lo + size - uvValue);
    vec2 ramp = smoothstep(texel, texel + max(2.0 * texel, size * 0.05), edge);
    return min(amplitude, 0.05) * clamp(2.0 * height - 1.0, -1.0, 1.0)
        * ramp.x * ramp.y * VgeDistanceFade(position);
}
float VgeEdgeLevel(vec3 a, vec3 b) {
    if (any(isnan(vge_tessellationPixels)) || any(isinf(vge_tessellationPixels))) return 1.0;
    float limit = floor(clamp(vge_tessellationPixels.y, 1.0, 8.0));
    // Fractional-odd spacing grows edge segments continuously; round the cap down to
    // an odd segment count so hardware rounding can never exceed the requested bound.
    limit = 2.0 * floor((limit - 1.0) * 0.5) + 1.0;
    if (!VgeFinite(a) || !VgeFinite(b) || !(vge_tessellationPixels.x > 0.0)) return 1.0;
    if (max(length(a), length(b)) >= vge_tessellationDistance.y) return 1.0;
    // Production uses one camera-derived angular metric for visible and shadow passes.
    // It depends only on shared endpoints, so light projection cannot change subdivision.
    if (vge_tessellationFocalPixels > 0.0) {
        float distance = max(0.05, min(length(a), length(b)));
        float pixels = length(b - a) * vge_tessellationFocalPixels / distance;
        float level = clamp(pixels / vge_tessellationPixels.x, 1.0, limit);
        return mix(1.0, level, min(VgeDistanceFade(a), VgeDistanceFade(b)));
    }
#if VGE_TESS_SHADOW
    // Shadows run before world-camera publication. If the frozen angular metric is
    // unavailable, preserve the subdivision cap without borrowing a viewport or light projection.
    return limit;
#else
    vec4 ca = vgeFrame.projectionMatrix * modelViewMatrix * vec4(a, 1);
    vec4 cb = vgeFrame.projectionMatrix * modelViewMatrix * vec4(b, 1);
    // Never divide by a near-plane crossing; bound it conservatively instead.
    if (min(ca.w, cb.w) <= 0.0001) return limit;
    float pixels = length((ca.xy / ca.w - cb.xy / cb.w) * vgeFrame.screenSize * 0.5);
    if (isnan(pixels) || isinf(pixels)) return limit;
    float level = clamp(pixels / vge_tessellationPixels.x, 1.0, limit);
    return mix(1.0, level, min(VgeDistanceFade(a), VgeDistanceFade(b)));
#endif
}
