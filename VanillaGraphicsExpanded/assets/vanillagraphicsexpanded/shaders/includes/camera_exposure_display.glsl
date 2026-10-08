#ifndef VGE_CAMERA_EXPOSURE_DISPLAY
#define VGE_CAMERA_EXPOSURE_DISPLAY
uniform sampler2D vge_cameraExposure;
uniform int vge_cameraExposureEnabled;
uniform float vge_cameraManualEV;

/** Applies the camera multiplier before the shared tone curve; radiance inputs remain unmodified. */
vec3 VgeExposeCamera(vec3 radiance)
{
    float exposureEV = vge_cameraManualEV;
    if (vge_cameraExposureEnabled != 0) exposureEV = texelFetch(vge_cameraExposure, ivec2(0), 0).r;
    return radiance * exp2(exposureEV);
}
#endif
