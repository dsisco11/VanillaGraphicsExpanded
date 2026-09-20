#ifndef LUMON_WORLDPROBE_GATHER_GLSL
#define LUMON_WORLDPROBE_GATHER_GLSL

const float LUMON_WORLDPROBE_SCREEN_WEIGHT_THRESHOLD = 0.001;
const float LUMON_WORLDPROBE_CONFIDENCE_THRESHOLD = 1e-3;

struct LumOnWorldProbeGatherFallback
{
    vec3 irradiance;
    float confidence;
    bool used;
};

LumOnWorldProbeGatherFallback lumonGatherWorldProbeFallback(
    vec3 pixelPosWS,
    vec3 pixelNormalWS,
    float screenWeight)
{
    LumOnWorldProbeGatherFallback result;
    result.irradiance = vec3(0.0);
    result.confidence = 0.0;
    result.used = false;

#if VGE_LUMON_WORLDPROBE_ENABLED
    if (screenWeight < LUMON_WORLDPROBE_SCREEN_WEIGHT_THRESHOLD)
    {
        LumOnWorldProbeSample worldProbe = lumonWorldProbeSampleClipmapBound(pixelPosWS, pixelNormalWS);
        if (worldProbe.confidence > LUMON_WORLDPROBE_CONFIDENCE_THRESHOLD)
        {
            result.irradiance = worldProbe.irradiance;
            result.confidence = worldProbe.confidence;
            result.used = true;
        }
    }
#endif

    return result;
}

LumOnWorldProbeGatherFallback lumonSampleWorldProbeGatherCandidate(
    vec3 pixelPosWS,
    vec3 pixelNormalWS)
{
    return lumonGatherWorldProbeFallback(pixelPosWS, pixelNormalWS, 0.0);
}

#endif
