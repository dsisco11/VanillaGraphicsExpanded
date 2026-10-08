#ifndef LUMON_DEBUG_RENDER_LUMON_SCENE_IRRADIANCE_DEBUG_GLSL
#define LUMON_DEBUG_RENDER_LUMON_SCENE_IRRADIANCE_DEBUG_GLSL


/** Implements render lumon scene irradiance debug for its explicit view entrypoint. */
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
#endif
