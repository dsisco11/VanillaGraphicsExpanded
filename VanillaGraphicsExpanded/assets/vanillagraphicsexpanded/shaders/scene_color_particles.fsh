#version 330 core

uniform sampler2D visibilityDepth;
uniform sampler2D beforeDepth;
uniform sampler2D afterDepth;
uniform sampler2D particleColor;

layout(location = 0) out float receiverDepth;
layout(location = 1) out vec4 visibleParticles;

/** Restores material depth only where the original particle draw still owns visibility. */
void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    float current = texelFetch(visibilityDepth, pixel, 0).r;
    float before = texelFetch(beforeDepth, pixel, 0).r;
    float after = texelFetch(afterDepth, pixel, 0).r;
    vec4 particles = texelFetch(particleColor, pixel, 0);
    // All three depth images use the same 32-bit depth representation and projection.
    // A later closer opaque draw replaces both the receiver and particle coverage.
    bool survives = current == after;
    receiverDepth = survives && after < before ? before : current;
    visibleParticles = survives ? particles : vec4(0.0);
}
