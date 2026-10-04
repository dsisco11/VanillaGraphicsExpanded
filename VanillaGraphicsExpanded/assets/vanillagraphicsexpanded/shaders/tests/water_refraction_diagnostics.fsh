#version 450 core
uniform int diagnosticScenario;
uniform vec2 frameSize;
uniform vec3 customSurface;
uniform vec3 customNormal;
mat4 projectionMatrix;
int diagnosticReason = 0;
int diagnosticCount = 0;
vec3 diagnosticSample = vec3(0);
#define VGE_REFRACTION_EVENT(reason) diagnosticReason = reason
#define VGE_REFRACTION_SAMPLE(uv, depth) diagnosticCount++; diagnosticSample = vec3(uv, depth)

/** Converts independently supplied conventional device depth to axial metres. */
float VgeLiquidViewDepth(float depth) { return 20.0 / (100.1 - (depth * 2.0 - 1.0) * 99.9); }
@import "../includes/liquids/transport.glsl"
@import "../includes/liquids/refraction.glsl"
layout(location=0) out vec4 decision;
layout(location=1) out vec4 sampled;
layout(location=2) out vec4 transport;

/** Exposes rejection, receiver evaluation count, confidence, and selected optical geometry. */
void main()
{
    float focal = diagnosticScenario == 8 ? .1 : sqrt(3.0);
    projectionMatrix = mat4(focal,0,0,0, 0,focal,0,0, 0,0,-100.1/99.9,-1, 0,0,-20.0/99.9,0);
    vec3 surface = diagnosticScenario == 8 ? vec3(10,0,-2)
        : vec3((gl_FragCoord.xy / frameSize * 2.0 - 1.0) * 2.0 / focal, -2);
    vec3 normal = diagnosticScenario == 1 || diagnosticScenario == 10 ? normalize(vec3(-.4,0,1))
        : diagnosticScenario == 8 ? normalize(vec3(-.7,0,-.714))
        : diagnosticScenario == 11 ? normalize(vec3(1,0,.01)) : vec3(0,0,1);
    if (diagnosticScenario == 12) { surface = customSurface; normal = customNormal; }
    VgeWaterReceiver receiver = VgeWaterRefraction(surface, normal, false);
    decision = vec4(receiver.valid ? 1 : 0, diagnosticReason, diagnosticCount, receiver.confidence);
    sampled = vec4(diagnosticSample, 1);
    transport = vec4(receiver.submergedLength, refract(normalize(surface), normal, 1.0/1.333).z,
        receiver.valid ? 1.0-receiver.confidence : 1.0, receiver.positionVS.z);
}
