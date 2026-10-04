#ifndef VGE_WATER_REFRACTION_GLSL
#define VGE_WATER_REFRACTION_GLSL
@import "./transport.glsl"
// Diagnostic hooks compile away in ordinary rendering variants.
// Reasons: 0 accepted, 1 projection/bounds, 2 depth/sky/metadata, 3 homogeneous
// reconstruction, 4 interface plane, 5 adjacent receiver, 6 TIR, 7 residual,
// 8 nonfinite radiance, 9 distance exhaustion. SAMPLE counts every receiver
// depth evaluation, including the final post-refinement validation.
#ifndef VGE_REFRACTION_EVENT
#define VGE_REFRACTION_EVENT(reason)
#define VGE_REFRACTION_SAMPLE(uv, depth)
#endif
layout(binding = 9) uniform sampler2D vge_refractionColor;
layout(binding = 10) uniform sampler2D vge_refractionDepth;

/** Projects a view-space ray point without clamping offscreen samples into the image. */
bool VgeRefractionProject(vec3 position, out vec2 sampleUv)
{
    vec4 clip = projectionMatrix * vec4(position, 1.0);
    if (clip.w <= .0001) { VGE_REFRACTION_EVENT(1); return false; }
    sampleUv = clip.xy / clip.w * .5 + .5;
    vec2 margin = 2.0 / frameSize;
    bool supported = all(greaterThan(sampleUv, margin)) && all(lessThan(sampleUv, 1.0 - margin));
    if (!supported) { VGE_REFRACTION_EVENT(1); }
    return supported;
}

/** Rejects unknown depth, sky, first-person proxies and foreground shoreline leakage. */
bool VgeRefractionReceiver(vec2 sampleUv, vec3 surface, vec3 normalVS,
    mat4 inverseProjection, out float receiverZ)
{
    float depth = texture(vge_refractionDepth, sampleUv).r;
    receiverZ = VgeLiquidViewDepth(depth);
    VGE_REFRACTION_SAMPLE(sampleUv, receiverZ);
    if (isnan(depth) || isinf(depth) || depth <= 0.0 || depth >= .999999
        || texture(vge_refractionColor, sampleUv).a < .5) { VGE_REFRACTION_EVENT(2); return false; }
    vec4 position = inverseProjection * vec4(sampleUv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    if (abs(position.w) < .00001) { VGE_REFRACTION_EVENT(3); return false; }
    // Foreground is relative to the interface, not the camera's Z axis. A bent ray
    // can legitimately reach a receiver with smaller view depth when the camera pitches.
    if (dot(position.xyz / position.w - surface, normalVS) >= -.02) { VGE_REFRACTION_EVENT(4); return false; }
    // A nearest sample beside foreground geometry must not bend that foreground behind the interface.
    ivec2 pixel = ivec2(sampleUv * frameSize);
    for (int axis = 0; axis < 4; ++axis)
    {
        ivec2 offset = axis == 0 ? ivec2(-1,0) : axis == 1 ? ivec2(1,0)
            : axis == 2 ? ivec2(0,-1) : ivec2(0,1);
        float adjacent = texelFetch(vge_refractionDepth, pixel + offset, 0).r;
        if (isnan(adjacent) || isinf(adjacent) || adjacent <= 0.0) { VGE_REFRACTION_EVENT(5); return false; }
        if (adjacent < .999999)
        {
            vec2 adjacentUv = (vec2(pixel + offset) + .5) / frameSize;
            vec4 adjacentPosition = inverseProjection * vec4(adjacentUv * 2.0 - 1.0, adjacent * 2.0 - 1.0, 1.0);
            if (abs(adjacentPosition.w) < .00001
                || dot(adjacentPosition.xyz / adjacentPosition.w - surface, normalVS) >= -.02) { VGE_REFRACTION_EVENT(5); return false; }
        }
    }
    return true;
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
            || !VgeRefractionReceiver(sampleUv, surface, normalVS, inverseProjection, receiverZ)) return result;
        if (-point.z >= receiverZ)
        {
            float low = previousT;
            float high = distance;
            for (int refine = 0; refine < 5; ++refine)
            {
                float middle = (low + high) * .5;
                point = surface + direction * middle;
                if (!VgeRefractionProject(point, sampleUv)
                    || !VgeRefractionReceiver(sampleUv, surface, normalVS, inverseProjection, receiverZ)) return result;
                if (-point.z >= receiverZ) high = middle;
                else low = middle;
            }
            receiver = surface + direction * high;
            if (!VgeRefractionProject(receiver, sampleUv)
                || !VgeRefractionReceiver(sampleUv, surface, normalVS, inverseProjection, receiverZ)) return result;
            // A depth discontinuity can mimic a crossing. Missing hidden geometry is not a hit.
            if (abs(-receiver.z - receiverZ) > .15) { VGE_REFRACTION_EVENT(7); return result; }
            background = texture(vge_refractionColor, sampleUv).rgb;
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
