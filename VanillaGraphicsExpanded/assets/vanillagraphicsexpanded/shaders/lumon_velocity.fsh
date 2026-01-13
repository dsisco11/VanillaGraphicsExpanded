#version 330 core

layout(location = 0) out vec4 outVelocity;

@import "./includes/lumon_common.glsl"

// ============================================================================
// LumOn Velocity Pass (Phase 14)
//
// Produces a per-pixel screen-space reprojection velocity without engine motion vectors.
//
// Encoding (RGBA32F):
// - RG: velocityUv = currUv - prevUv
// - B : reserved (0)
// - A : uintBitsToFloat(flags)
// ============================================================================

uniform sampler2D primaryDepth;

uniform vec2 screenSize;

uniform mat4 invCurrViewProjMatrix;
uniform mat4 prevViewProjMatrix;

// 0/1: whether previous-frame history is valid at all (first frame / resize / teleport resets)
uniform int historyValid;

const uint LUMON_VEL_FLAG_VALID                = 1u << 0;
const uint LUMON_VEL_FLAG_HISTORY_INVALID      = 1u << 1;
const uint LUMON_VEL_FLAG_SKY_OR_INVALID_DEPTH = 1u << 2;
const uint LUMON_VEL_FLAG_PREV_BEHIND_CAMERA   = 1u << 3;
const uint LUMON_VEL_FLAG_PREV_OOB             = 1u << 4;
const uint LUMON_VEL_FLAG_NAN                  = 1u << 5;

bool lumonIsNanFloat(float v)
{
    return !(v == v);
}

bool lumonIsNanVec2(vec2 v)
{
    return lumonIsNanFloat(v.x) || lumonIsNanFloat(v.y);
}

void main(void)
{
    vec2 currUv = gl_FragCoord.xy / screenSize;

    uint flags = 0u;
    vec2 velocityUv = vec2(0.0);

    if (historyValid == 0)
    {
        flags |= LUMON_VEL_FLAG_HISTORY_INVALID;
        outVelocity = vec4(velocityUv, 0.0, uintBitsToFloat(flags));
        return;
    }

    float depthRaw = texture(primaryDepth, currUv).r;
    if (lumonIsSky(depthRaw) || depthRaw <= 0.0)
    {
        flags |= LUMON_VEL_FLAG_SKY_OR_INVALID_DEPTH;
        outVelocity = vec4(velocityUv, 0.0, uintBitsToFloat(flags));
        return;
    }

    vec4 currClip = vec4(currUv * 2.0 - 1.0, depthRaw * 2.0 - 1.0, 1.0);
    vec4 worldPosH = invCurrViewProjMatrix * currClip;

    if (abs(worldPosH.w) < 1e-8)
    {
        flags |= LUMON_VEL_FLAG_NAN;
        outVelocity = vec4(velocityUv, 0.0, uintBitsToFloat(flags));
        return;
    }

    vec3 worldPos = worldPosH.xyz / worldPosH.w;

    vec4 prevClip = prevViewProjMatrix * vec4(worldPos, 1.0);
    if (prevClip.w <= 1e-8)
    {
        flags |= LUMON_VEL_FLAG_PREV_BEHIND_CAMERA;
        outVelocity = vec4(velocityUv, 0.0, uintBitsToFloat(flags));
        return;
    }

    vec2 prevNdc = prevClip.xy / prevClip.w;
    vec2 prevUv = prevNdc * 0.5 + 0.5;

    if (lumonIsNanVec2(prevUv))
    {
        flags |= LUMON_VEL_FLAG_NAN;
        outVelocity = vec4(velocityUv, 0.0, uintBitsToFloat(flags));
        return;
    }

    if (prevUv.x < 0.0 || prevUv.x > 1.0 || prevUv.y < 0.0 || prevUv.y > 1.0)
    {
        flags |= LUMON_VEL_FLAG_PREV_OOB;
        outVelocity = vec4(velocityUv, 0.0, uintBitsToFloat(flags));
        return;
    }

    flags |= LUMON_VEL_FLAG_VALID;
    velocityUv = currUv - prevUv;

    outVelocity = vec4(velocityUv, 0.0, uintBitsToFloat(flags));
}
