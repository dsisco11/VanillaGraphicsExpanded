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


/** Implements render velocity prev uv debug for its explicit view entrypoint. */
vec4 renderVelocityPrevUvDebug()
{
    vec4 velSample = texture(velocityTex, uv);
    vec2 v = lumonVelocityDecodeUv(velSample);
    uint flags = lumonVelocityDecodeFlags(velSample);

    if (!lumonVelocityIsValid(flags))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec2 prevUv = uv - v;
    return vec4(clamp(prevUv, 0.0, 1.0), 0.0, 1.0);
}

/** Renders only the VelocityPrevUv view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderVelocityPrevUvDebug();
}
