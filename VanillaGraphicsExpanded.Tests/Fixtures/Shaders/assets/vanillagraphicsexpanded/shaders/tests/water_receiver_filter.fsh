#version 450 core
@import "../includes/vge_frame_ubo.glsl"
// Imported optical kernels reuse the universal camera without private fixture copies.
#define projectionMatrix (vgeFrame.projectionMatrix)
#define inverseProjectionMatrix (vgeFrame.invProjectionMatrix)
#define frameSize (vgeFrame.screenSize)
layout(std140, binding = 28) uniform WaterReceiverFilterInputs
{
    vec2 sampleUv;
    vec3 surfaceVS;
    vec3 normalVS;
};





ivec4 receiverWork = ivec4(0);
#define VGE_REFRACTION_DEPTH_FETCH() receiverWork.x++
#define VGE_REFRACTION_COLOR_FETCH() receiverWork.y++
#define VGE_REFRACTION_RADIANCE_BLEND() receiverWork.z++
#define VGE_REFRACTION_TRIANGLE_BLEND() receiverWork.w++
@import "../includes/liquids/receiver.glsl"
layout(location=0) out vec4 receiverPosition;
layout(location=1) out vec4 receiverColor;
layout(location=2) out vec4 receiverOperations;

/** Exposes validity and both outputs of the unmodified production receiver filter. */
void main()
{
    vec3 position = vec3(0), radiance = vec3(0);
    bool valid = VgeRefractionFilter(sampleUv, surfaceVS, normalVS, inverseProjectionMatrix, position, radiance);
    receiverPosition = vec4(position, valid ? 1 : 0);
    receiverColor = vec4(radiance, valid ? 1 : 0);
    receiverOperations = vec4(receiverWork);
}
