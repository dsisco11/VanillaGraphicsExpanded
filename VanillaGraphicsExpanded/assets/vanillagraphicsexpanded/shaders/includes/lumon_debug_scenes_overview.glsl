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

@import "./vge_worldspace_bridge.glsl"

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
    // Note: `invViewMatrix` produces the engine's render "matrix space" positions.
    // Convert to absolute world cell coords via `vge_worldspace_bridge.glsl` before sampling the occupancy clipmap.
    vec3 worldPosRel = (invViewMatrix * vec4(viewPos, 1.0)).xyz;

    // Match TraceScene occupancy queries to the solid side of the visible surface.
    // Prefer PatchId axis (robust for voxel terrain), fall back to GBuffer normal.
    uvec4 pid = texelFetch(gBufferPatchId, ivec2(screenPos), 0);
    uint patchId = pid.y;

    vec3 stepN = vec3(0.0);
    if (patchId != 0U)
    {
        uint axisId = (patchId - 1U) % 6U;
        if (axisId == 0U) stepN = vec3( 1.0, 0.0, 0.0);
        if (axisId == 1U) stepN = vec3(-1.0, 0.0, 0.0);
        if (axisId == 2U) stepN = vec3( 0.0, 1.0, 0.0);
        if (axisId == 3U) stepN = vec3( 0.0,-1.0, 0.0);
        if (axisId == 4U) stepN = vec3( 0.0, 0.0, 1.0);
        if (axisId == 5U) stepN = vec3( 0.0, 0.0,-1.0);
    }
    else
    {
        vec3 n01 = texelFetch(gBufferNormal, ivec2(screenPos), 0).xyz;
        vec3 normalWS = normalize(n01 * 2.0 - 1.0);
        if (dot(normalWS, normalWS) > 1e-6)
        {
            stepN = normalWS;
        }
    }

    vec3 occPosRel = worldPosRel;
    if (dot(stepN, stepN) > 1e-6)
    {
        occPosRel = worldPosRel - stepN * 0.51;
    }

    ivec3 worldCell = VgeMatrixSpacePosToWorldCell(occPosRel);

    bool inBounds = VgeOccInBoundsL0(worldCell, vge_traceOccOriginMinCell0, vge_traceOccResolution);
    uint payloadPacked = VgeSampleOccL0(
        vge_traceOccL0,
        worldCell,
        vge_traceOccOriginMinCell0,
        vge_traceOccRing0,
        vge_traceOccResolution);
    bool isSolid = VgeUnpackMaterialPaletteIndex(payloadPacked) != 0U;

    // Middle: occupancy presence
    if (uv01.x < (2.0 / 3.0))
    {
        if (!inBounds) return vec4(0.85, 0.15, 0.15, 1.0);
        return isSolid ? vec4(1.0) : vec4(0.0, 0.0, 0.0, 1.0);
    }

    // Right: materialPaletteIndex color (hashed for stable visualization)
    if (!inBounds) return vec4(0.25, 0.05, 0.05, 1.0);
    if (!isSolid) return vec4(0.0, 0.0, 0.0, 1.0);

    uint mat = VgeUnpackMaterialPaletteIndex(payloadPacked);
    vec3 c = VgeHashColorU(mat);
    return vec4(c, 1.0);
}

#endif // LUMON_DEBUG_SCENES_OVERVIEW_GLSL
