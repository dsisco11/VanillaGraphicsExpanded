#ifndef VGE_FRAME_UBO_GLSL
#define VGE_FRAME_UBO_GLSL
#extension GL_ARB_shading_language_420pack : require
@import "./vge_ubo_bindings.glsl"

/** Universal camera/view snapshot; CPU packing is owned by VgeFrameUniformBuffer. */
layout(std140, binding = VGE_UBO_FRAME_BINDING) uniform VgeFrameUBO
{
    mat4 projectionMatrix;
    mat4 viewMatrix;
    mat4 invProjectionMatrix;
    mat4 invViewMatrix;

    mat4 prevViewProjMatrix;
    mat4 currViewProjMatrix;

    vec2 screenSize;
    float timeSeconds;
    uint frameIndex;

    vec3 cameraPosWS;
    float fogMinimum;

    // fogColor.xyz, fogDensity.w
    vec4 fog0;
    mat4 invCurrViewProjMatrix;
    vec2 clipPlanes;
    float deltaTime;
    uint frameFlags; // Bit zero rejects temporal camera history.
    // Absolute render origin split into 32-block chunks and a bounded remainder.
    ivec4 renderOriginChunkCoord;
    vec4 renderOriginBlockRemainder;
} vgeFrame;
#endif
