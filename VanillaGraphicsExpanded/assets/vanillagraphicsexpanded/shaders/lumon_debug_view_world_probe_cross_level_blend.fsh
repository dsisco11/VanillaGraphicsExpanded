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

/** Implements render world probe cross level blend debug for its explicit view entrypoint. */
vec4 renderWorldProbeCrossLevelBlendDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;

    int levels = VGE_LUMON_WORLDPROBE_LEVELS;
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;
    if (levels <= 0 || resolution <= 0) return vec4(0.0, 0.0, 0.0, 1.0);

    // Depth reconstruction and the rebased clipmap origins share the current terrain render origin.
    vec3 posRel = posWS;
    int level = lumonWorldProbeSelectLevelByExtents(posRel, baseSpacing, levels, resolution);
    float spacingL = lumonWorldProbeSpacing(baseSpacing, level);

    vec3 originL = lumonWorldProbeGetOriginMinCorner(level);
    vec3 localL = (posRel - originL) / max(spacingL, 1e-6);
    float edgeDist = lumonWorldProbeDistanceToBoundaryProbeUnits(localL, resolution);
    float wL = lumonWorldProbeCrossLevelBlendWeight(edgeDist, 2.0, 2.0);

    float levelN = (levels > 1) ? float(level) / float(max(levels - 1, 1)) : 0.0;
    // R = selected level (normalized), G = weight for L, B = weight for L+1.
    return vec4(levelN, wL, 1.0 - wL, 1.0);
#endif
}

/** Renders only the WorldProbeCrossLevelBlend view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderWorldProbeCrossLevelBlendDebug();
}
