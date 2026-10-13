#ifndef VGE_FRAME_LIGHTING_GLSL
#define VGE_FRAME_LIGHTING_GLSL
#extension GL_ARB_shader_storage_buffer_object : require
#extension GL_ARB_shader_image_load_store : require
/** Frozen physical-light storage publication; UI/offscreen programs do not bind this block. */
// The engine's vertex face data uses slot three; both stages must coexist in native draws.
layout(std430, binding=4) readonly buffer FrozenFrameLighting { vec4 lightingStorage; uvec4 lightingIdentity; };
/** Admits only the current frame and calibration, so retired or partially prepared lighting cannot be consumed. */
bool VgeFrameLightingValid(uint frame, uint calibrationGeneration)
{
    return lightingStorage.w > .5 && lightingIdentity.x == frame && calibrationGeneration != 0u
        && lightingIdentity.y == calibrationGeneration && lightingStorage.x >= 7.147944e-6
        && !isnan(lightingStorage.x) && !isinf(lightingStorage.x) && lightingStorage.y > 0.0
        && !isinf(lightingStorage.x * 16.0)
        && !isnan(lightingStorage.y) && !isinf(lightingStorage.y);
}
/** Encodes outgoing nits or integrated incident illumination into this frame's transient storage. */
vec3 VgeStoreFrameLight(vec3 physical) { return physical * lightingStorage.x; }
/** Decodes transient lighting for physical evaluation or metering. */
vec3 VgePhysicalFrameLight(vec3 stored) { return stored * lightingStorage.y; }
/** Converts admitted fixed-scale cache data into transient lighting without a duplicate physical conversion. */
vec3 VgePersistentToFrameLight(vec3 stored) { return stored * (lightingStorage.x * 16.0); }
/** Limits only visible disk storage, preserving physical solar diagnostics and source illumination. */
bool VgeStoreVisibleSolarDisk(vec3 physicalNits, float maximumScale, out vec3 stored)
{
    stored = vec3(0);
    if (lightingStorage.x <= 0.0 || isnan(lightingStorage.x) || isinf(lightingStorage.x)
        || any(isnan(physicalNits)) || any(isinf(physicalNits)) || any(lessThan(physicalNits, vec3(0)))
        || isnan(maximumScale) || isinf(maximumScale) || maximumScale <= 0.0)
        return false;
    // The CPU computes and rounds the disk limit down before publication. Selecting
    // it before multiplication keeps the fragment path finite without precise arithmetic.
    float scale = min(lightingStorage.x, maximumScale);
    stored = physicalNits * scale;
    return !any(isnan(stored)) && !any(isinf(stored));
}
/** Corrects old transient samples before filtering; validity and originating bounds are checked by the history owner. */
bool VgeRescaleFrameHistory(vec3 history, float historyScale, float maximumStored, out vec3 current)
{
    float ratio = lightingStorage.x / historyScale;
    current = history * ratio;
    return historyScale > 0.0 && !isnan(historyScale) && !isinf(historyScale)
        && ratio > 0.0 && !isnan(ratio) && !isinf(ratio)
        && maximumStored > 0.0 && !isnan(maximumStored) && !isinf(maximumStored)
        && !any(isnan(current)) && !any(isinf(current)) && all(lessThanEqual(abs(current), vec3(maximumStored)));
}
#endif
