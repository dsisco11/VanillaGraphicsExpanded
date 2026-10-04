#ifndef VGE_WATER_UV_DISTORTION_GLSL
#define VGE_WATER_UV_DISTORTION_GLSL
@import "./transport.glsl"
@import "./receiver.glsl"
#ifndef VGE_REFRACTION_UV_SAMPLE
#define VGE_REFRACTION_UV_SAMPLE(uv)
#endif

/** Projects an approximate receiver without clamping missing coverage onto an edge texel. */
bool VgeWaterUvProject(vec3 positionVS, out vec2 sampleUv)
{
    vec4 clip = projectionMatrix * vec4(positionVS, 1.0);
    sampleUv = vec2(0);
    if (any(isnan(clip)) || any(isinf(clip)) || clip.w <= .0001) return false;
    sampleUv = clip.xy / clip.w * .5 + .5;
    return all(greaterThanEqual(sampleUv, vec2(0))) && all(lessThanEqual(sampleUv, vec2(1)));
}

/** Selects a Snell-based projected UV receiver with at most two filtered lookups and no ray search. */
VgeWaterReceiver VgeWaterUvRefraction(vec3 surface, vec3 normalVS, bool underwater)
{
    vec3 direction = refract(normalize(surface), normalVS, underwater ? 1.333 : 1.0 / 1.333);
    VgeWaterReceiver result = VgeWaterReceiver(false, VGE_WATER_RECEIVER_NONE,
        vec3(0), surface, direction, 0.0, 0.0);
    // A refracted air exit is never used as the water-side scattering direction.
    // TIR has no transmitted receiver and performs no background reads.
    if (dot(direction, direction) < .0001) { VGE_REFRACTION_EVENT(6); return result; }
    vec2 seedUv;
    if (!VgeWaterUvProject(surface, seedUv)) return result;
    mat4 inverseProjection = inverse(projectionMatrix);
    vec3 seedPosition;
    vec3 seedRadiance;
    VGE_REFRACTION_UV_SAMPLE(seedUv);
    if (!VgeRefractionFilter(seedUv, surface, normalVS, inverseProjection, seedPosition, seedRadiance)) return result;

    // The straight receiver supplies a local parallel-plane thickness estimate.
    // All distances are metres. The 2 cm eligibility bias and 25 cm ramp suppress
    // shoreline displacement; a 32 m cap and cosine floor bound grazing estimates.
    float seedThickness = max(0.0, -dot(seedPosition - surface, normalVS));
    float normalCosine = max(.1, -dot(direction, normalVS));
    float estimate = min(32.0, seedThickness / normalCosine);
    float suppression = smoothstep(.02, .25, seedThickness);
    vec3 selectedPosition = seedPosition;
    vec3 selectedRadiance = seedRadiance;
    vec2 projectedUv;
    if (suppression > 0.0 && VgeWaterUvProject(surface + direction * estimate, projectedUv))
    {
        vec2 candidateUv = mix(seedUv, projectedUv, suppression);
        vec3 candidatePosition;
        vec3 candidateRadiance;
        VGE_REFRACTION_UV_SAMPLE(candidateUv);
        // Unsupported candidate taps cannot import foreground radiance. Preserve
        // the validated seed instead of clamping an offscreen ray into the image.
        if (VgeRefractionFilter(candidateUv, surface, normalVS, inverseProjection, candidatePosition, candidateRadiance))
        {
            selectedPosition = candidatePosition;
            selectedRadiance = candidateRadiance;
        }
    }
    // This is a one-interface planar approximation, not a confirmed intersection.
    // Underwater the camera-to-interface segment is water; the sampled exit is air.
    float pathLength = underwater ? length(surface)
        : min(32.0, max(0.0, -dot(selectedPosition - surface, normalVS)) / normalCosine);
    return VgeWaterReceiver(true, VGE_WATER_RECEIVER_UV, selectedRadiance,
        selectedPosition, direction, pathLength, 1.0);
}
#endif
