// Scene / GBuffer debug views

// Debug Mode 41: POM Metrics
vec4 renderPomMetricsDebug()
{
    // Patched chunk shaders optionally write a scalar diagnostic into gBufferNormal.w.
    // Interpretation is controlled by MaterialAtlas.PomDebugMode.
    float v = clamp(texture(gBufferNormal, uv).w, 0.0, 1.0);
    vec3 c = vec3(1.0 - v, v, 0.0);
    return vec4(c, 1.0);
}

// Debug Mode 29: Material Bands (Phase 7)
vec4 renderMaterialBandsDebug()
{
    vec4 m = clamp(texture(gBufferMaterial, uv), 0.0, 1.0);

    // Quantize to 8-bit per channel before hashing to make the visualization stable.
    uvec4 q = uvec4(m * 255.0 + 0.5);

    uint key = (q.r) | (q.g << 8) | (q.b << 16) | (q.a << 24);
    uint h = Squirrel3HashU(key);

    vec3 c = vec3(
        float((h) & 255u) / 255.0,
        float((h >> 8) & 255u) / 255.0,
        float((h >> 16) & 255u) / 255.0);

    // Snap to visible bands (reduces noisy gradients).
    c = floor(c * 6.0 + 0.5) / 6.0;

    return vec4(c, 1.0);
}

// Debug Mode 4: Scene Depth
vec4 renderSceneDepthDebug()
{
    float depth = texture(primaryDepth, uv).r;

    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    float linearDepth = lumonLinearizeDepth(depth, zNear, zFar);
    float normalizedDepth = linearDepth / 100.0;  // Normalize to ~100m

    return vec4(heatmap(normalizedDepth), 1.0);
}

// Debug Mode 5: Scene Normals
vec4 renderSceneNormalDebug()
{
    float depth = texture(primaryDepth, uv).r;

    if (lumonIsSky(depth))
    {
        return vec4(0.5, 0.5, 1.0, 1.0);  // Sky blue for no geometry
    }

    // Decode normal from G-buffer [0,1] to [-1,1], then re-encode for visualization
    vec3 normalEncoded = texture(gBufferNormal, uv).xyz;
    vec3 normalDecoded = lumonDecodeNormal(normalEncoded);
    // Display as color: remap [-1,1] to [0,1] so all directions are visible
    return vec4(normalDecoded * 0.5 + 0.5, 1.0);
}

// Debug Mode 52: LumonScene page readiness (Near field v1)
vec4 renderLumonScenePageReadyDebug()
{
    if (vge_lumonSceneEnabled == 0)
    {
        return vec4(0.2, 0.0, 0.2, 1.0);
    }

    uvec4 pid = texelFetch(gBufferPatchId, ivec2(gl_FragCoord.xy), 0);
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }
    if (!lumonIsSky(depth) && pid.y == 0u)
    {
        // Geometry present but no PatchId written (or suppressed): make this obvious.
        return vec4(0.8, 0.0, 0.8, 1.0);
    }

    uint chunkSlot, patchId;
    vec2 patchUv01;
    if (!VgeLumonSceneTryDecodePatchId(pid, chunkSlot, patchId, patchUv01))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec3 irr;
    uint flags;
    uint physicalPageId;
    bool ok = VgeLumonSceneTrySampleIrradiance_NearFieldV1(
        chunkSlot,
        patchId,
        patchUv01,
        vge_lumonScenePageTableMip0,
        vge_lumonSceneIrradianceAtlas,
        vge_lumonSceneTileSizeTexels,
        vge_lumonSceneTilesPerAxis,
        vge_lumonSceneTilesPerAtlas,
        irr,
        flags,
        physicalPageId);

    if (ok)
    {
        return vec4(0.0, 1.0, 0.0, 1.0); // ready
    }

    // Not ready: show coarse reason based on flags. Missing (no Resident) = red; needs work = yellow.
    if ((flags & VGE_LUMONSCENE_FLAG_RESIDENT) != 0u)
    {
        return vec4(1.0, 1.0, 0.0, 1.0);
    }

    return vec4(1.0, 0.0, 0.0, 1.0);
}

