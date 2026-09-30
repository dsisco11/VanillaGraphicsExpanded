#version 450 core
layout(location = 0) in vec3 xyz;
layout(location = 3) in int renderFlags;
layout(location = 6) in int waterFlagsIn;

layout(std140, binding = 12) uniform VgeLiquidDepthFrameParams
{
    mat4 projectionMatrix;
};
layout(std140, binding = 14) uniform VgeLiquidDrawParams
{
    mat4 modelViewMatrix;
    vec4 liquidOrigin;
};

@import "./includes/vertex_flags.glsl"
@import "./includes/liquids/waves.glsl"

/** Writes the shared displaced liquid mesh to the engine-owned liquid-depth target. */
void main()
{
    vec3 relativePosition = xyz + liquidOrigin.xyz;
    vec3 displacedPosition;
    vec3 waveNormal;
    vec2 waveWeights = VgeLiquidWaveWeights(waterFlagsIn, unpackNormal(renderFlags));
    VgeLiquidWaveSurface(relativePosition, waveWeights, displacedPosition, waveNormal);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(displacedPosition, 1.0);
}
