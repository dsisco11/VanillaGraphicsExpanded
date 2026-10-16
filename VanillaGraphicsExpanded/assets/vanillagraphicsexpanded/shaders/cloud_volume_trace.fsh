#version 330 core
#extension GL_ARB_separate_shader_objects : require
#define VGE_CLOUD_PHYSICAL
@import "./includes/cloud_volume_transport.glsl"
@import "./includes/cloud_temporal_parameters.glsl"
layout(location=6) out vec4 outCloudDepth;
/** Traces a rotating two-by-two phase, retaining neutral pixels and complete OIT/depth metadata. */
void main()
{
    vec2 uv=CloudTraceUv(ivec2(gl_FragCoord.xy),cloudTrace.zw);
    CloudPayload p=CloudEmpty(uv);
    if(VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration())) CloudIntegrate(uv,p);
    CloudWrite(p); outCloudDepth=p.depth;
}
