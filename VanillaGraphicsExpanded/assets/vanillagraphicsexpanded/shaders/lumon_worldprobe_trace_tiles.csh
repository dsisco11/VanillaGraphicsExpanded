#version 430 core
@import "./includes/lumon_worldprobe_trace_work.glsl"
layout(local_size_x=64) in;
layout(std430,binding=5) writeonly buffer WorldProbeTraceTiles { uvec2 tiles[]; };
layout(std430,binding=7) buffer WorldProbeTraceDispatch { uint groupsX; uint groupsY; uint groupsZ; };
/** Builds compact probe/tile references and the dispatch count entirely on the GPU. */
void main()
{
    uint probe=gl_GlobalInvocationID.x;
    if(probe>=uint(probes.length())) return;
    uint count=(probes[probe].selection.z+63u)>>6u;
    uint first=atomicAdd(groupsX,count);
    for(uint tile=0u;tile<count;tile++) tiles[first+tile]=uvec2(probe,tile<<6u);
}
