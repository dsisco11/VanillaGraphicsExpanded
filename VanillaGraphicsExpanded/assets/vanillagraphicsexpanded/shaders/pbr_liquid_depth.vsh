#version 330 core
#extension GL_ARB_shading_language_420pack : require
layout(location = 0) in vec3 xyz;
layout(location = 3) in uint renderFlagsPacked;
layout(location = 6) in uint waterFlagsPacked;

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
    // Match unsigned engine storage, then preserve the shared signed flag decoding contract.
    int renderFlags = int(renderFlagsPacked);
    int waterFlagsIn = int(waterFlagsPacked);
    vec3 relativePosition = xyz + liquidOrigin.xyz;
    vec3 displacedPosition;
    vec3 waveNormal;
    vec2 waveWeights = VgeLiquidWaveWeights(waterFlagsIn, unpackNormal(renderFlags));
    VgeLiquidWaveSurface(relativePosition, waveWeights, displacedPosition, waveNormal);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(displacedPosition, 1.0);
}
