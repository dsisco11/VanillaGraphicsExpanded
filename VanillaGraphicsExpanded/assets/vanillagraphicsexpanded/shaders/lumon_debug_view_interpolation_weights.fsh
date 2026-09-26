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


/** Implements render interpolation weights debug for its explicit view entrypoint. */
vec4 renderInterpolationWeightsDebug(vec2 screenPos)
{
    // Get pixel's probe-space position
    vec2 probePos = screenPos / float(probeSpacing);
    ivec2 baseProbe = ivec2(floor(probePos));
    vec2 fracCoord = fract(probePos);

    // Bilinear base weights
    float bw00 = (1.0 - fracCoord.x) * (1.0 - fracCoord.y);
    float bw10 = fracCoord.x * (1.0 - fracCoord.y);
    float bw01 = (1.0 - fracCoord.x) * fracCoord.y;
    float bw11 = fracCoord.x * fracCoord.y;

    // Load probe validity
    ivec2 p00 = clamp(baseProbe + ivec2(0, 0), ivec2(0), ivec2(probeGridSize) - 1);
    ivec2 p10 = clamp(baseProbe + ivec2(1, 0), ivec2(0), ivec2(probeGridSize) - 1);
    ivec2 p01 = clamp(baseProbe + ivec2(0, 1), ivec2(0), ivec2(probeGridSize) - 1);
    ivec2 p11 = clamp(baseProbe + ivec2(1, 1), ivec2(0), ivec2(probeGridSize) - 1);

    float v00 = texelFetch(probeAnchorPosition, p00, 0).a;
    float v10 = texelFetch(probeAnchorPosition, p10, 0).a;
    float v01 = texelFetch(probeAnchorPosition, p01, 0).a;
    float v11 = texelFetch(probeAnchorPosition, p11, 0).a;

    // Apply validity to weights
    float w00 = bw00 * (v00 > 0.5 ? 1.0 : 0.0);
    float w10 = bw10 * (v10 > 0.5 ? 1.0 : 0.0);
    float w01 = bw01 * (v01 > 0.5 ? 1.0 : 0.0);
    float w11 = bw11 * (v11 > 0.5 ? 1.0 : 0.0);

    float totalWeight = w00 + w10 + w01 + w11;

    if (totalWeight < 0.001)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black = no valid probes
    }

    // Normalize weights
    w00 /= totalWeight;
    w10 /= totalWeight;
    w01 /= totalWeight;
    w11 /= totalWeight;

    // Visualize as color:
    // R = w00 (bottom-left, red)
    // G = w10 (bottom-right, green)
    // B = w01 + w11 (top probes, blue)
    vec3 color = vec3(w00, w10, w01 + w11);

    // Also draw probe dots for reference
    float dotRadius = max(2.0, float(probeSpacing) * 0.15);

    // Check all 4 probe positions
    for (int dy = 0; dy <= 1; dy++)
    {
        for (int dx = 0; dx <= 1; dx++)
        {
            vec2 pCenter = (vec2(baseProbe + ivec2(dx, dy)) + 0.5) * float(probeSpacing);
            float dist = length(screenPos - pCenter);
            if (dist < dotRadius)
            {
                // Color probe dot based on its weight
                float probeWeight = (dx == 0 && dy == 0) ? w00 :
                    (dx == 1 && dy == 0) ? w10 :
                    (dx == 0 && dy == 1) ? w01 : w11;
                return vec4(vec3(probeWeight), 1.0);
            }
        }
    }

    return vec4(color, 1.0);
}

/** Renders only the InterpolationWeights view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderInterpolationWeightsDebug(screenPos);
}
