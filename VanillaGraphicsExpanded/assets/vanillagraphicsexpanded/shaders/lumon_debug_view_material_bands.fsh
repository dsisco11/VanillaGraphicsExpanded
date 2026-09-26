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


/** Implements render material bands debug for its explicit view entrypoint. */
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

/** Renders only the MaterialBands view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderMaterialBandsDebug();
}
