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


/** Implements render probe grid debug for its explicit view entrypoint. */
vec4 renderProbeGridDebug(vec2 screenPos)
{
    // Sample the scene as background
    float depth = texture(primaryDepth, uv).r;
    vec3 baseColor = vec3(0.1);

    if (!lumonIsSky(depth))
    {
        // Show darkened scene as background
        vec3 normal = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);
        baseColor = normal * 0.3 + 0.2;
    }

    // Calculate which probe cell this pixel is in
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));

    // Calculate the center of this probe cell in screen space
    vec2 probeCenter = (vec2(probeCoord) + 0.5) * float(probeSpacing);

    // Distance from pixel to probe center
    float dist = length(screenPos - probeCenter);

    // Probe dot radius
    float dotRadius = max(3.0, float(probeSpacing) * 0.25);

    // Draw probe dots
    if (dist < dotRadius)
    {
        // Clamp probe coord to valid range
        if (probeCoord.x >= 0 && probeCoord.y >= 0 &&
            probeCoord.x < int(probeGridSize.x) && probeCoord.y < int(probeGridSize.y))
        {
            // Sample probe validity from anchor texture
            vec4 probeData = texelFetch(probeAnchorPosition, probeCoord, 0);
            float valid = probeData.a;

            // Color by validity
            vec3 probeColor;
            if (valid > 0.9)
            {
                probeColor = vec3(0.0, 1.0, 0.0);  // Green = fully valid
            }
            else if (valid > 0.4)
            {
                probeColor = vec3(1.0, 1.0, 0.0);  // Yellow = edge (partial validity)
            }
            else
            {
                probeColor = vec3(1.0, 0.0, 0.0);  // Red = invalid
            }

            // Smooth edge falloff
            float alpha = smoothstep(dotRadius, dotRadius * 0.5, dist);

            return vec4(mix(baseColor, probeColor, alpha), 1.0);
        }
    }

    // Draw grid lines between probes
    vec2 gridPos = mod(screenPos, float(probeSpacing));
    float lineWidth = 1.0;
    if (gridPos.x < lineWidth || gridPos.y < lineWidth)
    {
        return vec4(mix(baseColor, vec3(0.5), 0.4), 1.0);
    }

    return vec4(baseColor, 1.0);
}

/** Renders only the ProbeGrid view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderProbeGridDebug(screenPos);
}
