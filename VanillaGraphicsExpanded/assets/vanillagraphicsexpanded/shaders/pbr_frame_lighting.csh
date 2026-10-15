#version 430 core
layout(local_size_x=1) in;
layout(std140, binding=28) uniform FrameLightingInputs { vec4 boundAndSeed; uvec4 identity; };
layout(std430, binding=0) readonly buffer PreviousExposure { float previousLogGain; };
layout(std430, binding=1) writeonly buffer FrozenFrameLighting { vec4 lightingStorage; uvec4 lightingIdentity; };
/** Freezes a downward-rounded source-informed scale; metering later writes a different exposure buffer. */
void main()
{
    float multiplier = boundAndSeed.y;
    if (boundAndSeed.z > .5)
    {
        float candidate = exp2(previousLogGain);
        if (!isnan(candidate) && !isinf(candidate) && candidate > 0.0) multiplier = candidate;
    }
    float scale = max(multiplier, .0625);
    if (boundAndSeed.x > 0.0)
    {
        // One predecessor is conservative for a correctly rounded positive Float32 division.
        float guard = uintBitsToFloat(floatBitsToUint(16000.0 / boundAndSeed.x) - 1u);
        scale = min(scale, guard);
    }
    bool valid = !isnan(scale) && !isinf(scale) && scale >= 7.147944e-6
        && !isinf(scale * 16.0)
        && !isnan(boundAndSeed.x) && !isinf(boundAndSeed.x) && boundAndSeed.x >= 0.0
        && identity.y != 0u;
    lightingStorage = vec4(scale, 1.0 / scale, boundAndSeed.x, valid ? 1.0 : 0.0);
    lightingIdentity = identity;
}
