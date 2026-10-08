#ifndef VGE_CAMERA_EXPOSURE_INPUTS
#define VGE_CAMERA_EXPOSURE_INPUTS
layout(std140) uniform CameraExposureInputs
{
    vec4 meterRange;
    vec4 exposureRange;
    vec4 adaptation;
    vec4 percentiles;
};
#endif
