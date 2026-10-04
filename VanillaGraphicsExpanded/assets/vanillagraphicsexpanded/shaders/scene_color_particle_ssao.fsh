#version 330 core

uniform sampler2D visibilityDepth;
uniform sampler2D beforeDepth;
uniform sampler2D afterDepth;
uniform sampler2D particleNormal;
uniform sampler2D particlePosition;

layout(location = 0) out vec4 receiverNormal;
layout(location = 1) out vec4 receiverPosition;

/** Restores original particle SSAO metadata after material lighting has consumed the underlying receiver. */
void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    float current = texelFetch(visibilityDepth, pixel, 0).r;
    float before = texelFetch(beforeDepth, pixel, 0).r;
    float after = texelFetch(afterDepth, pixel, 0).r;
    // Even zero-alpha particles can write visibility depth in the engine. Match
    // that metadata ownership, but retain later geometry and untouched sky pixels.
    if (current != after || after >= before) discard;
    receiverNormal = texelFetch(particleNormal, pixel, 0);
    receiverPosition = texelFetch(particlePosition, pixel, 0);
}
