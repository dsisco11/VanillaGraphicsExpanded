#version 450 core
uniform vec2 sampleUv;
uniform vec3 surfaceVS;
uniform vec3 normalVS;
uniform mat4 inverseProjection;
uniform vec2 frameSize;
@import "../includes/liquids/receiver.glsl"
layout(location=0) out vec4 receiverPosition;
layout(location=1) out vec4 receiverColor;

/** Exposes validity and both outputs of the unmodified production receiver filter. */
void main()
{
    vec3 position = vec3(0), radiance = vec3(0);
    bool valid = VgeRefractionFilter(sampleUv, surfaceVS, normalVS, inverseProjection, position, radiance);
    receiverPosition = vec4(position, valid ? 1 : 0);
    receiverColor = vec4(radiance, valid ? 1 : 0);
}
