#version 330 core
#extension GL_ARB_shading_language_420pack : require
#extension GL_ARB_shader_storage_buffer_object : require
#extension GL_ARB_shader_image_load_store : require
@import "./includes/camera_exposure_buffer.glsl"
@import "./includes/pbr_frame_lighting.glsl"
@import "./includes/post_storage.glsl"
layout(std140, binding = 28) uniform BloomInputs {
    vec4 passInfo; vec4 effect; vec4 sun; vec4 solar;
    vec4 localControls; vec4 localOptions;
};
#if VGE_LOCAL_EXPOSURE
@import "./includes/post_local_exposure.glsl"
uniform sampler2D localBaseImage;
#endif
uniform sampler2D sourceImage;
uniform sampler2D secondaryImage;
in vec2 uv;
layout(location=0) out vec4 outColor;

/** Selects highlights in exposed luminance while retaining unexposed RGB and chromaticity. */
vec3 extractHighlight(vec3 radiance, vec2 coordinate, float ev, float exposureMultiplier)
{
    radiance = max(radiance, vec3(0));
#if VGE_LOCAL_EXPOSURE
    // Correct the emitting sample before thresholding and spreading its energy.
    // Subsequent scales and destination pixels must not reapply this gain.
    float q, base;
    if (localOptions.y > .5 && VgeLocalLogLuminance(VgePhysicalFrameLight(radiance), q)
        && VgeSampleLocalBase(localBaseImage, coordinate, q, localOptions.x, base))
        radiance *= exp2(VgeLocalExposureStops(base, ev, localOptions.z, localControls.xy, localControls.zw));
#endif
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
vec3 reduceScene(bool extract, float ev, float exposureMultiplier)
{
    vec2 texel = 1.0 / vec2(textureSize(sourceImage, 0));
    vec3 sum = vec3(0);
    for (int y = 0; y < 2; ++y)
        for (int x = 0; x < 2; ++x)
        {
            vec2 coordinate = clamp(uv + (vec2(x, y) - .5) * texel, vec2(0), vec2(1));
            vec3 sampleColor = VgePostStoredLight(texture(sourceImage, coordinate).rgb);
            // Weight before accumulation so a reduction never creates a larger transient sum.
            sum += (extract ? extractHighlight(sampleColor, coordinate, ev, exposureMultiplier) : sampleColor) * .25;
        }
    return sum;
}

/** Integrates every texel in the support through adjacent bilinear pairs, without sparse sampling gaps. */
vec3 filterGaussian(vec2 axis)
{
    vec2 texel = axis / vec2(textureSize(sourceImage, 0));
    // CPU-normalized kernel: passInfo.y is center weight; effect/sun hold pair weights/offsets.
    vec3 sum = VgePostStoredLight(texture(sourceImage, uv).rgb) * passInfo.y;
    for (int pair = 0; pair < 4; ++pair)
    {
        if (effect[pair] <= 0.0) continue;
        vec2 offset = texel * sun[pair];
        sum += VgePostStoredLight(texture(sourceImage, uv + offset).rgb) * effect[pair]
            + VgePostStoredLight(texture(sourceImage, uv - offset).rgb) * effect[pair];
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
        float ev = 0.0;
        if (extract)
        {
            ev = passInfo.w > .5 ? cameraExposureEV : effect.w;
            exposureMultiplier = VgePostExposure(ev);
        }
        color = reduceScene(extract, ev, exposureMultiplier);
    }
    else if (passInfo.z < 3.5)
        color = filterGaussian(passInfo.z < 2.5 ? vec2(1, 0) : vec2(0, 1));
    else
    {
        // Bilinear reconstruction only resamples the already Gaussian-filtered coarse image.
        color = VgePostStoredLight(texture(sourceImage, uv).rgb) * effect.rgb;
        if (passInfo.w > .5) color += VgePostStoredLight(texture(secondaryImage, uv).rgb);
    }
    outColor = vec4(VgePostStoredLight(color), 1);
}
