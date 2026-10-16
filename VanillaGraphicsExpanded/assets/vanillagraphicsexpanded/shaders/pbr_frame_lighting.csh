#version 430 core
layout(local_size_x=1) in;
layout(std140, binding=28) uniform FrameLightingInputs { vec4 boundAndSeed; uvec4 identity; };
layout(std430, binding=0) readonly buffer PreviousExposure { float previousLogGain; };
layout(std430, binding=1) writeonly buffer FrozenFrameLighting { vec4 lightingStorage; uvec4 lightingIdentity; };
/** Freezes camera-driven storage independently of exceptional scene peaks and subsequent metering. */
void main()
{
    float multiplier = boundAndSeed.y;
    if (boundAndSeed.z > .5)
    {
        float candidate = exp2(previousLogGain);
        if (!isnan(candidate) && !isinf(candidate) && candidate > 0.0 && !isinf(candidate * 16.0)) multiplier = candidate;
    }
    // Scene writes contain values exceeding their actual destination range. A single extreme
    // highlight must not push separate faint pixels below packed/half precision by lowering P.
    float scale = max(multiplier, 7.147944e-6);
    bool valid = !isnan(scale) && !isinf(scale) && scale >= 7.147944e-6
        && !isinf(scale * 16.0)
        && !isnan(boundAndSeed.x) && !isinf(boundAndSeed.x) && boundAndSeed.x >= 0.0
        && identity.y != 0u;
    lightingStorage = vec4(scale, 1.0 / scale, boundAndSeed.x, valid ? 1.0 : 0.0);
    lightingIdentity = identity;
}
