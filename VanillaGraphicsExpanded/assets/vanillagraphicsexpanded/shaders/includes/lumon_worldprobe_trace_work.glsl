#ifndef LUMON_WORLDPROBE_TRACE_WORK_GLSL
#define LUMON_WORLDPROBE_TRACE_WORK_GLSL
/** One shared integer-origin probe and its sparse directional selection range. */
struct WorldProbeTraceProbe
{
    ivec4 origin;
    vec4 fractionDistance;
    uvec4 selection; // octahedral edge, first direction, count, traversal steps
    vec4 nearbyDistance;
};
layout(std430,binding=4) readonly buffer WorldProbeTraceProbes { WorldProbeTraceProbe probes[]; };
#endif