// Debug Mode 53: LumonScene patchUV visualization (Near field v1)
vec4 renderLumonScenePatchUvDebug()
{
    if (vge_lumonSceneEnabled == 0)
    {
        return vec4(0.2, 0.0, 0.2, 1.0);
    }

    uvec4 pid = texelFetch(gBufferPatchId, ivec2(gl_FragCoord.xy), 0);
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }
    if (!lumonIsSky(depth) && pid.y == 0u)
    {
        return vec4(0.8, 0.0, 0.8, 1.0);
    }

    uint chunkSlot, patchId;
    vec2 patchUv01;
    if (!VgeLumonSceneTryDecodePatchId(pid, chunkSlot, patchId, patchUv01))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    return vec4(patchUv01, 1.0, 1.0);
}

// Debug Mode 54: LumonScene irradiance preview (Near field v1)
vec4 renderLumonSceneIrradianceDebug()
{
    if (vge_lumonSceneEnabled == 0)
    {
        return vec4(0.2, 0.0, 0.2, 1.0);
    }

    uvec4 pid = texelFetch(gBufferPatchId, ivec2(gl_FragCoord.xy), 0);
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }
    if (!lumonIsSky(depth) && pid.y == 0u)
    {
        return vec4(0.8, 0.0, 0.8, 1.0);
    }

    uint chunkSlot, patchId;
    vec2 patchUv01;
    if (!VgeLumonSceneTryDecodePatchId(pid, chunkSlot, patchId, patchUv01))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec3 irr;
    uint flags;
    uint physicalPageId;
    bool ok = VgeLumonSceneTrySampleIrradiance_NearFieldV1(
        chunkSlot,
        patchId,
        patchUv01,
        vge_lumonScenePageTableMip0,
        vge_lumonSceneIrradianceAtlas,
        vge_lumonSceneTileSizeTexels,
        vge_lumonSceneTilesPerAxis,
        vge_lumonSceneTilesPerAtlas,
        irr,
        flags,
        physicalPageId);

    if (!ok)
    {
        if (physicalPageId == 0u)
        {
            return vec4(0.0, 0.0, 0.0, 1.0);
        }

        if ((flags & VGE_LUMONSCENE_FLAG_RESIDENT) == 0u)
        {
            return vec4(1.0, 0.0, 0.0, 1.0);
        }

        if ((flags & VGE_LUMONSCENE_FLAG_NEEDS_CAPTURE) != 0u)
        {
            return vec4(1.0, 1.0, 0.0, 1.0);
        }

        if ((flags & VGE_LUMONSCENE_FLAG_NEEDS_RELIGHT) != 0u)
        {
            return vec4(0.2, 0.4, 1.0, 1.0);
        }

        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // Simple tonemap for preview.
    vec3 c = irr / (irr + vec3(1.0));

    // If the page is still marked NeedsRelight, tint slightly blue to indicate "in progress".
    if ((flags & VGE_LUMONSCENE_FLAG_NEEDS_RELIGHT) != 0u)
    {
        c = mix(c, vec3(0.2, 0.4, 1.0), 0.25);
    }
    return vec4(clamp(c, 0.0, 1.0), 1.0);
}

