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


/** Implements render lumon scene material roughness debug for its explicit view entrypoint. */
vec4 renderLumonSceneMaterialRoughnessDebug()
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

    uint surfaceId = VgeLumonSceneUnpackSurfaceIdFromMaterialAtlas(mat);
    uvec4 s = texelFetch(vge_lumonSceneSurfaceLut, VgeLumonSceneSurfaceLutUv(surfaceId), 0);
    float roughness = float(s.w) * (1.0 / 255.0);
    return vec4(vec3(clamp(roughness, 0.0, 1.0)), 1.0);
}

/** Renders only the LumonSceneMaterialRoughness view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderLumonSceneMaterialRoughnessDebug();
}
