#ifndef VGE_WATER_TRANSPORT_GLSL
#define VGE_WATER_TRANSPORT_GLSL
@import "./medium.glsl"

/** Receiver provenance is independent of confidence; unavailable coverage is never a ray hit. */
const int VGE_WATER_RECEIVER_NONE = 0;
const int VGE_WATER_RECEIVER_RAY = 1;

/** Unattenuated linear receiver radiance and view-space optical geometry selected by a sampler. */
struct VgeWaterReceiver
{
    bool valid;
    int method;
    vec3 radiance;
    vec3 positionVS;
    vec3 refractedDirectionVS;
    float submergedLength;
    float confidence;
};

/** Returns the outgoing photon direction inside water, never the air segment of an underwater exit. */
vec3 VgeWaterOutgoingDirection(vec3 surfaceVS, vec3 refractedDirectionVS, bool underwater, mat3 toWorld)
{
    // Tracing follows camera-to-receiver rays. Scattered photons travel in reverse.
    // For an underwater camera the camera-to-interface segment, not the refracted ray, is water.
    return normalize(toWorld * (underwater ? -surfaceVS : -refractedDirectionVS));
}

/** Evaluates RGB medium transmission and source radiance without exposure or display conversion. */
vec3 VgeWaterTransport(VgeWaterMedium medium, float submergedLength, vec3 background, vec3 source)
{
    return background * VgeWaterTransmittance(medium, submergedLength)
        + VgeWaterInScattering(medium, submergedLength, source);
}

/** Blends premultiplied linear contributions and coverage before any legacy display adaptation. */
vec4 VgeWaterCompose(vec3 fallbackRadiance, float fallbackAlpha, vec3 receiverRadiance, float confidence)
{
    float weight = clamp(confidence, 0.0, 1.0);
    float alpha = mix(fallbackAlpha, 1.0, weight);
    // The remaining OIT background weight is (1-weight)*(1-fallbackAlpha).
    vec3 source = mix(fallbackRadiance * fallbackAlpha, receiverRadiance, weight);
    return vec4(source / max(alpha, .001), alpha);
}
#endif