// Debug Mode 62: LumonScene material preview (Near field v1)
vec4 renderLumonSceneMaterialDebug()
{
    if (vge_lumonSceneEnabled == 0)
    {
        return vec4(0.2, 0.0, 0.2, 1.0);
    }

    uvec4 pid = texelFetch(gBufferPatchId, ivec2(gl_FragCoord.xy), 0);
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }
    if (!lumonIsSky(depth) && pid.y == 0u)
    {
        return vec4(0.8, 0.0, 0.8, 1.0);
    }

    uint chunkSlot, patchId;
    vec2 patchUv01;
    if (!VgeLumonSceneTryDecodePatchId(pid, chunkSlot, patchId, patchUv01))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec4 mat;
    uint flags;
    uint physicalPageId;
    bool ok = VgeLumonSceneTrySampleMaterial_NearFieldV1(
        chunkSlot,
        patchId,
        patchUv01,
        vge_lumonScenePageTableMip0,
        vge_lumonSceneMaterialAtlas,
        vge_lumonSceneTileSizeTexels,
        vge_lumonSceneTilesPerAxis,
        vge_lumonSceneTilesPerAtlas,
        mat,
        flags,
        physicalPageId);

    if (!ok)
    {
        if (physicalPageId == 0u)
        {
            return vec4(0.0, 0.0, 0.0, 1.0);
        }

        if ((flags & VGE_LUMONSCENE_FLAG_RESIDENT) == 0u)
        {
            return vec4(1.0, 0.0, 0.0, 1.0);
        }

        if ((flags & VGE_LUMONSCENE_FLAG_NEEDS_CAPTURE) != 0u)
        {
            return vec4(1.0, 1.0, 0.0, 1.0);
        }

        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // v1: material atlas contents are RGBA8 (baseColor rgb, roughness a).
    return vec4(clamp(mat.rgb, 0.0, 1.0), 1.0);
}

// Debug Mode 63: LumonScene material atlas visualization (all layers)
// Shows the raw material atlas contents laid out as an NxM grid of array layers.
vec4 renderLumonSceneMaterialAtlasAllDebug()
{
    if (vge_lumonSceneEnabled == 0)
    {
        return vec4(0.2, 0.0, 0.2, 1.0);
    }

    ivec3 sz = textureSize(vge_lumonSceneMaterialAtlas, 0);
    int layers = max(1, sz.z);

    // Grid dims: ceil(sqrt(layers)) by ceil(layers / gridX)
    int gridX = int(ceil(sqrt(float(layers))));
    int gridY = int(ceil(float(layers) / float(max(1, gridX))));

    vec2 uv01 = gl_FragCoord.xy / screenSize;

    int cx = int(floor(uv01.x * float(gridX)));
    int cy = int(floor(uv01.y * float(gridY)));
    if (cx < 0 || cy < 0 || cx >= gridX || cy >= gridY)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    int layer = cy * gridX + cx;
    if (layer < 0 || layer >= layers)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // Local UV inside the selected cell.
    vec2 cellUv = fract(vec2(uv01.x * float(gridX), uv01.y * float(gridY)));

    // Draw a subtle grid border.
    float px = 1.0 / max(1.0, screenSize.x);
    float py = 1.0 / max(1.0, screenSize.y);
    if (cellUv.x < px || cellUv.y < py || (1.0 - cellUv.x) < px || (1.0 - cellUv.y) < py)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec4 mat = texture(vge_lumonSceneMaterialAtlas, vec3(cellUv, float(layer)));
    return vec4(clamp(mat.rgb, 0.0, 1.0), 1.0);
}

// Debug Mode 59: LumonScene chunkSlot visualization (Phase 22.X)
vec4 renderLumonSceneChunkSlotDebug()
{
    uvec4 pid = texelFetch(gBufferPatchId, ivec2(gl_FragCoord.xy), 0);
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }
    if (!lumonIsSky(depth) && pid.y == 0u)
    {
        return vec4(0.8, 0.0, 0.8, 1.0);
    }

    uint chunkSlot, patchId;
    vec2 patchUv01;
    if (!VgeLumonSceneTryDecodePatchId(pid, chunkSlot, patchId, patchUv01))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // Hash the slot into a stable color.
    uint h = Squirrel3HashU(chunkSlot);
    vec3 c = vec3(
        float((h) & 255u) / 255.0,
        float((h >> 8) & 255u) / 255.0,
        float((h >> 16) & 255u) / 255.0);

    // Make slot 0 obvious (dark gray).
    if (chunkSlot == 0u)
    {
        c = vec3(0.15);
    }

    return vec4(c, 1.0);
}

