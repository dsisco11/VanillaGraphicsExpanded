// Debug Mode 26-28: Velocity Debug Views (Phase 14)

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

vec4 renderVelocityPrevUvDebug()
{
    vec4 velSample = texture(velocityTex, uv);
    vec2 v = lumonVelocityDecodeUv(velSample);
    uint flags = lumonVelocityDecodeFlags(velSample);

    if (!lumonVelocityIsValid(flags))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec2 prevUv = uv - v;
    return vec4(clamp(prevUv, 0.0, 1.0), 0.0, 1.0);
}

// Program entry: Velocity
vec4 RenderDebug_Velocity(vec2 screenPos)
{
    switch (debugMode)
    {
        case 26: return renderVelocityMagnitudeDebug();
        case 27: return renderVelocityValidityDebug();
        case 28: return renderVelocityPrevUvDebug();
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
