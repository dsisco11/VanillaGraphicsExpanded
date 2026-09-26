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


/** Implements render velocity validity debug for its explicit view entrypoint. */
vec4 renderVelocityValidityDebug()
{
    vec4 velSample = texture(velocityTex, uv);
    uint flags = lumonVelocityDecodeFlags(velSample);

    if (lumonVelocityIsValid(flags))
    {
        // Valid: green
        return vec4(0.0, 1.0, 0.0, 1.0);
    }

    // Invalid: show a reason tint when available.
    if ((flags & LUMON_VEL_FLAG_HISTORY_INVALID) != 0u) return vec4(1.0, 0.0, 1.0, 1.0); // magenta
    if ((flags & LUMON_VEL_FLAG_SKY_OR_INVALID_DEPTH) != 0u) return vec4(0.0, 0.5, 1.0, 1.0); // blue
    if ((flags & LUMON_VEL_FLAG_PREV_BEHIND_CAMERA) != 0u) return vec4(1.0, 0.0, 0.0, 1.0); // red
    if ((flags & LUMON_VEL_FLAG_PREV_OOB) != 0u) return vec4(1.0, 1.0, 0.0, 1.0); // yellow
    if ((flags & LUMON_VEL_FLAG_NAN) != 0u) return vec4(0.0, 1.0, 1.0, 1.0); // cyan

    return vec4(0.25, 0.25, 0.25, 1.0);
}

/** Renders only the VelocityValidity view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderVelocityValidityDebug();
}
