#ifndef VGE_LIQUID_WAVES_GLSL
#define VGE_LIQUID_WAVES_GLSL

layout(std140, binding = 15) uniform VgeLiquidWaveParams
{
    vec4 liquidWavePhases;
    vec4 liquidWaveConditions;
};

/** Accumulates one Gerstner band's displacement and analytic surface tangents in metres. */
void VgeAddGerstnerBand(vec2 metres, float phase, vec2 direction,
    float wavelengthMetres, float amplitudeMetres, float steepness,
    float strength, float lateral, inout vec3 displacement,
    inout vec3 tangentX, inout vec3 tangentZ)
{
    float k = 6.28318530718 / wavelengthMetres;
    float theta = k * dot(metres, direction) - phase;
    float s = sin(theta);
    float c = cos(theta);
    float horizontal = lateral * steepness * amplitudeMetres;
    float slope = amplitudeMetres * k;
    displacement += strength * vec3(direction.x * horizontal * c,
        amplitudeMetres * s, direction.y * horizontal * c);
    tangentX += strength * vec3(-direction.x * direction.x * horizontal * k * s,
        direction.x * slope * c, -direction.y * direction.x * horizontal * k * s);
    tangentZ += strength * vec3(-direction.x * direction.y * horizontal * k * s,
        direction.y * slope * c, -direction.y * direction.y * horizontal * k * s);
}

/** Extracts local wave weights so adjacent vertices interpolate their shore response. */
vec2 VgeLiquidWaveWeights(int flags, vec3 meshNormal)
{
    if ((flags & LiquidIsLavaBitMask) != 0 || meshNormal.y < 0.7) return vec2(0.0);
    bool animated = (flags & 1) != 0;
    bool weak = (flags & LiquidWeakWaveBitMask) != 0;
    if (!animated && !weak) return vec2(0.0);

    float oceanity = float((flags >> 2) & 255) / 255.0;
    float strength = mix(0.35, 1.0, oceanity)
        * (animated ? (weak ? 0.25 : 1.0) : 0.25)
        * mix(0.5, 1.5, clamp(liquidWaveConditions.x, 0.0, 1.0));
    // Reduce lateral motion against non-liquid geometry near shore flags.
    return vec2(strength, smoothstep(0.3, 0.9, oceanity));
}

/** Evaluates displacement and its analytic normal at any point on the wave surface. */
void VgeLiquidWaveSurface(vec3 relativePosition, vec2 weights,
    out vec3 displacedPosition, out vec3 surfaceNormal)
{
    displacedPosition = relativePosition;
    surfaceNormal = vec3(0.0, 1.0, 0.0);
    if (weights.x <= 0.0) return;
    float strength = weights.x;
    float lateral = weights.y;
    vec3 displacement = vec3(0.0);
    vec3 tangentX = vec3(1.0, 0.0, 0.0);
    vec3 tangentZ = vec3(0.0, 0.0, 1.0);
    vec2 metres = relativePosition.xz;
    VgeAddGerstnerBand(metres, liquidWavePhases.x,
        vec2(0.89442719, 0.44721360), 3.0, 0.012, 0.45,
        strength, lateral, displacement, tangentX, tangentZ);
    VgeAddGerstnerBand(metres, liquidWavePhases.y,
        vec2(-0.31622777, 0.94868330), 5.0, 0.018, 0.45,
        strength, lateral, displacement, tangentX, tangentZ);
    VgeAddGerstnerBand(metres, liquidWavePhases.z,
        vec2(0.70710678, -0.70710678), 8.0, 0.024, 0.45,
        strength, lateral, displacement, tangentX, tangentZ);
    VgeAddGerstnerBand(metres, liquidWavePhases.w,
        vec2(-0.85749293, -0.51449576), 13.0, 0.016, 0.45,
        strength, lateral, displacement, tangentX, tangentZ);
    displacedPosition += displacement;
    surfaceNormal = normalize(cross(tangentZ, tangentX));
}

#endif
