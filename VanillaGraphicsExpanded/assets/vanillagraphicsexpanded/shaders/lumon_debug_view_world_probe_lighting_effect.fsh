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


/** Implements render world probe lighting effect debug for its explicit view entrypoint. */
vec4 renderWorldProbeLightingEffectDebug()
{
    // A missing paired output is shown explicitly, rather than mistaken for zero effect.
    if (!worldProbeComparisonReady)
        return vec4(0.5, 0.0, 0.5, 1.0);
    vec3 delta = texture(indirectDiffuseFull, uv).rgb - texture(worldProbeSuppressedLighting, uv).rgb;
    // Compare linear luminance before tone mapping. Gain changes only the display,
    // so weak effects can be inspected without changing either lighting branch.
    float difference = dot(delta, vec3(0.2126, 0.7152, 0.0722));
    float magnitude = abs(difference) * max(worldProbeEffectGain, 1.0);
    float brightness = magnitude / (1.0 + magnitude);
    vec3 signColor = difference >= 0.0 ? vec3(1.0, 0.35, 0.0) : vec3(0.0, 0.35, 1.0);
    return vec4(signColor * brightness, 1.0);
}

/** Renders only the WorldProbeLightingEffect view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderWorldProbeLightingEffectDebug();
}
