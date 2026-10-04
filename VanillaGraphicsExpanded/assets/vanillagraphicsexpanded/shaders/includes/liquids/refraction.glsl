#ifndef VGE_WATER_REFRACTION_GLSL
#define VGE_WATER_REFRACTION_GLSL
@import "./transport.glsl"
// Diagnostic hooks compile away in ordinary rendering variants.
// Reasons: 0 accepted, 1 projection/bounds, 2 depth/sky/metadata, 3 homogeneous
// reconstruction, 4 interface plane, 5 reserved (former adjacent veto), 6 TIR, 7 residual,
// 8 nonfinite radiance, 9 distance exhaustion. SAMPLE counts every receiver
// depth evaluation, including the final post-refinement validation.
#ifndef VGE_REFRACTION_EVENT
#define VGE_REFRACTION_EVENT(reason)
#endif
#ifndef VGE_REFRACTION_SAMPLE
#define VGE_REFRACTION_SAMPLE(uv, depth)
#endif
@import "./receiver.glsl"

/** Projects a view-space ray point without clamping offscreen samples into the image. */
bool VgeRefractionProject(vec3 position, out vec2 sampleUv)
{
    vec4 clip = projectionMatrix * vec4(position, 1.0);
    if (clip.w <= .0001) { VGE_REFRACTION_EVENT(1); return false; }
    sampleUv = clip.xy / clip.w * .5 + .5;
    // Retain the existing view-pixel guard without doubling it at half size;
    // reduced storage additionally requires a supported texel-center footprint.
    vec2 margin = max(2.0 / frameSize, .5 / vec2(textureSize(vge_refractionDepth, 0)));
    bool supported = all(greaterThan(sampleUv, margin)) && all(lessThan(sampleUv, 1.0 - margin));
    if (!supported) { VGE_REFRACTION_EVENT(1); }
    return supported;
}

/** Evaluates one shared filtered receiver without an all-neighbours foreground veto. */
bool VgeRefractionReceiver(vec2 sampleUv, vec3 surface, vec3 normalVS,
    mat4 inverseProjection, out float receiverZ, out vec3 radiance)
{
    vec3 positionVS;
    bool supported = VgeRefractionFilter(sampleUv, surface, normalVS, inverseProjection, positionVS, radiance);
    receiverZ = -positionVS.z;
    VGE_REFRACTION_SAMPLE(sampleUv, receiverZ);
    return supported;
}
/** Traces only the visible opaque layer, with 32 samples and five crossing refinements over at most 32 metres. */
VgeWaterReceiver VgeWaterRefraction(vec3 surface, vec3 normalVS, bool underwater)
{
    vec3 background = vec3(0);
    float submergedLength = 0.0;
    vec3 receiver = surface;
    float confidence = 0.0;
    vec3 incident = normalize(surface);
    vec3 direction = refract(incident, normalVS, underwater ? 1.333 : 1.0 / 1.333);
    VgeWaterReceiver result = VgeWaterReceiver(false, VGE_WATER_RECEIVER_NONE, vec3(0), surface, direction, 0.0, 0.0);
    // A zero direction is total internal reflection; it is never a transmitted hit.
    if (dot(direction, direction) < .0001) { VGE_REFRACTION_EVENT(6); return result; }
    mat4 inverseProjection = inverse(projectionMatrix);
    float previousT = 0.0;
    for (int step = 1; step <= 32; ++step)
    {
        float fraction = float(step) / 32.0;
        float distance = 32.0 * fraction * fraction;
        vec3 point = surface + direction * distance;
        vec2 sampleUv;
        float receiverZ;
        if (!VgeRefractionProject(point, sampleUv)
            || !VgeRefractionReceiver(sampleUv, surface, normalVS, inverseProjection, receiverZ, background)) return result;
        if (-point.z >= receiverZ)
        {
            float low = previousT;
            float high = distance;
            for (int refine = 0; refine < 5; ++refine)
            {
                float middle = (low + high) * .5;
                point = surface + direction * middle;
                if (!VgeRefractionProject(point, sampleUv)
                    || !VgeRefractionReceiver(sampleUv, surface, normalVS, inverseProjection, receiverZ, background)) return result;
                if (-point.z >= receiverZ) high = middle;
                else low = middle;
            }
            receiver = surface + direction * high;
            if (!VgeRefractionProject(receiver, sampleUv)
                || !VgeRefractionReceiver(sampleUv, surface, normalVS, inverseProjection, receiverZ, background)) return result;
            // A depth discontinuity can mimic a crossing. Missing hidden geometry is not a hit.
            if (abs(-receiver.z - receiverZ) > .15) { VGE_REFRACTION_EVENT(7); return result; }
            if (any(isnan(background)) || any(isinf(background))) { VGE_REFRACTION_EVENT(8); return result; }
            submergedLength = underwater ? length(surface) : high;
            // Fade the complete refracted contribution before clipping or exhausting the trace.
            // Use both endpoints so lower-screen interfaces do not develop a binary fallback seam.
            vec2 edgePixels = min(sampleUv, 1.0 - sampleUv) * frameSize;
            vec2 surfacePixels = min(gl_FragCoord.xy, frameSize - gl_FragCoord.xy);
            float edge = min(min(edgePixels.x, edgePixels.y), min(surfacePixels.x, surfacePixels.y));
            float fadeWidth = min(48.0, max(2.0, min(frameSize.x, frameSize.y) * .08));
            confidence = smoothstep(2.0, 2.0 + fadeWidth, edge)
                * (1.0 - smoothstep(24.0, 32.0, high))
                * (1.0 - smoothstep(.04, .15, abs(-receiver.z - receiverZ)));
            return VgeWaterReceiver(true, VGE_WATER_RECEIVER_RAY, background, receiver, direction, submergedLength, confidence);
        }
        previousT = distance;
    }
    VGE_REFRACTION_EVENT(9);
    return result;
}
#endif
