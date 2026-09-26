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
@import "./includes/debug/heatmap.glsl"

/** Implements render velocity magnitude debug for its explicit view entrypoint. */
vec4 renderVelocityMagnitudeDebug()
{
    vec4 velSample = texture(velocityTex, uv);
    vec2 v = lumonVelocityDecodeUv(velSample);
    uint flags = lumonVelocityDecodeFlags(velSample);

    if (!lumonVelocityIsValid(flags))
    {
        // Invalid velocity: dark red.
        return vec4(0.25, 0.0, 0.0, 1.0);
    }

    float mag = lumonVelocityMagnitude(v);
    float denom = max(velocityRejectThreshold, 1e-6);
    float t = clamp(mag / denom, 0.0, 1.0);
    return vec4(heatmap(t), 1.0);
}

/** Renders only the VelocityMagnitude view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderVelocityMagnitudeDebug();
}
