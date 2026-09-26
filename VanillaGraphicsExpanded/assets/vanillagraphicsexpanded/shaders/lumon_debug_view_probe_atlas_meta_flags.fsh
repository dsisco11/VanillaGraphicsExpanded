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


/** Implements render probe atlas meta flags debug for its explicit view entrypoint. */
vec4 renderProbeAtlasMetaFlagsDebug()
{
    float conf;
    uint flags;
    lumonDecodeMeta(texture(probeAtlasMeta, uv).rg, conf, flags);

    float hit = (flags & LUMON_META_HIT) != 0u ? 1.0 : 0.0;
    float sky = (flags & LUMON_META_SKY_MISS) != 0u ? 1.0 : 0.0;
    float world = (flags & LUMON_META_WORLDPROBE_FALLBACK) != 0u ? 1.0 : 0.0;

    // Encode additional bits as brightness boost so they pop without hiding base flags.
    float exit = (flags & LUMON_META_SCREEN_EXIT) != 0u ? 0.25 : 0.0;
    float early = (flags & LUMON_META_EARLY_TERMINATED) != 0u ? 0.25 : 0.0;
    float thick = (flags & LUMON_META_THICKNESS_UNCERT) != 0u ? 0.25 : 0.0;

    vec3 base = vec3(hit, sky, world);
    base = clamp(base + vec3(exit + early + thick), 0.0, 1.0);
    return vec4(base, 1.0);
}

/** Renders only the ProbeAtlasMetaFlags view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeAtlasMetaFlagsDebug();
}
