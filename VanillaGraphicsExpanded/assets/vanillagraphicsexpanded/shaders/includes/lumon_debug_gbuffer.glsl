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
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // Simple tonemap for preview.
    vec3 c = irr / (irr + vec3(1.0));
    return vec4(clamp(c, 0.0, 1.0), 1.0);
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
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
