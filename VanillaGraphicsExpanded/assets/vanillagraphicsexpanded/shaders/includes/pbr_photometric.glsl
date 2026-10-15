#ifndef VGE_PHOTOMETRIC_GLSL
#define VGE_PHOTOMETRIC_GLSL
@import "./vge_frame_ubo.glsl"
const float VGE_PHOTOMETRIC_PI = 3.14159265358979323846;
@import "./pbr_persistent_lighting.glsl"
/** Converts the propagated block field to lux with a broad calibrated fade, preserving chromaticity. */
vec3 VgeBlockIlluminance(vec3 legacyBlockRgb, bool worldAccessor)
{
    vec2 calibration = worldAccessor ? vgeFrame.blockDecoderCalibration.xy : vgeFrame.lightCalibration.xy;
    vec3 block = max(legacyBlockRgb, vec3(0));
    float peak = max(block.r, max(block.g, block.b));
    // Begin fading within the lit field instead of retaining a bright plateau until
    // its last cells. The fourth-power dark end joins a near-linear strong response,
    // avoiding the excessive bright-source gain of an unbounded power curve.
    // The CPU fits this same response at the reference torch; propagation is unchanged.
    float amplitude = max(peak - calibration.y, 0.0);
    float amplitudeCubed = amplitude * amplitude * amplitude;
    float fade = amplitudeCubed / (amplitudeCubed + 0.064);
    return peak > 0.0 ? (block / peak) * (amplitude * fade * calibration.x) : vec3(0);
}
/** Retains terrain publication's explicit default decoder for existing block-field callers. */
vec3 VgeBlockIlluminance(vec3 legacyBlockRgb) { return VgeBlockIlluminance(legacyBlockRgb, false); }
/** Converts diffuse incident illuminance to the integrated E/pi input before receiver reflectance. */
vec3 VgeDiffuseIncident(vec3 illuminanceLux) { return illuminanceLux / VGE_PHOTOMETRIC_PI; }
/** Removes the active decoder's sky floor without changing dimensionless propagation or interpolation. */
float VgeSkyAvailability(float encoded, bool worldAccessor)
{
    vec2 endpoints = worldAccessor ? vgeFrame.skyCalibration.zw : vgeFrame.skyCalibration.xy;
    return clamp((encoded - endpoints.x) / (endpoints.y - endpoints.x), 0.0, 1.0);
}
/** Decodes the cache's nearest-byte representation of the world accessor without admitting its level-zero floor. */
float VgeStoredSkyAvailability(float encoded)
{
    vec2 endpoints = round(vgeFrame.skyCalibration.zw * 255.0) / 255.0;
    return clamp((encoded - endpoints.x) / (endpoints.y - endpoints.x), 0.0, 1.0);
}
/** Evaluates incident lux from an engine payload or explicitly authored candela and influence radius. */
vec3 VgePointIlluminance(vec4 payload, float distanceSquared)
{
    vec3 intensity = max(payload.xyz, vec3(0));
    float radius = payload.w > 0.0 ? payload.w : min(25.0, length(intensity));
    if (payload.w <= 0.0) intensity *= vgeFrame.lightCalibration.w;
    float distanceMetres = sqrt(max(distanceSquared, 0.0));
    if (radius <= 0.0 || distanceMetres >= radius) return vec3(0);
    float window = 1.0 - smoothstep(0.8 * radius, radius, distanceMetres);
    return intensity * (min(1.0 / max(distanceSquared, 0.0001), 1.0) * window);
}
/** Converts compatible dimensionless emission to outgoing RGB luminance independently of glow eligibility. */
vec3 VgeEmissionLuminance(vec3 baseColor, float coefficient)
{
    return baseColor * (max(coefficient, 0.0) * vgeFrame.lightCalibration.z);
}
#endif