// Debug Mode 60: LumonScene slot generation visualization (Phase 22.X)
vec4 renderLumonSceneSlotGenerationDebug()
{
    uvec4 pid = texelFetch(gBufferPatchId, ivec2(gl_FragCoord.xy), 0);
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }
    if (!lumonIsSky(depth) && pid.y == 0u)
    {
        return vec4(0.8, 0.0, 0.8, 1.0);
    }

    uint chunkSlot, patchId;
    vec2 patchUv01;
    if (!VgeLumonSceneTryDecodePatchId(pid, chunkSlot, patchId, patchUv01))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // Convention: PatchIdGBuffer.w stores generation (low 16 bits) when enabled.
    uint gen16 = pid.w & 0xFFFFu;

    // Visualize as a repeating grayscale ramp.
    float g = float(gen16 & 255u) / 255.0;
    return vec4(vec3(g), 1.0);
}

// Debug Mode 61: LumonScene page table occupancy visualization (Phase 22.X)
vec4 renderLumonScenePageTableOccupancyDebug()
{
    if (vge_lumonSceneEnabled == 0)
    {
        return vec4(0.2, 0.0, 0.2, 1.0);
    }

    uvec4 pid = texelFetch(gBufferPatchId, ivec2(gl_FragCoord.xy), 0);
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }
    if (!lumonIsSky(depth) && pid.y == 0u)
    {
        return vec4(0.8, 0.0, 0.8, 1.0);
    }

    uint chunkSlot, patchId;
    vec2 patchUv01;
    if (!VgeLumonSceneTryDecodePatchId(pid, chunkSlot, patchId, patchUv01))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    uint virtualPageIndex = patchId % VGE_LUMONSCENE_VIRTUAL_COUNT;
    uint vx = virtualPageIndex & (VGE_LUMONSCENE_VIRTUAL_W - 1u);
    uint vy = virtualPageIndex / VGE_LUMONSCENE_VIRTUAL_W;

    uint packedEntry = texelFetch(vge_lumonScenePageTableMip0, ivec3(int(vx), int(vy), int(chunkSlot)), 0).x;
    uint physicalPageId = packedEntry & VGE_LUMONSCENE_PAGE_PHYS_ID_MASK;
    uint flags = packedEntry >> VGE_LUMONSCENE_PAGE_FLAG_SHIFT;

    if (physicalPageId == 0u)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    if ((flags & VGE_LUMONSCENE_FLAG_RESIDENT) == 0u)
    {
        return vec4(1.0, 0.0, 0.0, 1.0);
    }

    if ((flags & (VGE_LUMONSCENE_FLAG_NEEDS_CAPTURE | VGE_LUMONSCENE_FLAG_NEEDS_RELIGHT)) != 0u)
    {
        return vec4(1.0, 1.0, 0.0, 1.0);
    }

    // Stable random-ish color per physical page.
    uint h = Squirrel3HashU(physicalPageId);
    vec3 c = vec3(
        float((h) & 255u) / 255.0,
        float((h >> 8) & 255u) / 255.0,
        float((h >> 16) & 255u) / 255.0);
    return vec4(c, 1.0);
}

// Program entry: SceneGBuffer
vec4 RenderDebug_SceneGBuffer(vec2 screenPos)
{
    switch (debugMode)
    {
        case 4: return renderSceneDepthDebug();
        case 5: return renderSceneNormalDebug();
        case 29: return renderMaterialBandsDebug();
        case 41: return renderPomMetricsDebug();
        case 52: return renderLumonScenePageReadyDebug();
        case 53: return renderLumonScenePatchUvDebug();
        case 54: return renderLumonSceneIrradianceDebug();
        case 59: return renderLumonSceneChunkSlotDebug();
        case 60: return renderLumonSceneSlotGenerationDebug();
        case 61: return renderLumonScenePageTableOccupancyDebug();
        case 62: return renderLumonSceneMaterialDebug();
        case 63: return renderLumonSceneMaterialAtlasAllDebug();
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
