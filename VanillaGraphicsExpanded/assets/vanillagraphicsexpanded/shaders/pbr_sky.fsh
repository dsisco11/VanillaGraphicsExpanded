#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/atmosphere_sky_inputs.glsl"
@import "./includes/pbr_color.glsl"
@import "./includes/atmosphere_sky_mapping.glsl"
#define windWaveCounter skyEffects.z
#define psychedelicStrength skyEffects.y
@import "./includes/perception_fragment.glsl"
uniform sampler2D skyLookup;
uniform sampler2D liquidDepth;
layout(location = 0) out vec4 outColor;
layout(location = 1) out vec4 outGlow;

/** Reproduces the engine's bounded horizon-noise seed. */
float skyHash(float n) { return fract(sin(n) * 1e4); }
/** Interpolates the engine's horizon fog noise without altering the atmospheric LUT. */
float skyNoise(vec3 p)
{
    vec3 i = floor(p), f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    vec3 step = vec3(110, 241, 171);
    float n = dot(i, step);
    return mix(mix(mix(skyHash(n), skyHash(n+110),u.x),
                   mix(skyHash(n+241),skyHash(n+351),u.x),u.y),
               mix(mix(skyHash(n+171),skyHash(n+281),u.x),
                   mix(skyHash(n+412),skyHash(n+522),u.x),u.y),u.z);
}
/** Preserves twilight star visibility and the engine horizon/flat-fog opacity policy. */
float skyCoverage(vec3 skyPosition, float compatibilityDepth)
{
    vec3 direction = normalize(skyPosition + vec3(0, .25 * skyDayWeather.z, 0));
    float start = skyFog.z < 0 ? skyFog.w : 0;
    float curvature = skyFog.z < 0 ? .55 * skyDayWeather.z : 0;
    float amount = max(skyFog.y + max(skyFog.x * 120 - .12, 0)
        + max(-skyFog.z * compatibilityDepth * start / 3.3, 0),
        1 - exp(-(skyPosition.y - start - curvature) * skyFog.z));
    float density = ((1 - direction.y) / 2 + .3)
        * (.6 + skyNoise(direction + vec3(skyDayWeather.w, 0, -skyDayWeather.w)) / 3);
    amount += max(0, density - .5) * max(min(1, 5 * amount), skyDayWeather.y);
    return mix(clamp(amount, 0, 1), 1.0, clamp(skyDayWeather.x, 0, 1));
}
/** Reconstructs the published Rayleigh and directional Mie rows without a seam or row bleed. */
vec3 skyRadiance(vec3 direction)
{
    float azimuth = dot(direction.xz,direction.xz) > .0000001 ? atan(direction.z,direction.x) : 0;
    float row = atmSkyCoordinate(asin(clamp(direction.y,-1,1)), skySunHorizon.w);
    float rows = float(textureSize(skyLookup,0).y / 2);
    vec2 uv = vec2(azimuth / 6.28318530718, (row * (rows-1) + .5) / (2*rows));
    return texture(skyLookup,uv).rgb + texture(skyLookup,uv+vec2(0,.5)).rgb
        * atmMieFactor(direction,skySunHorizon.xyz);
}
/** Uses the engine normalized linear-depth convention for the three shoreline samples. */
float skyLiquidDistance(float offset)
{
    float depth = texture(liquidDepth,(gl_FragCoord.xy+vec2(0,offset))/skyDepthFrame.zw).r;
    return 2 * skyDepthFrame.x / (skyDepthFrame.y + skyDepthFrame.x
        - (2*depth-1) * (skyDepthFrame.y-skyDepthFrame.x));
}
/** Emits one chosen color convention; alpha and glow never undergo transfer. */
void main()
{
    // Unproject two finite depths so perspective and orthographic views share a ray contract.
    vec2 ndc = gl_FragCoord.xy / skyDepthFrame.zw * 2.0 - 1.0;
    vec4 nearPoint = skyInverseViewProjection * vec4(ndc, -1, 1);
    vec4 middlePoint = skyInverseViewProjection * vec4(ndc, 0, 1);
    vec3 direction = normalize(middlePoint.xyz / middlePoint.w - nearPoint.xyz / nearPoint.w);
    // Legacy spatial effects retain their 250-unit scale without a tessellated dome.
    vec3 skyPosition = direction * 250.0;
    vec4 effectClip = skyViewProjection * vec4(skyPosition, 1);
    float compatibilityDepth = abs(effectClip.w) > 0.000001
        ? clamp(effectClip.z / effectClip.w * .5 + .5, 0.0, 1.0) : 1.0;
    bool linearScene = skyEffects.w != 0;
    vec3 radiance = skyRadiance(direction);
    vec4 color = vec4(linearScene ? radiance : VgeResolveDisplay(radiance), skyCoverage(skyPosition, compatibilityDepth));
    // Perception is spatial: preserve its location and alpha before sky blending.
    if (psychedelicStrength > .001) color = applyPsychedelicEffect(color,skyPosition/2,0);
    float murk = skyMurk.w > .7 ? 0 : 1-(skyLiquidDistance(0)+skyLiquidDistance(3)+skyLiquidDistance(6))/3;
    murk = clamp(murk-14*skyFog.x,0,1);
    vec3 tint = skyMurk.rgb * .4;
    vec3 night = vec3(.1,.5,.1) * skyEffects.x;
    color.rgb = mix(color.rgb,linearScene ? VgeSrgbToLinear(tint) : tint,murk)
        + (linearScene ? VgeSrgbToLinear(night) : night);
    outColor = vec4(linearScene ? max(color.rgb,vec3(0)) : VgeDitherDisplay(color.rgb,gl_FragCoord.xy),color.a);
    outGlow = vec4(0,0,0,1);
}
