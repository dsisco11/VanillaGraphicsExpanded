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
@import "./includes/debug/compute_composite_split.glsl"

/** Implements render composite indirect diffuse debug for its explicit view entrypoint. */
vec4 renderCompositeIndirectDiffuseDebug()
{
    float ao;
    float roughness;
    float metallic;
    vec3 diff;
    vec3 spec;
    computeCompositeSplit(ao, roughness, metallic, diff, spec);
    return vec4(diff, 1.0);
}

/** Renders only the CompositeIndirectDiffuse view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderCompositeIndirectDiffuseDebug();
}
