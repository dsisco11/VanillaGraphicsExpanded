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


/** Implements render lumon scene page table occupancy debug for its explicit view entrypoint. */
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

/** Renders only the LumonScenePageTableOccupancy view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderLumonScenePageTableOccupancyDebug();
}
