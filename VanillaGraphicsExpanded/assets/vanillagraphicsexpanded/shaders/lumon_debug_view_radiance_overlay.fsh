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


/** Implements render radiance overlay debug for its explicit view entrypoint. */
vec4 renderRadianceOverlayDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // indirectHalf is a half-resolution HDR buffer. Sample in normalized UVs;
    // the hardware sampler handles the resolution mismatch.
    vec3 rad = texture(indirectHalf, uv).rgb;

    // Simple Reinhard tone map for visualization
    vec3 color = rad / (rad + vec3(1.0));
    return vec4(color, 1.0);
}

/** Renders only the RadianceOverlay view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderRadianceOverlayDebug();
}
