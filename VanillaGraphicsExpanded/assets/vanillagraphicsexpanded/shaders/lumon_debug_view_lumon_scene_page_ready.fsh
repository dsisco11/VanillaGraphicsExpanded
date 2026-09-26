#version 330 core

vec2 uv;
out vec4 outColor;

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_sh.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"
@import "./includes/velocity_common.glsl"
@import "./includes/lumon_pbr.glsl"
@import "./includes/vge_global_defines.glsl"
@import "./includes/squirrel3.glsl"
@import "./includes/lumonscene_surface_cache.glsl"
@import "./includes/lumonscene_material_packing.glsl"
@import "./includes/lumon_debug_uniforms.glsl"


/** Implements render lumon scene page ready debug for its explicit view entrypoint. */
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

/** Renders only the LumonScenePageReady view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderLumonScenePageReadyDebug();
}
