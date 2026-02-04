// Phase 23.7: TraceScene debug views (occupancy clipmap).

#ifndef LUMON_DEBUG_TRACESCENE_GLSL
#define LUMON_DEBUG_TRACESCENE_GLSL

@import "./lumon_common.glsl"
@import "./lumonscene_trace_scene_occupancy.glsl"
@import "./vge_worldspace_bridge.glsl"

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
    // Note: `invViewMatrix` produces the engine's render "matrix space" positions.
    // Convert to absolute world cell coords via `vge_worldspace_bridge.glsl` before sampling the occupancy clipmap.
    vec3 worldPosRel = (invViewMatrix * vec4(viewPos, 1.0)).xyz;

    // Depth reconstruction lands on the visible surface (often on a voxel face boundary), so floor(worldPos)
    // can select the "outside" (air) cell. Step slightly into the surface for occupancy queries.
    //
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
    uint payloadPacked = VgeSampleOccL0(vge_traceOccL0, worldCell, vge_traceOccOriginMinCell0, vge_traceOccRing0, vge_traceOccResolution);

    // Modes:
    // 55: bounds in/out
    // 56: occupancy presence (payload != 0)
    // 57: payload decode preview (block/sun/light/material)
    // 67: voxel DDA distance-to-hit through the occupancy clipmap (SDF-style grayscale)
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
        // Occupancy = solid presence (materialPaletteIndex != 0). Air cells may still carry lighting payload.
        bool isSolid = VgeUnpackMaterialPaletteIndex(payloadPacked) != 0U;
        return isSolid ? vec4(1.0) : vec4(0.0, 0.0, 0.0, 1.0);
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

    if (debugMode == 67)
    {
        // Visualize the TraceScene occupancy volume by tracing a ray from the camera through the clipmap bounds
        // using voxel DDA, then mapping distance-to-first-solid to grayscale.

        vec2 uvRay = screenPos / screenSize;

        // Build a view ray by unprojecting a far-plane point.
        vec3 viewFar = lumonReconstructViewPos(uvRay, 1.0, invProjectionMatrix);
        vec3 viewDir = normalize(viewFar);

        vec3 camPosRel = (invViewMatrix * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
        vec3 rayDirRel = normalize((invViewMatrix * vec4(viewDir, 0.0)).xyz);

        // Convert ray origin to absolute world block coords (cell grid is in absolute world space).
        vec3 rayOrigin = VgeMatrixSpacePosToWorldPosAbs(camPosRel);
        vec3 rayDir = rayDirRel;

        // Clip to the clipmap AABB in world block units.
        vec3 aabbMin = vec3(vge_traceOccOriginMinCell0);
        vec3 aabbMax = aabbMin + vec3(float(vge_traceOccResolution));

        // Robust slab intersection (handles near-parallel rays).
        float tEnter = -1e30;
        float tExit = 1e30;

        if (abs(rayDir.x) < 1e-8)
        {
            if (rayOrigin.x < aabbMin.x || rayOrigin.x > aabbMax.x)
            {
                return vec4(0.0, 0.0, 0.0, 1.0);
            }
        }
        else
        {
            float invX = 1.0 / rayDir.x;
            float tx0 = (aabbMin.x - rayOrigin.x) * invX;
            float tx1 = (aabbMax.x - rayOrigin.x) * invX;
            tEnter = max(tEnter, min(tx0, tx1));
            tExit = min(tExit, max(tx0, tx1));
        }

        if (abs(rayDir.y) < 1e-8)
        {
            if (rayOrigin.y < aabbMin.y || rayOrigin.y > aabbMax.y)
            {
                return vec4(0.0, 0.0, 0.0, 1.0);
            }
        }
        else
        {
            float invY = 1.0 / rayDir.y;
            float ty0 = (aabbMin.y - rayOrigin.y) * invY;
            float ty1 = (aabbMax.y - rayOrigin.y) * invY;
            tEnter = max(tEnter, min(ty0, ty1));
            tExit = min(tExit, max(ty0, ty1));
        }

        if (abs(rayDir.z) < 1e-8)
        {
            if (rayOrigin.z < aabbMin.z || rayOrigin.z > aabbMax.z)
            {
                return vec4(0.0, 0.0, 0.0, 1.0);
            }
        }
        else
        {
            float invZ = 1.0 / rayDir.z;
            float tz0 = (aabbMin.z - rayOrigin.z) * invZ;
            float tz1 = (aabbMax.z - rayOrigin.z) * invZ;
            tEnter = max(tEnter, min(tz0, tz1));
            tExit = min(tExit, max(tz0, tz1));
        }

        if (tExit < max(tEnter, 0.0))
        {
            return vec4(0.0, 0.0, 0.0, 1.0);
        }

        float t = max(tEnter, 0.0) + 1e-3;
        vec3 p = rayOrigin + rayDir * t;
        ivec3 cell = ivec3(floor(p));

        ivec3 cellStep = ivec3(sign(rayDir));

        vec3 cellMin = vec3(cell);
        vec3 nextBoundary = cellMin + vec3(cellStep.x > 0 ? 1.0 : 0.0, cellStep.y > 0 ? 1.0 : 0.0, cellStep.z > 0 ? 1.0 : 0.0);

        vec3 tMax = vec3(1e30);
        vec3 tDelta = vec3(1e30);

        if (abs(rayDir.x) > 1e-8)
        {
            tMax.x = (nextBoundary.x - p.x) / rayDir.x;
            tDelta.x = float(cellStep.x) / rayDir.x;
        }
        if (abs(rayDir.y) > 1e-8)
        {
            tMax.y = (nextBoundary.y - p.y) / rayDir.y;
            tDelta.y = float(cellStep.y) / rayDir.y;
        }
        if (abs(rayDir.z) > 1e-8)
        {
            tMax.z = (nextBoundary.z - p.z) / rayDir.z;
            tDelta.z = float(cellStep.z) / rayDir.z;
        }

        tMax = tMax + t;
        tDelta = abs(tDelta);

        float hitT = -1.0;

        int maxSteps = min(1024, vge_traceOccResolution * 4);
        for (int i = 0; i < maxSteps; i++)
        {
            if (!VgeOccInBoundsL0(cell, vge_traceOccOriginMinCell0, vge_traceOccResolution))
            {
                break;
            }

            uint payloadPackedCell = VgeSampleOccL0(vge_traceOccL0, cell, vge_traceOccOriginMinCell0, vge_traceOccRing0, vge_traceOccResolution);
            bool isSolid = VgeUnpackMaterialPaletteIndex(payloadPackedCell) != 0U;
            if (isSolid)
            {
                hitT = t;
                break;
            }

            // Advance to the next voxel boundary.
            if (tMax.x < tMax.y)
            {
                if (tMax.x < tMax.z)
                {
                    t = tMax.x;
                    tMax.x += tDelta.x;
                    cell.x += cellStep.x;
                }
                else
                {
                    t = tMax.z;
                    tMax.z += tDelta.z;
                    cell.z += cellStep.z;
                }
            }
            else
            {
                if (tMax.y < tMax.z)
                {
                    t = tMax.y;
                    tMax.y += tDelta.y;
                    cell.y += cellStep.y;
                }
                else
                {
                    t = tMax.z;
                    tMax.z += tDelta.z;
                    cell.z += cellStep.z;
                }
            }

            if (t > tExit)
            {
                break;
            }
        }

        if (hitT < 0.0)
        {
            return vec4(0.0, 0.0, 0.0, 1.0);
        }

        // SDF-style falloff: bright near surfaces, dark further away.
        float rangeBlocks = 48.0;
        float v = 1.0 - clamp(hitT / max(1.0, rangeBlocks), 0.0, 1.0);
        v = pow(v, 0.5);
        return vec4(v, v, v, 1.0);
    }

    return vec4(1.0, 0.0, 1.0, 1.0);
}

#endif // LUMON_DEBUG_TRACESCENE_GLSL
