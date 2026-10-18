#version 330 core
#extension GL_ARB_separate_shader_objects : require
// Test registry overlay: only explicit oracle UBO settings exceed the production 384-step cap.
#define VGE_CLOUD_PRIMARY_LIMIT 768
@import "./includes/cloud_volume_transport.glsl"
/** Executes the production reference integrator with an opt-in higher test-only loop ceiling. */
void main()
{
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration())) discard;
    vec2 uv=gl_FragCoord.xy/vgeFrame.screenSize;
    CloudPayload p=CloudEmpty(uv); CloudIntegrate(uv,p); CloudWrite(p);
}
