#ifndef VGE_CAMERA_EXPOSURE_BUFFER
#define VGE_CAMERA_EXPOSURE_BUFFER
/** Borrows the published GPU history; manual draws do not read this optional block. */
layout(std430, binding=0) readonly buffer CameraExposure { float cameraExposureEV; };
#endif
