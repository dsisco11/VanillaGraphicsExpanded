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
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/vge_tonemap_reinhard.glsl"

/** Implements render direct total debug for its explicit view entrypoint. */
vec4 renderDirectTotalDebug()
{
    vec3 dd = texture(directDiffuse, uv).rgb;
    vec3 ds = texture(directSpecular, uv).rgb;
    return vec4(vgeTonemapReinhard(dd + ds), 1.0);
}

/** Renders only the DirectTotal view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderDirectTotalDebug();
}
