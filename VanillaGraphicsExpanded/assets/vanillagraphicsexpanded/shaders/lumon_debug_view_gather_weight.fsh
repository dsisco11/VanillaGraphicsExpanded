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


/** Implements render gather weight debug for its explicit view entrypoint. */
vec4 renderGatherWeightDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    float a = texture(indirectHalf, uv).a;
    float w = clamp(abs(a), 0.0, 1.0);
    // Slight curve to make low weights more visible
    w = sqrt(w);

    if (a < 0.0)
    {
        return vec4(w, 0.0, 0.0, 1.0);
    }

    return vec4(vec3(w), 1.0);
}

/** Renders only the GatherWeight view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderGatherWeightDebug();
}
