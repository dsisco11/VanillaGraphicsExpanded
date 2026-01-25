// Debug Mode 10-11: Indirect debug views

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

// Program entry: Indirect
vec4 RenderDebug_Indirect(vec2 screenPos)
{
    switch (debugMode)
    {
        case 10: return renderRadianceOverlayDebug();
        case 11: return renderGatherWeightDebug();
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
