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

struct LumOnWorldProbeGatherResult
{
    vec3 irradiance;
    float confidence;
    bool usedWorldProbe;
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

LumOnWorldProbeGatherResult lumonResolveWorldProbeGather(
    vec3 screenIrradiance,
    float screenConfidence,
    float screenWeight,
    vec3 pixelPosWS,
    vec3 pixelNormalWS)
{
    LumOnWorldProbeGatherResult result;
    result.irradiance = screenIrradiance;
    result.confidence = screenConfidence;
    result.usedWorldProbe = false;

    LumOnWorldProbeGatherFallback worldProbe = lumonGatherWorldProbeFallback(
        pixelPosWS,
        pixelNormalWS,
        screenWeight);
    if (worldProbe.used)
    {
        result.irradiance = worldProbe.irradiance;
        result.confidence = worldProbe.confidence;
        result.usedWorldProbe = true;
    }

    return result;
}

#endif
