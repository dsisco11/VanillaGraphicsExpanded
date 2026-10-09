#version 330 core
@import "./includes/postprocess_inputs.glsl"
uniform sampler2D sourceImage;
uniform sampler2D secondaryImage;
in vec2 uv;
layout(location=0) out vec4 outColor;

/** Selects highlights in exposed luminance while retaining unexposed RGB and chromaticity. */
vec3 extractHighlight(vec3 radiance, float exposureMultiplier)
{
    radiance = max(radiance, vec3(0));
    float luminance = dot(radiance, vec3(.2126, .7152, .0722)) * exposureMultiplier;
    // Zero threshold explicitly bypasses selection, including zero-luminance samples.
    float weight = 1.0;
    if (effect.x > 0.0)
    {
        float transition = effect.x * effect.y;
        weight = transition > 0.0
            ? smoothstep(effect.x - transition, effect.x + transition, luminance)
            : step(effect.x, luminance);
    }
    return radiance * weight * effect.z;
}

/** Samples symmetric source-texel offsets; extract before averaging to retain small bright sources. */
vec3 reduceScene(bool extract, float exposureMultiplier)
{
    vec2 texel = 1.0 / vec2(textureSize(sourceImage, 0));
    vec3 sum = vec3(0);
    for (int y = 0; y < 2; ++y)
        for (int x = 0; x < 2; ++x)
        {
            vec3 sampleColor = texture(sourceImage, uv + (vec2(x, y) - .5) * texel).rgb;
            sum += extract ? extractHighlight(sampleColor, exposureMultiplier) : sampleColor;
        }
    return sum * .25;
}

/** Integrates every texel in the support through adjacent bilinear pairs, without sparse sampling gaps. */
vec3 filterGaussian(vec2 axis)
{
    vec2 texel = axis / vec2(textureSize(sourceImage, 0));
    // CPU-normalized kernel: passInfo.y is center weight; effect/sun hold pair weights/offsets.
    vec3 sum = texture(sourceImage, uv).rgb * passInfo.y;
    for (int pair = 0; pair < 4; ++pair)
    {
        if (effect[pair] <= 0.0) continue;
        vec2 offset = texel * sun[pair];
        sum += (texture(sourceImage, uv + offset).rgb
            + texture(sourceImage, uv - offset).rgb) * effect[pair];
    }
    return sum;
}

/** Reduces, filters or additively combines separately weighted Gaussian scales without display conversion. */
void main()
{
    vec3 color;
    if (passInfo.z < 1.5)
    {
        bool extract = passInfo.z < .5;
        float exposureMultiplier = 1.0;
        if (extract)
        {
            float ev = passInfo.w > .5 ? texelFetch(secondaryImage, ivec2(0), 0).r : effect.w;
            exposureMultiplier = exp2(clamp(ev, -24.0, 24.0));
        }
        color = reduceScene(extract, exposureMultiplier);
    }
    else if (passInfo.z < 3.5)
        color = filterGaussian(passInfo.z < 2.5 ? vec2(1, 0) : vec2(0, 1));
    else
    {
        // Bilinear reconstruction only resamples the already Gaussian-filtered coarse image.
        color = texture(sourceImage, uv).rgb * effect.rgb;
        if (passInfo.w > .5) color += texture(secondaryImage, uv).rgb;
    }
    outColor = vec4(color, 1);
}
