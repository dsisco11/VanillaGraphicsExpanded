#ifndef SURFACE_WORK_DIAGNOSTICS_GLSL
#define SURFACE_WORK_DIAGNOSTICS_GLSL
// Offsets match SurfaceWorkCounter. The caller supplies a uniform enabled flag;
// disabled or saturated instrumentation never accesses an unbound counter buffer.
layout(std430, binding=4) buffer SurfaceDiagnostics { uint surfaceCounters[]; };
const uint SD_TEXELS=0u, SD_COMPLETED=1u, SD_UNCHANGED=2u, SD_EMPTY=3u, SD_HIDDEN=4u;
const uint SD_ORIGIN_OUTSIDE=5u, SD_ORIGIN_UNPUBLISHED=6u, SD_ORIGIN_UNSUPPORTED=7u;
const uint SD_UNSEEDED=8u, SD_NONFINITE=9u, SD_RAYS=10u, SD_HIT=11u, SD_SKY=12u;
const uint SD_OUTSIDE=13u, SD_UNPUBLISHED=14u, SD_UNSUPPORTED=15u, SD_BUDGET=16u;
const uint SD_DISTANCE=17u, SD_HIT_MATERIAL=18u, SD_HIT_LIGHTING=19u;
const uint SD_CAPTURE_OUTSIDE=20u, SD_CAPTURE_UNPUBLISHED=21u, SD_CAPTURE_MATERIAL=22u;
/** Records one event in its documented texel or ray units. */
void surfaceCount(uint counter) { if (SURFACE_DIAGNOSTICS_ENABLED) atomicAdd(surfaceCounters[counter],1u); }
/** Classifies unavailable origin geometry without treating unsupported shapes as sky. */
void surfaceOriginFailure(int status)
{
    surfaceCount(status == TRACE_SCENE_OUTSIDE ? SD_ORIGIN_OUTSIDE :
        status == TRACE_SCENE_UNSUPPORTED ? SD_ORIGIN_UNSUPPORTED : SD_ORIGIN_UNPUBLISHED);
}
#endif
