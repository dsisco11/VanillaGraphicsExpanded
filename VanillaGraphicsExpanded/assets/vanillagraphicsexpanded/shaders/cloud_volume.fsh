#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/cloud_volume_transport.glsl"
/** Renders the full-resolution history-free comparison without reconstruction. */
void main()
{
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration())) discard;
    vec2 uv=gl_FragCoord.xy/vgeFrame.screenSize;
    CloudPayload p=CloudEmpty(uv); CloudIntegrate(uv,p); CloudWrite(p);
}
