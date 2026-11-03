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
    int motionBatchCount;
    vec4 motionSurfaces[64];
    vec4 motionSampleUvs[64];
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
    int motionIndex = int(gl_FragCoord.x);
    if (motionBatchCount < 0 || motionBatchCount > 64 || (motionBatchCount > 0 && motionIndex >= motionBatchCount)) discard;
    vec3 position = vec3(0), radiance = vec3(0);
    vec2 selectedUv = motionBatchCount > 0 ? motionSampleUvs[motionIndex].xy : sampleUv;
    vec3 selectedSurface = motionBatchCount > 0 ? motionSurfaces[motionIndex].xyz : surfaceVS;
    bool valid = VgeRefractionFilter(selectedUv, selectedSurface, normalVS, inverseProjectionMatrix, position, radiance);
    receiverPosition = vec4(position, valid ? 1 : 0);
    receiverColor = vec4(radiance, valid ? 1 : 0);
    receiverOperations = vec4(receiverWork);
}
