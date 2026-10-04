#version 450 core
uniform vec3 surfaceVS;
uniform vec3 normalVS;
uniform mat4 projectionMatrix;
uniform mat4 inverseProjectionMatrix;
uniform vec2 frameSize;
uniform int underwater;
int uvLookups = 0;
vec2 firstUv = vec2(0), lastUv = vec2(0);
#define VGE_REFRACTION_UV_SAMPLE(uv) if (uvLookups == 0) firstUv = uv; lastUv = uv; uvLookups++
@import "../includes/liquids/transport.glsl"
@import "../includes/liquids/uv_distortion.glsl"
layout(location=0) out vec4 decision;
layout(location=1) out vec4 receiverPosition;
layout(location=2) out vec4 receiverRadiance;
layout(location=3) out vec4 refractedDirection;
layout(location=4) out vec4 sampleCoordinates;

/** Exposes the unmodified approximate receiver and its water transport inputs. */
void main()
{
    VgeWaterReceiver receiver = VgeWaterUvRefraction(surfaceVS, normalVS, underwater != 0);
    decision = vec4(receiver.valid ? 1 : 0, receiver.method, receiver.submergedLength, receiver.confidence);
    receiverPosition = vec4(receiver.positionVS, 1);
    receiverRadiance = vec4(receiver.radiance, 1);
    refractedDirection = vec4(receiver.refractedDirectionVS, uvLookups);
    sampleCoordinates = vec4(firstUv, lastUv);
}
