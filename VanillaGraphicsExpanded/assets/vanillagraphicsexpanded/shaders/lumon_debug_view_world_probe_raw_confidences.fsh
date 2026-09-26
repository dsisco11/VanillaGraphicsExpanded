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
@import "./includes/lumon_worldprobe.glsl"
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/lumon_world_probe_debug_disabled_color.glsl"

/** Implements render world probe raw confidences debug for its explicit view entrypoint. */
vec4 renderWorldProbeRawConfidencesDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    // Final confidence from gather (screen-first blend result).
    float sumW = clamp(texture(indirectHalf, uv).a, 0.0, 1.0);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    float worldConf = clamp(wp.confidence, 0.0, 1.0);

    // Reconstruct the screen confidence (screenW) from sumW and worldConf.
    // This matches the gather blend:
    //   sumW = screenW + worldConf * (1 - screenW)
    float screenConf = (worldConf >= 0.999)
        ? 0.0
        : clamp((sumW - worldConf) / max(1.0 - worldConf, 1e-6), 0.0, 1.0);

    return vec4(screenConf, worldConf, sumW, 1.0);
#endif
}

/** Renders only the WorldProbeRawConfidences view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderWorldProbeRawConfidencesDebug();
}
