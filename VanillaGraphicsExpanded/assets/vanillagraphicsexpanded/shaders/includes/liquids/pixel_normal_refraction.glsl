#ifndef VGE_WATER_PIXEL_NORMAL_REFRACTION_GLSL
#define VGE_WATER_PIXEL_NORMAL_REFRACTION_GLSL
@import "./uv_distortion.glsl"

/** Samples bounded normal-detail distortion without projecting a Snell receiver endpoint. */
VgeWaterReceiver VgeWaterPixelNormalRefraction(vec3 surface, vec3 normalVS, vec3 baseNormalVS, bool underwater)
{
    vec3 direction = refract(normalize(surface), normalVS, underwater ? 1.333 : 1.0 / 1.333);
    VgeWaterReceiver result = VgeWaterReceiver(false, VGE_WATER_RECEIVER_NONE,
        vec3(0), surface, direction, 0.0, 0.0);
    // Keep the optical direction for scattering and TIR, not for the screen offset.
    if (dot(direction, direction) < .0001) { VGE_REFRACTION_EVENT(6); return result; }
    vec2 seedUv;
    if (!VgeWaterUvProject(surface, false, seedUv)) return result;
    VgeRefractionSupport support;
    VGE_REFRACTION_UV_SAMPLE(seedUv);
    if (!VgeRefractionFilterSupport(seedUv, surface, normalVS, inverseProjectionMatrix, support)) return result;

    // UE-style pixel-normal offset: flat surfaces have no displacement. Use a
    // resolution-independent strength with projection scaling in our metre-based
    // renderer, and fade distortion over the first 30 cm of axial separation.
    const float strength = .02;
    float shallow = clamp((surface.z - support.positionVS.z) / .3, 0.0, 1.0);
    vec2 offset = (baseNormalVS.xy - normalVS.xy)
        * vec2(projectionMatrix[0][0], projectionMatrix[1][1]) * strength * shallow;
    vec2 inset = min(vec2(.5), vec2(.55) / vec2(textureSize(vge_refractionDepth, 0)));
    vec2 sampleUv = seedUv;
    if (dot(offset, offset) > 0.0)
    {
        vec2 candidateUv = clamp(seedUv + offset, inset, vec2(1) - inset);
        VgeRefractionSupport candidate;
        VGE_REFRACTION_UV_SAMPLE(candidateUv);
        // Edge continuation cannot authorize foreground or unavailable receivers.
        if (VgeRefractionFilterSupport(candidateUv, surface, normalVS, inverseProjectionMatrix, candidate))
        {
            support = candidate;
            sampleUv = candidateUv;
        }
    }
    vec3 position = support.positionVS, radiance;
    vec3 correctedPosition, correctedRadiance;
    if (VgeWaterUvPatch(sampleUv, inverseProjectionMatrix, support, correctedPosition, correctedRadiance))
    {
        position = correctedPosition;
        radiance = correctedRadiance;
    }
    else if (!VgeRefractionRadiance(support, radiance)) return result;
    float pathLength = underwater ? length(surface)
        : min(32.0, max(0.0, -dot(position - surface, normalVS)) / max(.1, -dot(direction, normalVS)));
    return VgeWaterReceiver(true, VGE_WATER_RECEIVER_UV, radiance, position, direction, pathLength, 1.0);
}
#endif
