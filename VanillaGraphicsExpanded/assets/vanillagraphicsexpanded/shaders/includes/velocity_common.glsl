#ifndef LUMON_VELOCITY_COMMON_GLSL
#define LUMON_VELOCITY_COMMON_GLSL

// ============================================================================
// LumOn Velocity Common
//
// Shared constants and helpers for the Phase 14 velocity buffer.
//
// Velocity texture encoding (RGBA32F):
// - RG: velocityUv = currUv - prevUv
// - B : reserved
// - A : uintBitsToFloat(flags)
// ============================================================================

// Flags (packed into float via uintBitsToFloat / floatBitsToUint).
const uint LUMON_VEL_FLAG_VALID                = 1u << 0;
const uint LUMON_VEL_FLAG_HISTORY_INVALID      = 1u << 1;
const uint LUMON_VEL_FLAG_SKY_OR_INVALID_DEPTH = 1u << 2;
const uint LUMON_VEL_FLAG_PREV_BEHIND_CAMERA   = 1u << 3;
const uint LUMON_VEL_FLAG_PREV_OOB             = 1u << 4;
const uint LUMON_VEL_FLAG_NAN                  = 1u << 5;

uint lumonVelocityDecodeFlags(vec4 velocitySample)
{
    return floatBitsToUint(velocitySample.a);
}

vec2 lumonVelocityDecodeUv(vec4 velocitySample)
{
    return velocitySample.xy;
}

bool lumonVelocityIsValid(uint flags)
{
    return (flags & LUMON_VEL_FLAG_VALID) != 0u;
}

float lumonVelocityMagnitude(vec2 velocityUv)
{
    return length(velocityUv);
}

bool lumonIsNanFloat(float v)
{
    // NaN is the only float where v != v.
    return !(v == v);
}

bool lumonIsNanVec2(vec2 v)
{
    return lumonIsNanFloat(v.x) || lumonIsNanFloat(v.y);
}

#endif // LUMON_VELOCITY_COMMON_GLSL
