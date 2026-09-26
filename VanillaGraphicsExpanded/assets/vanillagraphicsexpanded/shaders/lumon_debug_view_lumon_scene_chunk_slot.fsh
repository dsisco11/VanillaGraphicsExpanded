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


/** Implements render lumon scene chunk slot debug for its explicit view entrypoint. */
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

/** Renders only the LumonSceneChunkSlot view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderLumonSceneChunkSlotDebug();
}
