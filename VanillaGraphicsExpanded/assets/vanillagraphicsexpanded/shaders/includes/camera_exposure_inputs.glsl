#extension GL_ARB_shading_language_420pack : require
#ifndef VGE_CAMERA_EXPOSURE_INPUTS
#define VGE_CAMERA_EXPOSURE_INPUTS
layout(std140, binding = 28) uniform CameraExposureInputs
{
    vec4 meterRange;
    vec4 exposureRange;
    vec4 adaptation;
    vec4 percentiles;
};
#endif
