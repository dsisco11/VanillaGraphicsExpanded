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


/** Implements render sh coefficients debug for its explicit view entrypoint. */
vec4 renderSHCoefficientsDebug(vec2 screenPos)
{
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);

    vec4 posData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = posData.a;

    if (valid < 0.1)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black for invalid probes
    }

    // Load SH data from both textures
    vec4 sh0 = texelFetch(radianceTexture0, probeCoord, 0);
    vec4 sh1 = texelFetch(radianceTexture1, probeCoord, 0);

    // Unpack SH coefficients
    vec4 shR, shG, shB;
    shUnpackFromTextures(sh0, sh1, shR, shG, shB);

    // DC terms (ambient/average radiance) - stored in first coefficient
    vec3 dc = vec3(shR.x, shG.x, shB.x);

    // Directional magnitude - sum of absolute values of directional coefficients
    float dirMagR = abs(shR.y) + abs(shR.z) + abs(shR.w);
    float dirMagG = abs(shG.y) + abs(shG.z) + abs(shG.w);
    float dirMagB = abs(shB.y) + abs(shB.z) + abs(shB.w);
    float dirMag = (dirMagR + dirMagG + dirMagB) / 3.0;

    // Visualize: DC as base color, directional as brightness boost
    vec3 color = dc + vec3(dirMag * 0.5);

    // Apply tone mapping for HDR values
    color = color / (color + vec3(1.0));

    return vec4(color, 1.0);
}

/** Renders only the ShCoefficients view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderSHCoefficientsDebug(screenPos);
}
