#version 450 core
layout(std140, binding = 28) uniform WaterRefractionDiagnosticInputs
{
    int diagnosticScenario;
    vec2 frameSize;
    vec3 customSurface;
    vec3 customNormal;
    int diagnosticBudget;
    int diagnosticSelect;
    int diagnosticQuality;
    int diagnosticUnderwater;
    mat4 projectionMatrix;
    mat4 inverseProjectionMatrix;
};










ivec4 receiverWork = ivec4(0);
#define VGE_REFRACTION_DEPTH_FETCH() receiverWork.x++
#define VGE_REFRACTION_COLOR_FETCH() receiverWork.y++
#define VGE_REFRACTION_RADIANCE_BLEND() receiverWork.z++
#define VGE_REFRACTION_TRIANGLE_BLEND() receiverWork.w++
int diagnosticReason = 0;
int diagnosticCount = 0;
vec3 diagnosticSample = vec3(0);
int uvCount = 0;
vec2 uvSample = vec2(0);
#define VGE_REFRACTION_EVENT(reason) diagnosticReason = reason
#define VGE_REFRACTION_SAMPLE(uv, depth) diagnosticCount++; diagnosticSample = vec3(uv, depth)
#define VGE_REFRACTION_UV_SAMPLE(uv) uvCount++; uvSample = uv

/** Converts independently supplied conventional device depth to axial metres. */
float VgeLiquidViewDepth(float depth) { return 20.0 / (100.1 - (depth * 2.0 - 1.0) * 99.9); }
@import "../includes/liquids/transport.glsl"
@import "../includes/liquids/refraction_selection.glsl"
layout(location=0) out vec4 decision;
layout(location=1) out vec4 sampled;
layout(location=2) out vec4 transport;
layout(location=3) out vec4 selection;
layout(location=4) out vec4 receiverPosition;
layout(location=5) out vec4 receiverRadiance;
layout(location=6) out vec4 receiverOperations;

/** Exposes rejection, receiver evaluation count, confidence, and selected optical geometry. */
void main()
{
    float focal = projectionMatrix[0][0];
    vec3 surface = diagnosticScenario == 8 ? vec3(10,0,-2)
        : vec3((gl_FragCoord.xy / frameSize * 2.0 - 1.0) * 2.0 / focal, -2);
    vec3 normal = diagnosticScenario == 1 || diagnosticScenario == 10 ? normalize(vec3(-.4,0,1))
        : diagnosticScenario == 8 ? normalize(vec3(-.7,0,-.714))
        : diagnosticScenario == 11 ? normalize(vec3(1,0,.01)) : vec3(0,0,1);
    if (diagnosticScenario == 12) { surface = customSurface; normal = customNormal; }
    VgeRefractionSupport seed;
    bool seedValid;
    VgeWaterReceiver receiver;
    if (diagnosticSelect == 2)
        receiver = VgeWaterUvRefraction(surface, normal, diagnosticUnderwater != 0);
    else if (diagnosticSelect != 0)
        receiver = VgeWaterSelectRefraction(surface, normal, vec3(0,0,1), diagnosticUnderwater != 0, diagnosticQuality);
    else
        receiver = VgeWaterRefraction(surface, normal, diagnosticUnderwater != 0, diagnosticBudget, seed, seedValid);
    decision = vec4(receiver.valid ? 1 : 0, diagnosticReason, diagnosticCount, receiver.confidence);
    sampled = vec4(diagnosticSample, 1);
    transport = vec4(receiver.submergedLength, receiver.refractedDirectionVS.z,
        receiver.valid ? 1.0-receiver.confidence : 1.0, receiver.positionVS.z);
    selection = vec4(receiver.method, uvCount, uvSample);
    receiverPosition = vec4(receiver.positionVS, receiver.valid ? 1 : 0);
    receiverRadiance = vec4(receiver.radiance, 1);
    receiverOperations = vec4(receiverWork);
}
