// Phase 23.7: TraceScene debug views (occupancy clipmap).

#ifndef LUMON_DEBUG_TRACESCENE_GLSL
#define LUMON_DEBUG_TRACESCENE_GLSL

@import "./lumon_common.glsl"
@import "./lumonscene_trace_scene_occupancy.glsl"

// Packed payload layout (R32UI):
// - bits  0..5  : blockLightLevel (0..63; gameplay currently clamps to 0..32)
// - bits  6..11 : sunLevel        (0..63; gameplay currently clamps to 0..32)
// - bits 12..17 : lightId         (0..63)
// - bits 18..31 : materialPaletteIndex (0..16383)
uint VgeUnpackBlockLevel(uint payloadPacked)
{
    return payloadPacked & 63U;
}

uint VgeUnpackSunLevel(uint payloadPacked)
{
    return (payloadPacked >> 6U) & 63U;
}

uint VgeUnpackLightId(uint payloadPacked)
{
    return (payloadPacked >> 12U) & 63U;
}

uint VgeUnpackMaterialPaletteIndex(uint payloadPacked)
{
    return (payloadPacked >> 18U) & 16383U;
}

vec4 RenderDebug_TraceScene(vec2 screenPos)
{
    if (vge_traceSceneEnabled == 0 || vge_traceOccResolution <= 0)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec2 uv = screenPos / screenSize;

    float depth = texelFetch(primaryDepth, ivec2(screenPos), 0).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec3 viewPos = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 worldPos = (invViewMatrix * vec4(viewPos, 1.0)).xyz;

    ivec3 worldCell = ivec3(floor(worldPos));

    bool inBounds = VgeOccInBoundsL0(worldCell, vge_traceOccOriginMinCell0, vge_traceOccResolution);
    uint payloadPacked = VgeSampleOccL0(vge_traceOccL0, worldCell, vge_traceOccOriginMinCell0, vge_traceOccRing0, vge_traceOccResolution);

    // Modes:
    // 55: bounds in/out
    // 56: occupancy presence (payload != 0)
    // 57: payload decode preview (block/sun/light/material)
    if (debugMode == 55)
    {
        return inBounds ? vec4(0.15, 0.85, 0.15, 1.0) : vec4(0.85, 0.15, 0.15, 1.0);
    }

    if (!inBounds)
    {
        return vec4(0.05, 0.05, 0.05, 1.0);
    }

    if (debugMode == 56)
    {
        return payloadPacked != 0U ? vec4(1.0) : vec4(0.0, 0.0, 0.0, 1.0);
    }

    if (debugMode == 57)
    {
        if (payloadPacked == 0U)
        {
            return vec4(0.0, 0.0, 0.0, 1.0);
        }

        float blockLevel = clamp(float(min(VgeUnpackBlockLevel(payloadPacked), 32U)) / 32.0, 0.0, 1.0);
        float sunLevel = clamp(float(min(VgeUnpackSunLevel(payloadPacked), 32U)) / 32.0, 0.0, 1.0);
        float lightId = clamp(float(min(VgeUnpackLightId(payloadPacked), 63U)) / 63.0, 0.0, 1.0);
        float mat = clamp(float(VgeUnpackMaterialPaletteIndex(payloadPacked)) / 16383.0, 0.0, 1.0);
        return vec4(blockLevel, sunLevel, lightId, 1.0) * vec4(1.0, 1.0, 1.0, 1.0 - 0.5 * mat);
    }

    return vec4(1.0, 0.0, 1.0, 1.0);
}

#endif // LUMON_DEBUG_TRACESCENE_GLSL
