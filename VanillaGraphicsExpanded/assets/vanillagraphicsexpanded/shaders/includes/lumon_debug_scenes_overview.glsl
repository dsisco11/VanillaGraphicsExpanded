// Phase 23: Combined "LumOn scenes" overview debug view.
//
// Purpose:
// - Provide a single mode that visually compares the main LumOn scene inputs:
//   - LumonScene surface cache (near-field irradiance at hit patch)
//   - TraceScene occupancy/payload (L0)
//   - TraceScene materialPaletteIndex (from packed payload)
//
// Visualization: 3 vertical columns (same pixel coords, different meaning):
// - Left third  : LumonScene irradiance
// - Middle third: TraceScene occupancy presence (payload != 0) + out-of-bounds indicator
// - Right third : TraceScene materialPaletteIndex (hashed color) + out-of-bounds indicator

#ifndef LUMON_DEBUG_SCENES_OVERVIEW_GLSL
#define LUMON_DEBUG_SCENES_OVERVIEW_GLSL

// NOTE: This include is imported by `lumon_debug.fsh` after all other debug includes.
// Do not @import other debug includes here to avoid duplicate function definitions.

vec3 VgeHashColorU(uint key)
{
    uint h = Squirrel3HashU(key);
    vec3 c = vec3(
        float((h) & 255U) / 255.0,
        float((h >> 8U) & 255U) / 255.0,
        float((h >> 16U) & 255U) / 255.0);

    // Snap to visible bands (reduces noisy gradients).
    return floor(c * 6.0 + 0.5) / 6.0;
}

vec4 RenderDebug_LumOnScenesOverview(vec2 screenPos)
{
    vec2 uv01 = screenPos / screenSize;

    // Divider bars
    float div0 = abs(uv01.x - (1.0 / 3.0));
    float div1 = abs(uv01.x - (2.0 / 3.0));
    float px = 1.0 / max(1.0, screenSize.x);
    if (div0 < px || div1 < px)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // Left: LumonScene surface cache irradiance (near-field v1).
    if (uv01.x < (1.0 / 3.0))
    {
        return renderLumonSceneIrradianceDebug();
    }

    // TraceScene-derived columns require valid geometry.
    if (vge_traceSceneEnabled == 0 || vge_traceOccResolution <= 0)
    {
        return vec4(0.2, 0.0, 0.2, 1.0);
    }

    float depth = texelFetch(primaryDepth, ivec2(screenPos), 0).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec3 viewPos = lumonReconstructViewPos(uv01, depth, invProjectionMatrix);
    vec3 worldPos = (invViewMatrix * vec4(viewPos, 1.0)).xyz;
    ivec3 worldCell = ivec3(floor(worldPos));

    bool inBounds = VgeOccInBoundsL0(worldCell, vge_traceOccOriginMinCell0, vge_traceOccResolution);
    uint payloadPacked = VgeSampleOccL0(
        vge_traceOccL0,
        worldCell,
        vge_traceOccOriginMinCell0,
        vge_traceOccRing0,
        vge_traceOccResolution);

    // Middle: occupancy presence
    if (uv01.x < (2.0 / 3.0))
    {
        if (!inBounds) return vec4(0.85, 0.15, 0.15, 1.0);
        return (payloadPacked != 0U) ? vec4(1.0) : vec4(0.0, 0.0, 0.0, 1.0);
    }

    // Right: materialPaletteIndex color (hashed for stable visualization)
    if (!inBounds) return vec4(0.25, 0.05, 0.05, 1.0);
    if (payloadPacked == 0U) return vec4(0.0, 0.0, 0.0, 1.0);

    uint mat = VgeUnpackMaterialPaletteIndex(payloadPacked);
    vec3 c = VgeHashColorU(mat);
    return vec4(c, 1.0);
}

#endif // LUMON_DEBUG_SCENES_OVERVIEW_GLSL
