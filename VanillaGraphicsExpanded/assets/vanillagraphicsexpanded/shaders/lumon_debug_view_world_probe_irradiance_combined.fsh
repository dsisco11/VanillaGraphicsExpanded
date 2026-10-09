#version 330 core
@import "./includes/gbuffer_layers.glsl"

vec2 uv;
out vec4 outColor;

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_sh.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"
@import "./includes/velocity_common.glsl"
@import "./includes/lumon_pbr.glsl"
@import "./includes/vge_global_defines.glsl"
@import "./includes/squirrel3.glsl"
@import "./includes/lumon_worldprobe.glsl"
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/lumon_world_probe_debug_disabled_color.glsl"
@import "./includes/debug/lumon_world_probe_debug_tone_map.glsl"

/** Implements render world probe irradiance combined debug for its explicit view entrypoint. */
vec4 renderWorldProbeIrradianceCombinedDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, vgeFrame.invProjectionMatrix);
    vec3 posWS = (vgeFrame.invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferSurface, vec3(uv, VGE_SURFACE_NORMAL)).xyz);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    vec3 color = lumonWorldProbeDebugToneMap(wp.irradiance);
    return vec4(color, 1.0);
#endif
}

/** Renders only the WorldProbeIrradianceCombined view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / vgeFrame.screenSize;
    vec2 screenPos = uv * vgeFrame.screenSize;
    outColor = renderWorldProbeIrradianceCombinedDebug();
}
