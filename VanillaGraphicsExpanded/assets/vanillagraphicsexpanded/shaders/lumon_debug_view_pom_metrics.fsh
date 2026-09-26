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


/** Implements render pom metrics debug for its explicit view entrypoint. */
vec4 renderPomMetricsDebug()
{
    // Patched chunk shaders optionally write a scalar diagnostic into gBufferNormal.w.
    // Interpretation is controlled by MaterialAtlas.PomDebugMode.
    float v = clamp(texture(gBufferNormal, uv).w, 0.0, 1.0);
    vec3 c = vec3(1.0 - v, v, 0.0);
    return vec4(c, 1.0);
}

/** Renders only the PomMetrics view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderPomMetricsDebug();
}
