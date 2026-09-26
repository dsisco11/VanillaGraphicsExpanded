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


/** Implements render probe atlas temporal rejection debug for its explicit view entrypoint. */
vec4 renderProbeAtlasTemporalRejectionDebug()
{
    float conf;
    uint flags;
    lumonDecodeMeta(texture(probeAtlasMeta, uv).rg, conf, flags);

    // Priority ordering: show the most actionable rejection reason.
    if ((flags & LUMON_META_TEMPREJ_REPROJ_OOB) != 0u)
    {
        return vec4(1.0, 0.0, 0.0, 1.0); // Red = reprojection out of bounds
    }

    if ((flags & LUMON_META_TEMPREJ_VELOCITY_TOO_LARGE) != 0u)
    {
        return vec4(1.0, 1.0, 0.0, 1.0); // Yellow = velocity too large
    }

    if ((flags & LUMON_META_TEMPREJ_HITDIST_DELTA) != 0u)
    {
        return vec4(1.0, 0.5, 0.0, 1.0); // Orange = hit-distance delta reject
    }

    if ((flags & LUMON_META_TEMPREJ_HIT_CLASS_MISMATCH) != 0u)
    {
        return vec4(1.0, 0.0, 1.0, 1.0); // Magenta = hit/miss classification changed
    }

    if ((flags & LUMON_META_TEMPREJ_CONFIDENCE_LOW) != 0u)
    {
        return vec4(0.8, 0.2, 0.8, 1.0); // Purple = low history confidence
    }

    if ((flags & LUMON_META_TEMPREJ_HISTORY_INVALID) != 0u)
    {
        return vec4(0.5, 0.0, 0.5, 1.0); // Dark purple = no valid history
    }

    if ((flags & LUMON_META_TEMPREJ_VELOCITY_INVALID) != 0u)
    {
        return vec4(0.0, 0.4, 1.0, 1.0); // Blue = velocity invalid (fell back to non-velocity path)
    }

    // No rejection bits set => history considered valid.
    return vec4(0.0, 1.0, 0.0, 1.0); // Green
}

/** Renders only the ProbeAtlasTemporalRejection view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeAtlasTemporalRejectionDebug();
}
